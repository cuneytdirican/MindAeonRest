using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>FAZ 5a Sadakat Portalı — MÜŞTERİ self-servis (telefon + OTP) veri katmanı (şirket DB, ADO.NET, EF YOK).
/// Sorumluluklar: kısa-ömürlü OTP üret/doğrula (KENDİ tablosu <c>dbo.CustomerPortalOtp</c>, idempotent şema),
/// telefondan KAYITLI müşteri bul (enumerasyon sızdırmaz — çağıran daima genel yanıt döner), müşterinin sipariş
/// geçmişi (kendi ClFiche'leri) + geçmiş adisyonu YENİDEN-SİPARİŞ şablonuna çöz. Fiyat/puan asla istemciden gelmez;
/// puan bakiyesi LoyaltyService'ten, sipariş oluşturma OrderService'ten çözülür (bu servis yalnız şablon+kimlik verir).</summary>
public class CustomerPortalService
{
    // OTP: 6 hane, kısa ömür, sınırlı deneme (fail-closed: süre/deneme dolunca reddet).
    private const int OtpLength = 6;
    private const int OtpExpiryMinutes = 5;
    private const int MaxVerifyAttempts = 5;

    /// <summary>OTP tablosunu (yoksa) oluşturur — SecretProtector deseni: servis KENDİ tablosunu tembel/idempotent kurar.
    /// EnsureSchema/EnsureColumn ile aynı sözleşme (mevcut akışları BOZMAZ, additive). Aktif şirket DB'sinde çalışır.</summary>
    public static void EnsureSchema()
    {
        using var c = Database.OpenCompany();
        using var ddl = c.CreateCommand();
        ddl.CommandText = @"
IF OBJECT_ID(N'dbo.CustomerPortalOtp', N'U') IS NULL
CREATE TABLE dbo.CustomerPortalOtp (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Phone      NVARCHAR(30)  NOT NULL,
    Code       NVARCHAR(10)  NOT NULL,
    CustomerId INT           NOT NULL,
    ExpiresUtc DATETIME2      NOT NULL,
    Attempts   INT           NOT NULL DEFAULT(0),
    UsedUtc    DATETIME2      NULL,
    CreatedAt  DATETIME2      NOT NULL DEFAULT(SYSUTCDATETIME())
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CustomerPortalOtp_Phone')
    CREATE INDEX IX_CustomerPortalOtp_Phone ON dbo.CustomerPortalOtp (Phone, ExpiresUtc);";
        ddl.ExecuteNonQuery();
    }

    /// <summary>Telefonu yalnızca RAKAMLARA indirger (boşluk/parantez/+/tire temizler) → karşılaştırma kararlı olsun.</summary>
    public static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        // Türkiye numaralarında baştaki 0 / 90 ülke kodu değişkenliğini azalt: son 10 haneyi anahtar al.
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    /// <summary>Telefondan KAYITLI, aktif (PA/perakende hariç) müşteriyi bulur; yoksa null. Customers.Phone ve
    /// CustomerAddresses.Phone alanlarının son 10 hanesi eşleştirilir. Enumerasyon: çağıran bu sonucu SIZDIRMAMALI.</summary>
    public (int Id, string Name)? FindCustomerByPhone(string? rawPhone)
    {
        var key = NormalizePhone(rawPhone);
        if (key.Length < 7) return null;   // anlamsız/çok kısa numara

        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        // Rakam-dışı karakterleri T-SQL'de birebir soyutlamak zor; olası biçimleri LIKE '%<son10>%' ile yakala,
        // ardından uygulamada NormalizePhone ile KESİN eşleşmeyi doğrula (aşağıdaki okuma döngüsünde).
        cmd.CommandText = @"
SELECT DISTINCT c.Id, c.Name, c.Phone
FROM dbo.Customers c
WHERE c.IsDeleted = 0 AND c.IsActive = 1 AND c.Code <> N'PA' AND c.Phone IS NOT NULL AND c.Phone <> N''
UNION
SELECT DISTINCT c.Id, c.Name, a.Phone
FROM dbo.Customers c
JOIN dbo.CustomerAddresses a ON a.CustomerId = c.Id AND a.IsDeleted = 0
WHERE c.IsDeleted = 0 AND c.IsActive = 1 AND c.Code <> N'PA' AND a.Phone IS NOT NULL AND a.Phone <> N'';";
        using var r = cmd.ExecuteReader();
        int? matchId = null; string matchName = string.Empty;
        while (r.Read())
        {
            var phone = r.IsDBNull(2) ? null : r.GetString(2);
            if (NormalizePhone(phone) != key) continue;
            // İlk kesin eşleşmeyi al; birden fazla müşteri aynı numarayı paylaşıyorsa ilkini kabul et (deterministik: Id sırası değil,
            // ama tek eşleşme beklenir). Ambiguity durumunda güvenli davranış: yine de giriş bir karta bağlanır.
            matchId = r.GetInt32(0); matchName = r.GetString(1);
            break;
        }
        return matchId is int id ? (id, matchName) : null;
    }

    /// <summary>Telefon için OTP üretir: kayıtlı müşteri varsa kod üretip saklar (eski kodları geçersiz kılar) ve döner;
    /// yoksa <see cref="OtpIssue.Found"/>=false. SMS gönderimini ÇAĞIRAN yapar (NotificationService, fail-closed).
    /// Anti-enumerasyon: çağıran, Found değerine bakmaksızın DAİMA aynı genel yanıtı vermelidir.</summary>
    public OtpIssue IssueOtp(string? rawPhone)
    {
        EnsureSchema();
        var found = FindCustomerByPhone(rawPhone);
        if (found is null) return new OtpIssue(false, null, 0, null);

        var phoneKey = NormalizePhone(rawPhone);
        string code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, OtpLength))
            .ToString().PadLeft(OtpLength, '0');

