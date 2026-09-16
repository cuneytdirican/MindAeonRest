using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>FAZ 3b — Online teslimat GELEN (inbound) sipariş boru hattı uçları.
/// <list type="bullet">
/// <item><c>POST /api/delivery/webhook/{company}/{platform}</c> — ANONİM; imza/secret doğrulanır (fail-closed) → paket adisyonuna çevrilir.</item>
/// <item><c>POST /api/delivery/simulate/{platform}</c> — YETKİLİ; gerçek platform OLMADAN uçtan uca test (SimulateInbound → Ingest).</item>
/// <item><c>POST /api/delivery/{orderId}/sync-status</c> — YETKİLİ; yerel durumu platforma geri bildirir (simülasyon).</item>
/// </list>
/// FAIL-CLOSED: webhook imzası/secret yoksa ya da inbound kapalıysa gerçek işlem yapılmaz.</summary>
[Route("api/delivery")]
public class DeliveryController(PosNotifier notifier) : PosControllerBase
{
    private readonly DeliveryInboundService _inbound = new();
    private readonly DeliveryPlatformService _settings = new();
    private readonly OrderService _orders = new();

    private const string ManageRoles = "SysAdmin,Kasiyer";

    private const string SignatureHeader = "X-Delivery-Signature";

    private static bool TryPlatform(string platform, out DeliveryPlatform p)
        => Enum.TryParse(platform, ignoreCase: true, out p)
           && DeliveryPlatformService.OnlinePlatforms.Contains(p);

    // ===== Webhook (anonim; imza ile doğrulanır) =====

    /// <summary>Platform webhook'u: ham gövde → imza doğrula → normalize → paket adisyonuna çevir (idempotent).
    /// Şirket kodu URL'de (platform her mağaza için ayrı webhook URL'i tanımlar). Fail-closed: imza/secret yoksa 401.</summary>
    [HttpPost("webhook/{company}/{platform}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(string company, string platform)
    {
        if (!TryPlatform(platform, out var plat))
            return BadRequest(new { message = "Geçersiz/desteklenmeyen teslimat platformu." });

        // 1) Şirketi çöz → izole tenant bağlamı kur (TenantMiddleware anonim isteklerde çalışmaz).
        var comp = new CompanyService().GetByCode(company);
        if (comp is null) return NotFound(new { message = "Şirket bulunamadı." });

        // 2) Ham gövdeyi oku (imza tam bu bayt dizisi üzerinden hesaplanır).
        string payload;
        using (var reader = new StreamReader(Request.Body))
            payload = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return BadRequest(new { message = "Boş webhook gövdesi." });

        string? signature = Request.Headers.TryGetValue(SignatureHeader, out var sv) ? sv.ToString() : null;

        using (Session.BeginScope())
        {
            Session.CurrentCompany = new Company { Code = comp.Code, Name = comp.Name, DatabaseName = comp.DatabaseName };
            MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(comp.DatabaseName);
            Session.CurrentKioskNo = 1;

            var client = DeliveryClientFactory.Get(plat);
            if (client is null) return BadRequest(new { message = "Sağlayıcı istemcisi bulunamadı." });

            // 3) Payload'u normalize et (StoreId'yi almak için) — sonra şube/ayar çözülür.
            var parsed = client.ParseWebhook(payload, new DeliveryPlatformSetting { Platform = plat });
            if (parsed is null)
                return BadRequest(new { message = "Webhook gövdesi çözümlenemedi (beklenen alanlar yok)." });

            // 4) Mağaza → yerel şube + ayar (fail-closed: eşleşme yoksa reddet).
            var route = _settings.FindByPlatformStore(plat, parsed.StoreId);
            if (route is null)
                return NotFound(new { message = $"'{parsed.StoreId}' mağazası bu şirkette bir şubeye eşlenmemiş." });
            var (branchId, setting) = route.Value;

            // 5) FAIL-CLOSED: inbound kapalı ya da imza doğrulanamıyorsa gerçek işlem YOK.
            if (!setting.IsEnabled || !setting.IsInboundEnabled)
                return StatusCode(403, new { message = $"{plat.ToTr()} için gelen sipariş (inbound) etkin değil." });
            if (!client.VerifyWebhookSignature(payload, signature, setting))
                return Unauthorized(new { message = "Webhook imzası doğrulanamadı." });

            // 6) İçeri al (idempotent).
            var result = _inbound.Ingest(parsed, branchId, userId: null);
            await notifier.TableChanged(comp.Code, branchId, new { source = "delivery", platform = plat.ToString() });
            return Ok(ToDto(result));
        }
    }

    // ===== Simülasyon (yetkili; gerçek platform olmadan uçtan uca test) =====

    /// <summary>TEST: gerçek platform çağrısı OLMADAN örnek bir gelen sipariş üretir ve içeri alır (uçtan uca iç akış).
    /// Katalogdan gerçek ürünler örneklenir → mutfağa gerçekten düşen satırlar oluşur.</summary>
    [HttpPost("simulate/{platform}")]
    [Authorize(Roles = ManageRoles)]
    public async Task<ActionResult<InboundIngestDto>> Simulate(string platform)
    {
        if (!TryPlatform(platform, out var plat))
            return BadRequest(new { message = "Geçersiz/desteklenmeyen teslimat platformu." });

        var client = DeliveryClientFactory.Get(plat);
        if (client is null) return BadRequest(new { message = "Sağlayıcı istemcisi bulunamadı." });

        int branchId = BranchId();
        var setting = _settings.Get(branchId, plat);

        // Katalogdan satılabilir birkaç gerçek ürün → simüle siparişin kalemleri (eşleşme garanti).
        var sample = new PosCatalogService().GetProducts(branchId)
            .Where(p => !p.IsSalesStopped && !p.IsMenu)
            .Take(2)
            .Select(p => (p.Code, p.Name, p.Price))
            .ToList();

        var inbound = client.SimulateInbound(setting, sample);
        var result = _inbound.Ingest(inbound, branchId, UserId);
        await notifier.TableChanged(CompanyCode, branchId, new { source = "delivery-sim", platform = plat.ToString() });
        return Ok(ToDto(result));
    }

    // ===== Durum senkron (yerel → platforma geri bildir) =====

    /// <summary>Yerel teslimat durumunu platforma geri bildirir (simülasyon) + eşleme durumunu günceller.
    /// Not: yerel <c>Orders.DeliveryStatus</c> güncellemesi ayrı uçtadır (orders/{id}/delivery-status).</summary>
    [HttpPost("{orderId:int}/sync-status")]
    [Authorize(Roles = ManageRoles)]
    public IActionResult SyncStatus(int orderId, [FromBody] DeliverySyncStatusRequest req)
    {
        if (_orders.GetOrderBranch(orderId) is not int b || b != BranchId())
            return NotFound();
        if (!Enum.IsDefined(typeof(DeliveryStatus), req.Status))
            return BadRequest(new { message = "Geçersiz teslimat durumu." });

        var result = _inbound.SyncStatusToPlatform(orderId, (DeliveryStatus)req.Status, UserId, req.NotifyCustomer);
        return Ok(new { success = result.Success, simulated = result.Simulated, message = result.Message });
    }

    private static InboundIngestDto ToDto(DeliveryInboundService.IngestResult r)
        => new(r.LocalOrderId, r.Duplicate, r.MatchedLines, r.UnmatchedItems, r.Message);
}
