using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

// Operasyonel sipariş işlemleri tüm servis personeli (SysAdmin/Kasiyer/Garson) — Mutfak hariç.
// Para/hassas uçlar (treat/discount/pay/settle) ayrıca yalnız Kasiyer+Admin (metot-bazlı [Authorize] AND ile daraltır).
[Route("api/orders")]
[Authorize(Roles = "SysAdmin,Kasiyer,Garson,Yönetici")]
public class OrdersController(PosNotifier notifier) : PosControllerBase
{
    private const string PaymentRoles = "SysAdmin,Kasiyer";   // para işlemleri: ikram/iskonto
    private const string SettleRoles = "SysAdmin,Kasiyer,Yönetici";   // ödeme/settle: Yönetici de tahsilat kapatabilir
    private readonly OrderService _orders = new();
    private readonly CustomerService _customers = new();
    private readonly PosCatalogService _catalog = new();
    private readonly ProductService _products = new();

    // ===== Idempotency (offline kuyruk + çift-tık emici katman) =====
    // İstemci (PWA outbox / retry) aynı mutasyonu 'Idempotency-Key' header'ıyla birden çok gönderebilir
    // (ağ hatası sonrası replay, Background Sync). Aynı key ile ikinci istek → İLK isteğin sonucunu döndürür;
    // yeni sipariş/ödeme OLUŞTURMAZ. MVP: process-içi statik MemoryCache — bu web tek-süreç host'ta çalışır
    // (WindowsService), Program.cs'e dokunmadan DI'ya bağımsız çalışır; 24 saat TTL yeterli (aynı vardiya penceresi).
    private static readonly IMemoryCache _idem = new MemoryCache(new MemoryCacheOptions());
    private static readonly TimeSpan IdemTtl = TimeSpan.FromHours(24);
    private sealed record IdemEntry(int Status, object? Body);

    // Yalnızca BAŞARILI (2xx) yanıtı önbelleğe alır (hatalar tekrar denenebilsin). Key tenant+şube ile izole.
    private string? IdemKey()
        => Request.Headers.TryGetValue("Idempotency-Key", out var v) && !string.IsNullOrWhiteSpace(v)
            ? "idem:" + CompanyCode + ":" + BranchId() + ":" + v.ToString() : null;

    private void IdemCapture(string ck, IActionResult? result, object? value)
    {
        int status; object? body;
        if (result is ObjectResult obj) { status = obj.StatusCode ?? 200; body = obj.Value; }
        else if (result is StatusCodeResult scr) { status = scr.StatusCode; body = null; }
        else if (result is null && value is not null) { status = 200; body = value; }   // ActionResult<T> doğrudan T döndü
        else return;
        if (status is >= 200 and < 300) _idem.Set(ck, new IdemEntry(status, body), IdemTtl);
    }

    private ActionResult<T> Idempotent<T>(Func<ActionResult<T>> action)
    {
        var ck = IdemKey();
        if (ck is null) return action();
        if (_idem.TryGetValue(ck, out IdemEntry? hit) && hit is not null)
            return new ObjectResult(hit.Body) { StatusCode = hit.Status };
        var r = action();
        IdemCapture(ck, r.Result, r.Value);
        return r;
    }

    private async Task<ActionResult<T>> IdempotentAsync<T>(Func<Task<ActionResult<T>>> action)
    {
        var ck = IdemKey();
        if (ck is null) return await action();
        if (_idem.TryGetValue(ck, out IdemEntry? hit) && hit is not null)
            return new ObjectResult(hit.Body) { StatusCode = hit.Status };
        var r = await action();
        IdemCapture(ck, r.Result, r.Value);
        return r;
    }

    // IDOR koruması: kaynak çağıranın JWT şubesine ait mi? (şirket-içi şubeler aynı DB'yi paylaşır)
    private bool OwnsOrder(int orderId) => _orders.GetOrderBranch(orderId) == BranchId();
    private bool OwnsLine(int lineId) => _orders.GetLineBranch(lineId) == BranchId();
    private bool OwnsTable(int tableId) => _orders.GetTableBranch(tableId) == BranchId();