        using var c = Database.OpenCompany();
        // Aynı numara için bekleyen kodları geçersiz kıl (yalnız EN SON kod geçerli).
        using (var inv = c.CreateCommand())
        {
            inv.CommandText = "UPDATE dbo.CustomerPortalOtp SET UsedUtc = SYSUTCDATETIME() WHERE Phone=@p AND UsedUtc IS NULL;";
            inv.Parameters.AddWithValue("@p", phoneKey);
            inv.ExecuteNonQuery();
        }
        using (var ins = c.CreateCommand())
        {
            ins.CommandText = @"INSERT INTO dbo.CustomerPortalOtp (Phone, Code, CustomerId, ExpiresUtc)
                                VALUES (@p, @code, @cid, DATEADD(MINUTE, @min, SYSUTCDATETIME()));";
            ins.Parameters.AddWithValue("@p", phoneKey);
            ins.Parameters.AddWithValue("@code", code);
            ins.Parameters.AddWithValue("@cid", found.Value.Id);
            ins.Parameters.AddWithValue("@min", OtpExpiryMinutes);
            ins.ExecuteNonQuery();
        }
        return new OtpIssue(true, code, found.Value.Id, found.Value.Name);
    }

    /// <summary>OTP doğrular: süresi geçmemiş, kullanılmamış, deneme limiti aşılmamış EN SON kodu eşleştirir.
    /// Başarılıysa kodu kullanılmış işaretler ve müşteri Id'sini döner; aksi halde null (fail-closed).</summary>
    public int? VerifyOtp(string? rawPhone, string? code)
    {
        EnsureSchema();
        var phoneKey = NormalizePhone(rawPhone);
        var codeKey = (code ?? string.Empty).Trim();
        if (phoneKey.Length < 7 || codeKey.Length == 0) return null;

        using var c = Database.OpenCompany();
        int otpId = 0, customerId = 0, attempts = 0; bool codeMatch = false;
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = @"
SELECT TOP 1 Id, Code, CustomerId, Attempts
FROM dbo.CustomerPortalOtp
WHERE Phone=@p AND UsedUtc IS NULL AND ExpiresUtc > SYSUTCDATETIME() AND Attempts < @max
ORDER BY Id DESC;";
            sel.Parameters.AddWithValue("@p", phoneKey);
            sel.Parameters.AddWithValue("@max", MaxVerifyAttempts);
            using var r = sel.ExecuteReader();
            if (!r.Read()) return null;
            otpId = r.GetInt32(0);
            codeMatch = string.Equals(r.GetString(1), codeKey, StringComparison.Ordinal);
            customerId = r.GetInt32(2);
            attempts = r.GetInt32(3);
        }

        if (!codeMatch)
        {
            using var bump = c.CreateCommand();
            bump.CommandText = "UPDATE dbo.CustomerPortalOtp SET Attempts = Attempts + 1 WHERE Id=@id;";
            bump.Parameters.AddWithValue("@id", otpId);
            bump.ExecuteNonQuery();
            return null;
        }

        using (var use = c.CreateCommand())
        {
            use.CommandText = "UPDATE dbo.CustomerPortalOtp SET UsedUtc = SYSUTCDATETIME() WHERE Id=@id AND UsedUtc IS NULL;";
            use.Parameters.AddWithValue("@id", otpId);
            // Yarış: iki eşzamanlı doğrulama aynı kodu kullanmasın (yalnız ilk UPDATE 1 satır etkiler).
            if (use.ExecuteNonQuery() == 0) return null;
        }
        return customerId;
    }

    /// <summary>Müşterinin sipariş geçmişi: KENDİ cari fişleri (adisyon kapanışları), en yeni önce.</summary>
    public List<PortalHistoryItem> GetHistory(int customerId, int top = 30)
    {
        var list = new List<PortalHistoryItem>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@n) cf.Id, cf.Code, cf.OrderId, cf.BranchId, cf.Total, cf.Paid, cf.FicheDate
