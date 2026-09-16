using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Online teslimat GELEN (inbound) sipariş boru hattı: normalize edilmiş <see cref="InboundOrder"/>'ı
/// alır → ürünleri yerel ProductId'ye eşler → <see cref="OrderService.CreatePackage"/> ile PAKET adisyonu açar,
/// kalemleri ekler ve mutfağa gönderir. Dış sipariş no ↔ yerel Order eşlemesi <c>dbo.DeliveryInboundMap</c>'te
/// saklanır (UQ Platform+ExternalOrderNo → IDEMPOTENT: aynı sipariş iki kez gelirse çift adisyon OLUŞMAZ).
/// Durum değişince platforma geri bildirir (sağlayıcı istemci — bu turda simülasyon).</summary>
public class DeliveryInboundService
{
    private readonly OrderService _orders = new();
    private readonly DeliveryPlatformService _settings = new();

    /// <summary>Ingest sonucu: oluşan/var olan yerel sipariş + eşleşmeyen kalem uyarıları.</summary>
    public sealed class IngestResult
    {
        public int LocalOrderId { get; init; }
        public bool Duplicate { get; init; }        // aynı dış sipariş no daha önce işlenmiş (idempotent)
        public int MatchedLines { get; init; }
        public List<string> UnmatchedItems { get; init; } = new();
        /// <summary>DOĞRULANMAMIŞ otomatik eşleşmeler (Code/Name yedeği ile bulundu, DeliveryProductMap'te yok) — personel kontrol etmeli.</summary>
        public List<string> UnverifiedMatches { get; init; } = new();
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>Gelen siparişi verilen şubeye içeri alır (idempotent). Aynı (Platform, ExternalOrderNo) daha önce
    /// işlenmişse yeni adisyon açmadan mevcut yerel sipariş id'sini döner.</summary>
    public IngestResult Ingest(InboundOrder ord, int branchId, int? userId)
    {
        if (ord is null) throw new ArgumentNullException(nameof(ord));
        if (string.IsNullOrWhiteSpace(ord.ExternalOrderNo))
            throw new InvalidOperationException("Gelen siparişin dış sipariş numarası boş; işlenemez.");

        // 1) IDEMPOTENCY GEÇİDİ: dış sipariş no'yu ATOMİK claim et (UQ ihlali → zaten var).
        if (!TryClaim(ord, out int existingLocalId))
        {
            // Başka bir işlem (ya da önceki teslim) aynı no'yu almış.
            return new IngestResult
            {
                LocalOrderId = existingLocalId,
                Duplicate = true,
                Message = existingLocalId > 0
                    ? $"Sipariş {ord.ExternalOrderNo} zaten işlenmiş (adisyon #{existingLocalId}); tekrar oluşturulmadı."
                    : $"Sipariş {ord.ExternalOrderNo} şu an işleniyor; yinelenen istek yok sayıldı."
            };
        }

        try
        {
            // 2) PA cari + aktif lokasyon (paket akışının gerektirdiği bağlam) — controller CreatePackage paritesi.
            var pa = new CustomerService().GetAll().FirstOrDefault(c => c.Code == "PA")
                ?? throw new InvalidOperationException("Varsayılan 'PA' cari kartı bulunamadı.");
            var zones = new ZoneService().GetAll();
            var zone = zones.FirstOrDefault(z => z.BranchId == branchId && z.IsActive)
                       ?? zones.FirstOrDefault(z => z.BranchId == branchId)
                       ?? throw new InvalidOperationException("Şubede tanımlı lokasyon (zone) yok.");

            // 3) Ürün eşleme (GÜVEN katmanlı): DeliveryProductMap = DOĞRULANMIŞ (birincil, güvenilir);
            //    yerel Products.Code / Products.Name = DOĞRULANMAMIŞ yedek (FARKLI ad-uzayları → yanlış ürün riski).
            //    Çift ada sahip yerel ürünler BELİRSİZ sayılır → ada göre eşleştirilmez (unmatched). Sessiz kabul YOK.
            var catalog = new PosCatalogService().GetProducts(branchId);
            var byId = catalog.ToDictionary(p => p.Id);
            var byCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in catalog)
            {
                if (!string.IsNullOrWhiteSpace(p.Code)) byCode[p.Code] = p.Id;   // Code yerelde unique
                if (!string.IsNullOrWhiteSpace(p.Name) && !byName.TryAdd(p.Name, p.Id))
                    ambiguousNames.Add(p.Name);   // aynı ada 2+ ürün → belirsiz
            }
            var externalMap = LoadProductMap(ord.Platform);

            var resolved = new List<(InboundOrderItem Item, PosProduct Product)>();
            var unmatched = new List<string>();
            var unverified = new List<string>();   // DOĞRULANMAMIŞ otomatik eşleşmeler (uyarı; sessiz kabul edilmez)
            foreach (var it in ord.Items)
            {
                var m = DeliveryInboundMatch.MatchItem(it.ExternalCode, it.Name,
                    externalMap, byCode, byName, ambiguousNames, byId.ContainsKey);
                if (m.IsMatched)
                {
                    var prod = byId[m.ProductId];
                    resolved.Add((it, prod));
                    if (m.Confidence == InboundMatchConfidence.AutoUnverified)
                        unverified.Add($"{it.Name ?? it.ExternalCode} → {prod.Name} (#{prod.Code}) x{it.Qty:0.##}");
                }
                else
                {
                    string why = m.Confidence == InboundMatchConfidence.Ambiguous ? " (çift ad; belirsiz)" : "";
                    unmatched.Add($"{it.Name ?? it.ExternalCode ?? "?"} x{it.Qty:0.##}{why}");
                }
            }

            // Not: eşleşmeyen + DOĞRULANMAMIŞ otomatik eşleşmeler görünür kılınır (personel kontrol/tamamlar).
            string note = ord.Note ?? string.Empty;
            if (unmatched.Count > 0)
                note = (note.Length > 0 ? note + " | " : "") + "EŞLEŞMEYEN: " + string.Join(", ", unmatched);
            if (unverified.Count > 0)
                note = (note.Length > 0 ? note + " | " : "") + "OTOMATİK-EŞLEŞME (DOĞRULANMAMIŞ, KONTROL EDİN): " + string.Join(", ", unverified);
            note = (note.Length > 0 ? note + " | " : "") + $"{ord.Platform.ToTr()} #{ord.ExternalOrderNo}";

            // 4) PAKET adisyonunu aç — KALICI (kendi tx'inde commit). OLUŞUR OLUŞMAZ claim'i bu id'ye BAĞLA:
            //    bu andan sonra herhangi bir hata claim'i SİLEMEZ (LocalOrderId!=0) → retry Duplicate ile MEVCUT
            //    adisyonu döner (ÇİFT adisyon + çift stok YOK). [BULGU 3 düzeltmesi: erken CompleteClaim]
            var order = _orders.CreatePackage(branchId, zone.Id, pa.Id, ord.Platform, OrderType.Paket,
                ord.CustomerName, ord.CustomerPhone, ord.CustomerAddress, userId);
            CompleteClaim(ord.Platform, ord.ExternalOrderNo, order.Id);

            _orders.SetOrderNote(order.Id, note, userId);

            // 5) Kalemleri ekle. Fiyat/vergi: platform DEĞERİ (0 dahil) önceliklidir; YALNIZ null ise katalog (BULGU 14).
            foreach (var (it, prod) in resolved)
            {
                decimal unitPrice = DeliveryInboundMatch.Resolve(it.UnitPrice, prod.Price);
                decimal taxRate = DeliveryInboundMatch.Resolve(it.TaxRate, prod.TaxRate);
                decimal qty = it.Qty > 0 ? it.Qty : 1;
                _orders.AddLine(order.Id, zone.Id, prod.Id, qty, unitPrice, taxRate, it.Note, 0, userId);
            }

            // 6) Mutfağa gönder — TEKRAR-GÜVENLİ: SendToKitchen içindeki MarkSentTx yalnız Beklemede satırı gönderir,
            //    stok bir kez düşer. Buradan sonra hata olsa bile claim tamamlandığından retry çift OLUŞTURMAZ.
            //    Kalem yoksa (hepsi eşleşmedi) gönderim boş döner — sorun değil.
            _orders.SendToKitchen(order.Id, userId);

            return new IngestResult
            {
                LocalOrderId = order.Id,
                MatchedLines = resolved.Count,
                UnmatchedItems = unmatched,
                UnverifiedMatches = unverified,
                Message = BuildIngestMessage(ord.ExternalOrderNo, order.Id, unmatched.Count, unverified.Count)
            };
        }
        catch
        {
            // Adisyon HENÜZ oluşmadıysa (claim LocalOrderId=0) claim'i bırak → aynı sipariş yeniden denenebilir.
            // Adisyon oluştuysa CompleteClaim onu bağlamıştır (LocalOrderId>0); TryReleaseClaim yalnız 0'ı siler →
            // DOKUNMAZ, böylece retry Duplicate ile MEVCUT adisyonu döner (çift adisyon/stok YOK). [BULGU 3]
            TryReleaseClaim(ord.Platform, ord.ExternalOrderNo);
            throw;
        }
    }