    /// <summary>Masadaki açık adisyon + kalemleri (yoksa order=null).</summary>
    [HttpGet("by-table/{tableId:int}")]
    public ActionResult<object> ByTable(int tableId)
    {
        if (!OwnsTable(tableId)) return NotFound();
        var order = _orders.GetOpenByTable(tableId);
        if (order is null) return Ok(new { order = (object?)null, lines = Array.Empty<object>() });
        return Ok(new { order, lines = _orders.GetLines(order.Id) });
    }

    /// <summary>Açık adisyon kalemleri.</summary>
    [HttpGet("{id:int}/lines")]
    public ActionResult<IEnumerable<OrderLine>> Lines(int id) => OwnsOrder(id) ? Ok(_orders.GetLines(id)) : NotFound();

    /// <summary>Masayı aç (perakende PA cari ile yeni adisyon).</summary>
    [HttpPost("open")]
    public ActionResult<Order> Open([FromBody] OpenOrderRequest req) => Idempotent<Order>(() =>
    {
        if (!OwnsTable(req.TableId)) return NotFound();   // IDOR: başka şubenin masası açılamaz
        var existing = _orders.GetOpenByTable(req.TableId);
        if (existing is not null) return Ok(existing);

        var pa = _customers.GetAll().FirstOrDefault(c => c.Code == "PA");
        if (pa is null) return BadRequest("Varsayılan 'PA' (perakende) cari kartı bulunamadı.");

        var order = _orders.CreateOrder(BranchId(), req.ZoneId, pa.Id, new[] { req.TableId }, UserId);
        Notify();
        return Ok(order);
    });

    /// <summary>Adisyona ürün ekle (+). Fiyat/KDV menüden gelir.</summary>
    [HttpPost("{id:int}/lines")]
    public ActionResult<OrderLine> AddLine(int id, [FromBody] AddLineRequest req) => Idempotent<OrderLine>(() =>
    {
        var order = _orders.GetById(id);
        if (order is null || order.BranchId != BranchId()) return NotFound("Adisyon bulunamadı.");
        // Fiyat/KDV SUNUCUDAN (menü) çözülür — istemci tutarına güvenilmez (fiyat manipülasyonu önlenir).
        var prod = _catalog.GetProducts(BranchId()).FirstOrDefault(p => p.Id == req.ProductId);
        if (prod is null) return BadRequest("Ürün bulunamadı.");
        if (prod.IsSalesStopped) return BadRequest("Ürün satışa kapalı.");
        if (req.Qty <= 0) return BadRequest(new { message = "Miktar 0'dan büyük olmalı." });
        // Ücretli opsiyon farkı (priceDelta) SUNUCUDAN doğrulanır — istemcinin gönderdiği tutara güvenilmez.
        // Yalnız BU ürüne ait geçerli opsiyon ID'leri sayılır (başka ürünün opsiyonu / uydurma ID yok sayılır).
        var delta = OptionPricing.Delta(_products.GetOptions(req.ProductId), req.OptionIds);
        var line = _orders.AddLine(id, order.ZoneId, req.ProductId, req.Qty, prod.Price + delta, prod.TaxRate,
            req.Note, req.SeatNo, UserId);
        return Ok(line);
    });

    /// <summary>Kalem adedini değiştir (+/-). qty=0 ise kalemi sil.</summary>
    [HttpPatch("lines/{lineId:int}/qty")]
    public IActionResult ChangeQty(int lineId, [FromBody] ChangeQtyRequest req)
    {
        if (!OwnsLine(lineId)) return NotFound();
        if (req.Qty <= 0) _orders.CancelLine(lineId, UserId, returnToStock: true);
        else _orders.ChangeQty(lineId, req.Qty, UserId);
        return Ok();
    }

    /// <summary>Kalemi iptal et (sebep + stoğa iade seçeneği).</summary>
    [HttpDelete("lines/{lineId:int}")]
    public IActionResult CancelLine(int lineId, [FromQuery] string? reason, [FromQuery] bool returnToStock = true)
    {
        if (!OwnsLine(lineId)) return NotFound();
        _orders.CancelLine(lineId, UserId, returnToStock, reason);
        return Ok();
    }

