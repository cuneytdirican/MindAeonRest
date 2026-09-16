using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>e-Fatura/e-Arşiv eklenti modülü: ayarlar + fatura oluşturma + sağlayıcıya gönderim.</summary>
public class EInvoiceService
{
    private readonly OrderService _orders = new();
    private readonly CustomerService _customers = new();

    public EInvoiceSettings GetSettings()
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 Id, Provider, Username, Password, IsTest, IsEnabled FROM dbo.EInvoiceSettings ORDER BY Id;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new EInvoiceSettings();
        return new EInvoiceSettings
        {
            Id = r.GetInt32(0), Provider = (EInvoiceProvider)r.GetInt32(1),
            Username = r.IsDBNull(2) ? null : r.GetString(2), Password = r.IsDBNull(3) ? null : r.GetString(3),
            IsTest = r.GetBoolean(4), IsEnabled = r.GetBoolean(5)
        };
    }

    public void SaveSettings(EInvoiceSettings s, int? userId)
    {
        using var c = Database.OpenCompany();
        int? existingId;
        using (var q = c.CreateCommand()) { q.CommandText = "SELECT TOP 1 Id FROM dbo.EInvoiceSettings ORDER BY Id;"; existingId = q.ExecuteScalar() as int?; }
        using var cmd = c.CreateCommand();
        if (existingId is null)
            cmd.CommandText = "INSERT INTO dbo.EInvoiceSettings (Provider, Username, Password, IsTest, IsEnabled, CreatedBy) VALUES (@p,@u,@pw,@t,@e,@by);";
        else { cmd.CommandText = "UPDATE dbo.EInvoiceSettings SET Provider=@p, Username=@u, Password=@pw, IsTest=@t, IsEnabled=@e, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;"; cmd.Parameters.AddWithValue("@id", existingId.Value); }
        cmd.Parameters.AddWithValue("@p", (int)s.Provider);
        CompanyService.AddNullable(cmd, "@u", s.Username);
        CompanyService.AddNullable(cmd, "@pw", s.Password);
        cmd.Parameters.AddWithValue("@t", s.IsTest);
        cmd.Parameters.AddWithValue("@e", s.IsEnabled);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public EInvoiceRequest BuildRequest(Order order)
    {
        var customer = _customers.GetById(order.CustomerId);
        var req = BuildFromLines(_orders.GetLines(order.Id));
        req.IsEArsiv = string.IsNullOrWhiteSpace(customer?.TaxNumber);
        req.CustomerName = customer?.Name ?? "Müşteri";
        req.TaxNumber = customer?.TaxNumber;
        req.TaxOffice = customer?.TaxOffice;
        req.Address = customer?.Address;
        req.Date = DateTime.Now;
        return req;
    }

    /// <summary>
    /// SAF (DB'siz, birim testine uygun): sipariş satırlarından fatura satırları + matrah/KDV/toplam üretir.
    /// BULGU 5: KISMİ İADE edilen adet faturaya GİRMEZ. Efektif miktar = Qty - RefundedQty (RefundableQty);
    /// iskonto efektif miktara oranlanır (birim fiyat × efektif miktar − oranlı iskonto). Efektif miktar 0 ise
    /// satır hariç tutulur. (Filtre: ana satır + ödenmiş/SettledClFicheId + iptal değil + ikram değil.)
    /// </summary>
    public static EInvoiceRequest BuildFromLines(IEnumerable<OrderLine> orderLines)
    {
        var req = new EInvoiceRequest();
        foreach (var l in orderLines.Where(l => l.ParentLineId == null && l.SettledClFicheId != null
                                                && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat))
        {
            decimal effQty = l.Qty - l.RefundedQty;         // = RefundableQty (iade düşülmüş efektif adet)
            if (effQty <= 0) continue;                       // tamamı iade edildi → satır faturaya girmez
            decimal discount = l.Qty > 0 ? Math.Round(l.DiscountAmount * (effQty / l.Qty), 2) : 0m;
            decimal effTotal = effQty * l.UnitPrice - discount;
            req.Lines.Add(new EInvoiceLine { Name = l.ProductName ?? "Ürün", Qty = effQty, UnitPrice = l.UnitPrice, TaxRate = l.TaxRate, LineTotal = effTotal });
        }

        foreach (var g in req.Lines.GroupBy(l => l.TaxRate))
        {
            decimal gross = g.Sum(x => x.LineTotal);
            decimal matrah = Math.Round(gross / (1 + g.Key / 100m), 2);
            req.MatrahTotal += matrah;
            req.TaxTotal += gross - matrah;
            req.GrandTotal += gross;
        }
        return req;
    }

    public EInvoiceResult SendForOrder(Order order, int? userId)
    {
        var settings = GetSettings();
        if (!settings.IsEnabled || settings.Provider == EInvoiceProvider.Yok)
            return new EInvoiceResult { Success = false, Message = "e-Fatura modülü etkin değil. Ayarlardan açın." };
        var provider = EInvoiceProviderFactory.Get(settings.Provider);
        if (provider is null) return new EInvoiceResult { Success = false, Message = "Sağlayıcı bulunamadı." };

        // Idempotency: aynı sipariş iki kez faturalanmasın (fiş→ETTN eşlemesi). Başarılı kayıt varsa onu döndür.
        var existing = GetLog(order.Id);
        if (existing is { Status: EInvoiceStatus.Gonderildi })
            return new EInvoiceResult
            {
                Success = true, Simulated = existing.IsTest, Status = EInvoiceStatus.Gonderildi,
                Ettn = existing.Ettn, InvoiceNo = existing.InvoiceNo,
                Message = $"Bu sipariş zaten faturalandı ({existing.DateText}). Mükerrer gönderim yapılmadı."
            };

        var req = BuildRequest(order);
        if (req.Lines.Count == 0) return new EInvoiceResult { Success = false, Message = "Faturalanacak (ödenmiş) satır yok." };

        // BULGU 15: Eşzamanlı çift gönderim koruması. Gönderimden ÖNCE "gönderiliyor" (Bekliyor) durumunda
        // bir CLAIM satırı INSERT edilir; UX_EInvoiceLog_OrderSent (OrderId, Status IN (0,1)) tekil index
        // sayesinde iki eşzamanlı çağrıdan yalnız BİRİ claim alır. Claim ALAMAYAN provider.Send'i ÇAĞIRMAZ
        // (sağlayıcıya/GİB'e mükerrer gönderim engellenir). Başarısızlıkta claim Hata'ya çekilir → retry serbest.
        int? claimId = TryClaim(order, settings, req, userId);
        if (claimId is null)
            return new EInvoiceResult { Success = false, Message = "Bu sipariş için e-Fatura gönderimi halihazırda sürüyor veya tamamlandı. Mükerrer gönderim yapılmadı." };

        EInvoiceResult res;
        try { res = provider.Send(req, settings); }
        catch (Exception ex)
        {
            // Beklenmeyen hata: claim'i Hata'ya çekip (kilidi bırak) tekrar denenebilir kıl, sonra hatayı yükselt.
            FinalizeClaim(claimId.Value, settings, req, new EInvoiceResult { Success = false, Message = "Gönderim sırasında beklenmeyen hata: " + ex.Message }, userId);
            throw;
        }
        // Claim satırını sonuca göre güncelle (başarı=Gonderildi, aksi=Hata). ProviderResponse/ETTN dahil.
        FinalizeClaim(claimId.Value, settings, req, res, userId);
        return res;
    }

    /// <summary>Gönderilmiş faturanın durumunu sağlayıcıdan sorgular (log'daki ETTN ile).</summary>
    public EInvoiceResult GetStatusForOrder(int orderId)
    {
        var log = GetLog(orderId);
        if (log is null || string.IsNullOrWhiteSpace(log.Ettn))
            return new EInvoiceResult { Success = false, Message = "Bu sipariş için gönderilmiş e-Fatura kaydı yok." };
        var settings = GetSettings();
        var provider = EInvoiceProviderFactory.Get(settings.Provider);
        if (provider is null) return new EInvoiceResult { Success = false, Message = "Sağlayıcı bulunamadı." };
        return provider.GetStatus(log.Ettn, settings);
    }

    /// <summary>Gönderilmiş faturayı iptal eder (iskele) ve başarılıysa log durumunu İptal'e çeker.</summary>
    public EInvoiceResult CancelForOrder(int orderId, int? userId)
    {
        var log = GetLog(orderId);
        if (log is null || log.Status != EInvoiceStatus.Gonderildi || string.IsNullOrWhiteSpace(log.Ettn))
            return new EInvoiceResult { Success = false, Message = "İptal edilecek gönderilmiş e-Fatura kaydı yok." };
        var settings = GetSettings();
        var provider = EInvoiceProviderFactory.Get(settings.Provider);
        if (provider is null) return new EInvoiceResult { Success = false, Message = "Sağlayıcı bulunamadı." };
        var res = provider.Cancel(log.Ettn, settings);
        if (res.Success) UpdateLogStatus(log.Id, EInvoiceStatus.Iptal, res.Message, userId);
        return res;
    }

    /// <summary>Bir siparişin EN SON e-Fatura kaydı (yoksa null).</summary>
    public EInvoiceLogEntry? GetLog(int orderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = LogSelect + " WHERE OrderId=@o ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapLog(r) : null;
    }

    /// <summary>Son e-Fatura kayıtları (BackOffice liste ekranı).</summary>
    public List<EInvoiceLogEntry> GetRecentLogs(int take = 200)
    {
        var list = new List<EInvoiceLogEntry>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = LogSelect.Replace("SELECT", $"SELECT TOP ({take})") + " ORDER BY Id DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapLog(r));
        return list;
    }

    /// <summary>BULGU 15: Gönderimden ÖNCE "gönderiliyor" (Bekliyor) claim satırı açar. Tekil index (OrderId,
    /// Status IN (0,1)) yarışı engeller. Claim alınırsa yeni log Id'si döner; başka çağrı almışsa (unique ihlali) null.</summary>
    private int? TryClaim(Order order, EInvoiceSettings s, EInvoiceRequest req, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.EInvoiceLog (OrderId, BranchId, Provider, IsEArsiv, IsTest, Status,
    CustomerName, TaxNumber, GrandTotal, TaxTotal, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@o,@br,@p,@ea,@t,@st,@cust,@vkn,@grand,@tax,@by);";
        cmd.Parameters.AddWithValue("@o", order.Id);
        cmd.Parameters.AddWithValue("@br", order.BranchId == 0 ? DBNull.Value : order.BranchId);
        cmd.Parameters.AddWithValue("@p", (int)s.Provider);
        cmd.Parameters.AddWithValue("@ea", req.IsEArsiv);
        cmd.Parameters.AddWithValue("@t", s.IsTest);
        cmd.Parameters.AddWithValue("@st", (int)EInvoiceStatus.Bekliyor);   // "gönderiliyor" = claim/kilit
        cmd.Parameters.AddWithValue("@cust", (object?)req.CustomerName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vkn", (object?)req.TaxNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@grand", req.GrandTotal);
        cmd.Parameters.AddWithValue("@tax", req.TaxTotal);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        try { return (int)cmd.ExecuteScalar()!; }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Yarış: başka bir gönderim aynı sipariş için claim aldı (gönderiliyor/gönderildi) → mükerrer engellendi.
            return null;
        }
    }

    /// <summary>Claim satırını gönderim sonucuna göre günceller: başarı=Gonderildi (kilit kalır=idempotency),
    /// aksi=Hata (kilit bırakılır → retry serbest). ProviderResponse/ETTN/InvoiceNo dahil.</summary>
    private void FinalizeClaim(int logId, EInvoiceSettings s, EInvoiceRequest req, EInvoiceResult res, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.EInvoiceLog SET Status=@st, IsTest=@t, Ettn=@ettn, InvoiceNo=@no,
    GrandTotal=@grand, TaxTotal=@tax, Message=@msg, ProviderResponse=@resp, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@st", (int)(res.Success ? EInvoiceStatus.Gonderildi : EInvoiceStatus.Hata));
        cmd.Parameters.AddWithValue("@t", res.Simulated || s.IsTest);
        cmd.Parameters.AddWithValue("@ettn", (object?)res.Ettn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@no", (object?)res.InvoiceNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@grand", req.GrandTotal);
        cmd.Parameters.AddWithValue("@tax", req.TaxTotal);
        cmd.Parameters.AddWithValue("@msg", (object?)res.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@resp", (object?)res.ProviderResponse ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", logId);
        cmd.ExecuteNonQuery();
    }

    private void UpdateLogStatus(int logId, EInvoiceStatus status, string? message, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.EInvoiceLog SET Status=@st, Message=@msg, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@st", (int)status);
        cmd.Parameters.AddWithValue("@msg", (object?)message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", logId);
        cmd.ExecuteNonQuery();
    }

    private const string LogSelect = @"
SELECT Id, OrderId, BranchId, Provider, IsEArsiv, IsTest, Status, Ettn, InvoiceNo,
       CustomerName, TaxNumber, GrandTotal, TaxTotal, Message, ProviderResponse, CreatedAt
FROM dbo.EInvoiceLog";

    private static EInvoiceLogEntry MapLog(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), OrderId = r.GetInt32(1), BranchId = r.IsDBNull(2) ? null : r.GetInt32(2),
        Provider = (EInvoiceProvider)r.GetInt32(3), IsEArsiv = r.GetBoolean(4), IsTest = r.GetBoolean(5),
        Status = (EInvoiceStatus)r.GetInt32(6),
        Ettn = r.IsDBNull(7) ? null : r.GetString(7), InvoiceNo = r.IsDBNull(8) ? null : r.GetString(8),
        CustomerName = r.IsDBNull(9) ? null : r.GetString(9), TaxNumber = r.IsDBNull(10) ? null : r.GetString(10),
        GrandTotal = r.GetDecimal(11), TaxTotal = r.GetDecimal(12),
        Message = r.IsDBNull(13) ? null : r.GetString(13), ProviderResponse = r.IsDBNull(14) ? null : r.GetString(14),
        CreatedAt = r.GetDateTime(15)
    };
}