    private static string BuildIngestMessage(string ext, int localId, int unmatchedCount, int unverifiedCount)
    {
        string msg = unmatchedCount == 0
            ? $"Sipariş {ext} → adisyon #{localId} (mutfağa gönderildi)."
            : $"Sipariş {ext} → adisyon #{localId}; {unmatchedCount} kalem eşleşmedi (nota eklendi).";
        if (unverifiedCount > 0)
            msg += $" {unverifiedCount} kalem DOĞRULANMAMIŞ otomatik eşleşti — kontrol edin.";
        return msg;
    }

    /// <summary>Yerel teslimat durumunu platforma geri bildirir (simülasyon) + eşleme durumunu günceller.
    /// Sipariş online platformdan gelmediyse (map yok) sessizce başarısız döner (Fail).</summary>
    public DeliveryClientResult SyncStatusToPlatform(int localOrderId, DeliveryStatus status, int? userId,
        bool notifyCustomer = false)
    {
        var map = FindMapByLocal(localOrderId);
        if (map is null)
            return DeliveryClientResult.Fail("Bu sipariş online bir platformdan gelmedi; senkron yapılmadı.");

        int branchId = _orders.GetOrderBranch(localOrderId)
            ?? throw new InvalidOperationException("Sipariş şubesi çözülemedi.");
        var setting = _settings.Get(branchId, map.Value.Platform);
        var client = DeliveryClientFactory.Get(map.Value.Platform);
        var result = client is null
            ? DeliveryClientResult.Fail($"{map.Value.Platform.ToTr()} sağlayıcısı bulunamadı.")
            : client.PushStatus(setting, map.Value.ExternalOrderNo, status);

        // Eşleme durumunu her hâlde güncelle (yerel gerçeklik).
        UpdateMapStatus(localOrderId, status);

        // Opsiyonel müşteri bildirimi (best-effort; teslimat şablonu FAZ 1b).
        if (notifyCustomer)
            TryNotifyCustomer(localOrderId, branchId, status, userId);

        return result;
    }