    /// <summary>Adisyon ekranından ürün eklemeden çıkıldığında çağrılır: açık ama (ödenecek) ürünü olmayan
    /// adisyonu siler → masa "Dolu" kalmaz. Boş değilse no-op (idempotent).</summary>
    [HttpPost("{id:int}/delete-if-empty")]
    public async Task<IActionResult> DeleteIfEmpty(int id)
    {
        if (!OwnsOrder(id)) return NotFound();
        try { _orders.DeleteIfEmpty(id, UserId); } catch { /* boş değilse/yarışta sessiz geç */ }
        await Notify();
        return Ok();
    }

    /// <summary>Bekleyen kalemleri mutfağa gönder → KDS'e anlık bildirim.</summary>
    [HttpPost("{id:int}/send")]
    public Task<ActionResult<IEnumerable<KitchenTicket>>> Send(int id) => IdempotentAsync<IEnumerable<KitchenTicket>>(async () =>
    {
        if (!OwnsOrder(id)) return NotFound();
        var tickets = _orders.SendToKitchen(id, UserId);
        // Reyon yönlendirmesi olmayan ürünler de mutfak ekranında görünür → her gönderimde KDS'i uyar.
        await notifier.NewKitchenTicket(CompanyCode, BranchId(), tickets);
        // HEDEFLİ alarm: reyon-geneli KDS'ler + gönderilen kalemlerin distinct reyonlarına özel KDS'ler.
        // ReyonId=0 (yönlendirmesiz) → mutfak-geneli KitchenGroup zaten kapsar. Mevcut NewTicket tazele davranışı KORUNUR.
        await notifier.KitchenNew(CompanyCode, BranchId(), tickets.Select(t => t.ReyonId).Distinct(),
            new { tableCode = tickets.FirstOrDefault()?.TableCodes, reyonIds = tickets.Select(t => t.ReyonId).Distinct() });
        return Ok(tickets);
    });