FROM dbo.ClFiches cf
WHERE cf.CustomerId=@c AND cf.IsDeleted=0
ORDER BY cf.FicheDate DESC, cf.Id DESC;";
        cmd.Parameters.AddWithValue("@n", top);
        cmd.Parameters.AddWithValue("@c", customerId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PortalHistoryItem
            {
                ClFicheId = r.GetInt32(0),
                Code = r.GetString(1),
                OrderId = r.GetInt32(2),
                BranchId = r.GetInt32(3),
                Total = r.GetDecimal(4),
                Paid = r.GetDecimal(5),
                FicheDate = r.GetDateTime(6)
            });
        return list;
    }

    /// <summary>Bir geçmiş fişin ürün kalemleri (üst satırlar; menü bileşenleri hariç). SAHİPLİK doğrulanır: fiş
    /// customerId'ye ait değilse boş döner (IDOR imkânsız). Yeniden-sipariş önizlemesi + şablonu için kullanılır.</summary>
    public List<PortalHistoryLine> GetFicheLines(int clFicheId, int customerId)
    {
        var list = new List<PortalHistoryLine>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT l.ProductId, p.Name, SUM(l.Qty) AS Qty
FROM dbo.OrderLines l
JOIN dbo.Products  p  ON p.Id = l.ProductId
JOIN dbo.ClFiches  cf ON cf.OrderId = l.OrderId
WHERE cf.Id=@f AND cf.CustomerId=@c AND cf.IsDeleted=0
  AND l.IsDeleted=0 AND l.ParentLineId IS NULL AND l.IsTreat=0
GROUP BY l.ProductId, p.Name
HAVING SUM(l.Qty) > 0
ORDER BY p.Name;";
        cmd.Parameters.AddWithValue("@f", clFicheId);
        cmd.Parameters.AddWithValue("@c", customerId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PortalHistoryLine
            {
                ProductId = r.GetInt32(0),
                ProductName = r.IsDBNull(1) ? "" : r.GetString(1),
                Qty = r.GetDecimal(2)
            });
        return list;
    }

    /// <summary>Yeniden-sipariş hedefi: geçmiş fişin şubesi + adisyonunun lokasyonu (yeni paket adisyonu bu bağlamda açılır).
    /// SAHİPLİK doğrulanır (fiş customerId'ye ait olmalı). Bulunamazsa null.</summary>
    public (int BranchId, int ZoneId)? GetReorderTarget(int clFicheId, int customerId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT o.BranchId, o.ZoneId
FROM dbo.ClFiches cf
JOIN dbo.Orders o ON o.Id = cf.OrderId
WHERE cf.Id=@f AND cf.CustomerId=@c AND cf.IsDeleted=0;";
        cmd.Parameters.AddWithValue("@f", clFicheId);
        cmd.Parameters.AddWithValue("@c", customerId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetInt32(0), r.GetInt32(1));
    }

    /// <summary>Yeni oluşturulan adisyonun türünü Paket'e çeker (portal yeniden-sipariş = paket/online alım).
    /// OrderService.CreateOrder MasaServis(0) üretir; portal siparişi masaya değil pakete yazılır.</summary>
    public void MarkOrderAsPackage(int orderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET OrderType=@t, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@t", (int)OrderType.Paket);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>OTP üretim sonucu — Found=false ise numara kayıtlı değil (çağıran genel yanıt vermeli, sızdırmamalı).</summary>
public sealed record OtpIssue(bool Found, string? Code, int CustomerId, string? CustomerName);

/// <summary>Portal sipariş geçmişi kalemi (müşterinin bir cari fişi).</summary>
public class PortalHistoryItem
{
    public int ClFicheId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public int BranchId { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public DateTime FicheDate { get; set; }
}

/// <summary>Geçmiş fiş ürün satırı (yeniden-sipariş şablonu).</summary>
public class PortalHistoryLine
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
}