    // ===== dbo.DeliveryInboundMap (idempotency + durum) =====

    /// <summary>Dış sipariş no'yu ATOMİK olarak sahiplenir (INSERT, LocalOrderId=0 placeholder).
    /// UQ ihlali → false + mevcut LocalOrderId (varsa &gt;0). Başarı → true.</summary>
    private static bool TryClaim(InboundOrder ord, out int existingLocalId)
    {
        existingLocalId = 0;
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO dbo.DeliveryInboundMap (Platform, ExternalOrderNo, LocalOrderId, Status, RawJson)
                            VALUES (@p, @e, 0, 0, @raw);";
        cmd.Parameters.AddWithValue("@p", (int)ord.Platform);
        cmd.Parameters.AddWithValue("@e", ord.ExternalOrderNo);
        cmd.Parameters.AddWithValue("@raw", (object?)ord.RawJson ?? DBNull.Value);
        try { cmd.ExecuteNonQuery(); return true; }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            // Zaten claim edilmiş → mevcut yerel id'yi dön (tamamlanmışsa >0).
            using var q = c.CreateCommand();
            q.CommandText = "SELECT LocalOrderId FROM dbo.DeliveryInboundMap WHERE Platform=@p AND ExternalOrderNo=@e;";
            q.Parameters.AddWithValue("@p", (int)ord.Platform);
            q.Parameters.AddWithValue("@e", ord.ExternalOrderNo);
            if (q.ExecuteScalar() is int lid) existingLocalId = lid;
            return false;
        }
    }

    private static void CompleteClaim(DeliveryPlatform platform, string ext, int localOrderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.DeliveryInboundMap SET LocalOrderId=@l, UpdatedAt=SYSUTCDATETIME() WHERE Platform=@p AND ExternalOrderNo=@e;";
        cmd.Parameters.AddWithValue("@l", localOrderId);
        cmd.Parameters.AddWithValue("@p", (int)platform);
        cmd.Parameters.AddWithValue("@e", ext);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Yarıda kalan claim'i (LocalOrderId=0) siler ki sipariş yeniden denenebilsin. Tamamlanmışsa (LocalOrderId&gt;0) DOKUNMAZ.</summary>
    private static void TryReleaseClaim(DeliveryPlatform platform, string ext)
    {
        try
        {
            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.DeliveryInboundMap WHERE Platform=@p AND ExternalOrderNo=@e AND LocalOrderId=0;";
            cmd.Parameters.AddWithValue("@p", (int)platform);
            cmd.Parameters.AddWithValue("@e", ext);
            cmd.ExecuteNonQuery();
        }
        catch { /* best-effort: temizlenemezse sonraki temizlik/elle müdahale */ }
    }

    private static (DeliveryPlatform Platform, string ExternalOrderNo)? FindMapByLocal(int localOrderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Platform, ExternalOrderNo FROM dbo.DeliveryInboundMap WHERE LocalOrderId=@l;";
        cmd.Parameters.AddWithValue("@l", localOrderId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ((DeliveryPlatform)r.GetInt32(0), r.GetString(1)) : null;
    }

    private static void UpdateMapStatus(int localOrderId, DeliveryStatus status)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.DeliveryInboundMap SET Status=@s, UpdatedAt=SYSUTCDATETIME() WHERE LocalOrderId=@l;";
        cmd.Parameters.AddWithValue("@s", (int)status);
        cmd.Parameters.AddWithValue("@l", localOrderId);
        cmd.ExecuteNonQuery();
    }

    // ===== dbo.DeliveryProductMap (platform ürün kodu → yerel ProductId) =====

    private static Dictionary<string, int> LoadProductMap(DeliveryPlatform platform)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ExternalCode, ProductId FROM dbo.DeliveryProductMap WHERE Platform=@p AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@p", (int)platform);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetInt32(1);
        return map;
    }

    /// <summary>Platform ürün kodu → yerel ürün eşlemesi ekler/günceller (elle bakım / BackOffice için).</summary>
    public void SaveProductMap(DeliveryPlatform platform, string externalCode, int productId, int? userId)
    {
        if (string.IsNullOrWhiteSpace(externalCode)) throw new ArgumentException("Dış ürün kodu boş olamaz.");
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.DeliveryProductMap SET ProductId=@pid, IsDeleted=0, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
    WHERE Platform=@p AND ExternalCode=@e;
IF @@ROWCOUNT=0
    INSERT INTO dbo.DeliveryProductMap (Platform, ExternalCode, ProductId, CreatedBy) VALUES (@p,@e,@pid,@by);";
        cmd.Parameters.AddWithValue("@p", (int)platform);
        cmd.Parameters.AddWithValue("@e", externalCode.Trim());
        cmd.Parameters.AddWithValue("@pid", productId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void TryNotifyCustomer(int localOrderId, int branchId, DeliveryStatus status, int? userId)
    {
        try
        {
            var order = new OrderService().GetById(localOrderId);
            if (order is null || string.IsNullOrWhiteSpace(order.DeliveryPhone)) return;
            var model = new Dictionary<string, string?>
            {
                ["ad"] = order.DeliveryName ?? order.CustomerName,
                ["kod"] = order.Code,
                ["durum"] = status.ToTr()
            };
            new NotificationService().Send(NotificationChannel.Sms, order.DeliveryPhone!, "delivery-status",
                null, model, branchId, userId: userId);
        }
        catch { /* bildirim best-effort; teslimat akışını bozmaz */ }
    }
}