    /// <summary>Adisyonu öde/kapat (nakit/kart/yemek çeki, çoklu).</summary>
    [HttpPost("{id:int}/pay")]
    [Authorize(Roles = SettleRoles)]
    public async Task<ActionResult<ClFiche>> Pay(int id, [FromBody] PayRequest req)
    {
        if (!OwnsOrder(id)) return NotFound();
        int payCur = new CurrencyService().GetDefaultId() ?? 1;
        var lines = req.Payments.Select(p => new PaymentLine
        {
            Kind = (PaymentKind)p.Kind,
            Amount = p.Amount,
            InstrumentId = p.InstrumentId,
            CurrencyId = p.CurrencyId > 0 ? p.CurrencyId : payCur,
            PayDate = DateTime.UtcNow
        }).ToList();
        ClFiche fiche;
        try { fiche = _orders.SettleLines(id, null, lines, req.OverrideCustomerId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify();
        return Ok(fiche);
    }

    /// <summary>PC'deki ödeme: çok yöntemli (Nakit/Kart/Yemek Çeki/Açık Hesap) + kısmi + kişi(koltuk) bazlı.
    /// topLineIds null → tüm açık satırlar. overrideCustomerId → açık hesap için cari.</summary>
    [HttpPost("{id:int}/settle")]
    [Authorize(Roles = SettleRoles)]
    public Task<ActionResult<ClFiche>> Settle(int id, [FromBody] SettleRequest req) => IdempotentAsync<ClFiche>(async () =>
    {
        if (!OwnsOrder(id)) return NotFound();
        int cur = new CurrencyService().GetDefaultId() ?? 1;
        var lines = req.Payments.Select(p => new PaymentLine
        {
            Kind = (PaymentKind)p.Kind,
            Amount = p.Amount,
            InstrumentId = p.InstrumentId,
            CurrencyId = p.CurrencyId > 0 ? p.CurrencyId : cur,
            PayDate = DateTime.UtcNow
        }).ToList();
        ClFiche fiche;
        try { fiche = _orders.SettleLines(id, req.TopLineIds, lines, req.OverrideCustomerId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify();
        return Ok(fiche);
    });

    /// <summary>POS satır iadesi (ödenmiş satır, kısmi miktar): negatif ClFiche + KDV + ödeme iadesi; stok geri/fire.
    /// Yalnız iade-yetkili persona (Patron/İade/Kasiyer/SysAdmin).</summary>
    [HttpPost("lines/{lineId:int}/refund")]
    public async Task<IActionResult> Refund(int lineId, [FromBody] RefundRequest req)
    {
        // İade yetkisi WPF Permissions.Refund ile BİREBİR: yalnız SysAdmin veya 'İade' rolü.
        // (İade rolü kasiyere additive verilir; düz Kasiyer/Patron tek başına iade yapamaz — WPF ile aynı.)
        if (!(Session.IsSysAdmin || Session.HasRole("İade") || Session.HasRole("Yönetici") || Session.HasRole("Kasiyer")))
            return Forbid();
        if (!OwnsLine(lineId)) return NotFound();   // IDOR: başka şubenin satırı iade edilemez
        ClFiche fiche;
        try { fiche = _orders.RefundLine(lineId, req.RefundQty, req.ReturnToStock, (PaymentKind)req.RefundKind, req.InstrumentId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify();
        return Ok(fiche);
    }

    /// <summary>İkram (satır bedava).</summary>
    [HttpPost("lines/{lineId:int}/treat")]
    [Authorize(Roles = PaymentRoles)]
    public IActionResult Treat(int lineId, [FromBody] TreatRequest req) { if (!OwnsLine(lineId)) return NotFound(); _orders.SetTreat(lineId, req.IsTreat, UserId); return Ok(); }

    /// <summary>İskonto (satır indirim tutarı).</summary>
    [HttpPost("lines/{lineId:int}/discount")]
    [Authorize(Roles = PaymentRoles)]
    public IActionResult Discount(int lineId, [FromBody] DiscountRequest req) { if (!OwnsLine(lineId)) return NotFound(); _orders.SetLineDiscount(lineId, req.Amount, UserId); return Ok(); }

    /// <summary>Satır mutfak notu.</summary>
    [HttpPost("lines/{lineId:int}/note")]
    public IActionResult Note(int lineId, [FromBody] NoteRequest req) { if (!OwnsLine(lineId)) return NotFound(); _orders.UpdateLineNote(lineId, req.Note, UserId); return Ok(); }

    /// <summary>Masa taşı (adisyonu başka masaya).</summary>
    [HttpPost("{id:int}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveRequest req) { if (!OwnsOrder(id) || !OwnsTable(req.TargetTableId)) return NotFound(); try { _orders.MoveOrder(id, req.TargetTableId, UserId); } catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); } await Notify(); return Ok(); }

    /// <summary>Masa Böl: seçili kalemleri BOŞ hedef masada yeni adisyona böl.</summary>
    [HttpPost("{id:int}/split")]
    public async Task<ActionResult<Order>> Split(int id, [FromBody] SplitRequest req)
    {
        if (!OwnsOrder(id) || !OwnsTable(req.TargetTableId)) return NotFound();
        Order o;
        try { o = _orders.SplitOrder(id, req.LineIds ?? new(), req.TargetTableId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify(); return Ok(o);
    }

    /// <summary>Masa Birleştir: kaynak adisyonu hedef adisyona birleştir (masalar hedefe geçer, kaynak kapanır).</summary>
    [HttpPost("{id:int}/merge")]
    public async Task<IActionResult> Merge(int id, [FromBody] MergeRequest req)
    {
        if (!OwnsOrder(id) || !OwnsOrder(req.TargetOrderId)) return NotFound();
        try { _orders.MergeOrders(id, req.TargetOrderId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify(); return Ok();
    }

    /// <summary>Kişi/Adisyon Aktar: seçili kalemleri var olan başka bir adisyona taşı.</summary>
    [HttpPost("{id:int}/transfer-lines")]
    public async Task<IActionResult> TransferLines(int id, [FromBody] TransferLinesRequest req)
    {
        if (!OwnsOrder(id) || !OwnsOrder(req.TargetOrderId)) return NotFound();
        try { _orders.MoveLines(id, req.LineIds ?? new(), req.TargetOrderId, UserId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        await Notify(); return Ok();
    }

    /// <summary>Kişi sayısı.</summary>
    [HttpPost("{id:int}/cover")]
    public IActionResult Cover(int id, [FromBody] CoverRequest req) { if (!OwnsOrder(id)) return NotFound(); _orders.SetCoverCount(id, req.Count, UserId); return Ok(); }

    /// <summary>Adisyon başlık notu (Masa Notu).</summary>
    [HttpPost("{id:int}/order-note")]
    public async Task<IActionResult> OrderNote(int id, [FromBody] NoteRequest req) { if (!OwnsOrder(id)) return NotFound(); _orders.SetOrderNote(id, req.Note, UserId); await Notify(); return Ok(); }

    /// <summary>Adisyon alerji notu (kırmızı ALERJİ rozeti).</summary>
    [HttpPost("{id:int}/allergy")]
    public async Task<IActionResult> Allergy(int id, [FromBody] NoteRequest req) { if (!OwnsOrder(id)) return NotFound(); _orders.SetAllergyNote(id, req.Note, UserId); await Notify(); return Ok(); }

    /// <summary>Adisyon ÖNCELİK / VIP bayrakları (KDS rozetleri).</summary>
    [HttpPost("{id:int}/flags")]
    public async Task<IActionResult> Flags(int id, [FromBody] OrderFlagsRequest req) { if (!OwnsOrder(id)) return NotFound(); _orders.SetOrderFlags(id, req.Vip, req.Priority, UserId); await Notify(); return Ok(); }

    /// <summary>Şubedeki hesabı alınmamış (açık / ödeme bekleyen / kısmi) masalar — arama yapmadan toplu görünüm.</summary>
    [HttpGet("unpaid")]
    public ActionResult<IEnumerable<UnpaidTable>> Unpaid() => Ok(_orders.GetUnpaidTables(BranchId()));

    /// <summary>Mutfak fişini yeniden bas (tüm gönderilmiş satırlar).</summary>
    [HttpPost("{id:int}/reprint")]
    public IActionResult Reprint(int id) { if (!OwnsOrder(id)) return NotFound(); _orders.Reprint(id, UserId); return Ok(); }

    /// <summary>Tek kalemin mutfak fişini yeniden bas (satır-bazlı reprint).</summary>
    [HttpPost("lines/{lineId:int}/reprint")]
    public IActionResult ReprintLine(int lineId) { if (!OwnsLine(lineId)) return NotFound(); _orders.ReprintLine(lineId, UserId); return Ok(); }

    /// <summary>Kapanmış (tam ödenmiş) adisyonlar — verilen gün (yerel). İade geçmişi için.</summary>
    [HttpGet("closed")]
    public ActionResult<IEnumerable<Order>> Closed([FromQuery] DateTime? date) =>
        Ok(_orders.GetClosedOrders(BranchId(), date ?? DateTime.Now));

    // ===== Paket / Gel-Al / Kurye / Teslimat =====

    /// <summary>Paket / Gel-Al siparişi aç (PA cari + şubenin ilk lokasyonu). Ürünler normal AddLine ile eklenir.</summary>
    [HttpPost("package")]
    public async Task<ActionResult<Order>> CreatePackage([FromBody] CreatePackageRequest req)
    {
        var type = (OrderType)req.Type;
        if (type != OrderType.Paket && type != OrderType.TakeAway)
            return BadRequest(new { message = "Geçersiz paket türü (Paket / Gel-Al)." });
        if (!Enum.IsDefined(typeof(DeliveryPlatform), req.Platform))
            return BadRequest(new { message = "Geçersiz teslimat platformu." });
        if (type == OrderType.Paket && string.IsNullOrWhiteSpace(req.Address))
            return BadRequest(new { message = "Paket servis için adres zorunludur." });

        var pa = _customers.GetAll().FirstOrDefault(c => c.Code == "PA");
        if (pa is null) return BadRequest(new { message = "Varsayılan 'PA' (perakende) cari kartı bulunamadı." });

        var zones = new ZoneService().GetAll();
        var zone = zones.FirstOrDefault(z => z.BranchId == BranchId() && z.IsActive)
                   ?? zones.FirstOrDefault(z => z.BranchId == BranchId());
        if (zone is null) return BadRequest(new { message = "Şubede tanımlı lokasyon (zone) yok." });

        var order = _orders.CreatePackage(BranchId(), zone.Id, pa.Id,
            (DeliveryPlatform)req.Platform, type, req.Name, req.Phone, req.Address, UserId);
        await Notify();
        return Ok(order);
    }

    /// <summary>Paket/Gel-Al listesi. delivered=false → aktif (Alındı/Hazırlanıyor/Yolda); true → bugün tamamlanan (Teslim/İptal).</summary>
    [HttpGet("packages")]
    public ActionResult<IEnumerable<Order>> Packages([FromQuery] bool delivered = false) =>
        Ok(_orders.GetPackages(BranchId(), delivered));

    /// <summary>Teslimat durumu değiştir (Alındı→Hazırlanıyor→Yola Çıktı→Teslim / İptal).</summary>
    [HttpPost("{id:int}/delivery-status")]
    public async Task<IActionResult> DeliveryStatus(int id, [FromBody] DeliveryStatusRequest req)
    {
        if (!OwnsOrder(id)) return NotFound();
        if (!Enum.IsDefined(typeof(DeliveryStatus), req.Status))
            return BadRequest(new { message = "Geçersiz teslimat durumu." });
        _orders.SetDeliveryStatus(id, (DeliveryStatus)req.Status, UserId);
        // Online platformdan gelen sipariş ise durumu platforma da geri bildir (best-effort, FAIL-CLOSED/simülasyon).
        // Platformdan gelmeyen (telefon/gel-al) siparişlerde map yoktur → sessizce atlanır; mevcut akış BOZULMAZ.
        try { new DeliveryInboundService().SyncStatusToPlatform(id, (DeliveryStatus)req.Status, UserId); } catch { }
        await Notify();
        return Ok();
    }

    /// <summary>Kurye ata (durum otomatik Yola Çıktı'ya yükselir).</summary>
    [HttpPost("{id:int}/courier")]
    public async Task<IActionResult> AssignCourier(int id, [FromBody] AssignCourierRequest req)
    {
        if (!OwnsOrder(id)) return NotFound();
        _orders.AssignCourier(id, req.CourierId, UserId);
        await Notify();
        return Ok();
    }

    /// <summary>Adisyona uygulanabilir + uygulanmış kampanyalar.</summary>
    [HttpGet("{id:int}/campaigns")]
    public ActionResult<IEnumerable<CampaignDto>> Campaigns(int id)
    {
        if (!OwnsOrder(id)) return NotFound();
        // ZORUNLU kampanyalar: kriter karşılanınca OTOMATİK uygula (WPF RefreshCampaigns paritesi) — sonra GÜNCEL durumu dön.
        try { _orders.AutoApplyMandatoryCampaigns(id, BranchId(), UserId); } catch { }
        var applied = _orders.GetAppliedCampaignIds(id).ToHashSet();
        var active = new CampaignService().GetActive(BranchId(), DateTime.Now);
        return Ok(active.Select(c => new CampaignDto(c.Id, c.Name, applied.Contains(c.Id), _orders.IsCampaignEligible(id, c), c.IsMandatory)));
    }

    /// <summary>Kampanya uygula.</summary>
    [HttpPost("{id:int}/campaigns/{cid:int}")]
    public async Task<IActionResult> ApplyCampaign(int id, int cid) { if (!OwnsOrder(id)) return NotFound(); try { _orders.ApplyCampaign(id, cid, BranchId(), UserId); } catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); } await Notify(); return Ok(); }

    /// <summary>Uygulanan kampanyaları kaldır.</summary>
    [HttpDelete("{id:int}/campaigns")]
    public async Task<IActionResult> RemoveCampaign(int id) { if (!OwnsOrder(id)) return NotFound(); _orders.RemoveCampaign(id, UserId); await Notify(); return Ok(); }

    private Task Notify() => notifier.TableChanged(CompanyCode, BranchId(), new { branch = BranchId() });
}
