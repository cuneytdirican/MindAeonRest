using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Yemek sitesi/paket platform ayarları — ŞUBE BAZLI (eklenti modül).</summary>
public class DeliveryPlatformService
{
    /// <summary>Entegre edilebilen online platformlar.</summary>
    public static readonly DeliveryPlatform[] OnlinePlatforms =
        { DeliveryPlatform.Trendyol, DeliveryPlatform.Getir, DeliveryPlatform.YemekSepeti, DeliveryPlatform.Migros };

    /// <summary>Şubenin tüm online platform ayarları (kayıt yoksa boş satır döner).</summary>
    public List<DeliveryPlatformSetting> GetByBranch(int branchId)
    {
        var stored = new Dictionary<int, DeliveryPlatformSetting>();
        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, BranchId, Platform, StoreId, ApiKey, SecretKey, IsEnabled, IsInboundEnabled FROM dbo.DeliveryPlatformSettings WHERE BranchId=@b;";
            cmd.Parameters.AddWithValue("@b", branchId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                stored[r.GetInt32(2)] = Map(r);
        }
        var list = new List<DeliveryPlatformSetting>();
        foreach (var p in OnlinePlatforms)
            list.Add(stored.TryGetValue((int)p, out var s) ? s : new DeliveryPlatformSetting { BranchId = branchId, Platform = p });
        return list;
    }

    /// <summary>Belirli şube+platform ayarı (kayıt yoksa boş/kapalı ayar). Inbound akışında sağlayıcı seçimi için.</summary>
    public DeliveryPlatformSetting Get(int branchId, DeliveryPlatform platform)
        => GetByBranch(branchId).First(s => s.Platform == platform);

    /// <summary>Webhook yönlendirmesi: platform + mağaza id'sine göre YEREL ŞUBE'yi + ayarı çözer (yoksa null).
    /// Gelen sipariş hangi şubeye ait belli olsun diye — StoreId platform mağaza kimliğidir.</summary>
    public (int BranchId, DeliveryPlatformSetting Setting)? FindByPlatformStore(DeliveryPlatform platform, string? storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId)) return null;
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, BranchId, Platform, StoreId, ApiKey, SecretKey, IsEnabled, IsInboundEnabled FROM dbo.DeliveryPlatformSettings WHERE Platform=@p AND StoreId=@s;";
        cmd.Parameters.AddWithValue("@p", (int)platform);
        cmd.Parameters.AddWithValue("@s", storeId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var s = Map(r);
        return (s.BranchId, s);
    }

    private static DeliveryPlatformSetting Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), BranchId = r.GetInt32(1), Platform = (DeliveryPlatform)r.GetInt32(2),
        StoreId = r.IsDBNull(3) ? null : r.GetString(3),
        ApiKey = r.IsDBNull(4) ? null : SecretProtector.Unprotect(r.GetString(4)),
        SecretKey = r.IsDBNull(5) ? null : SecretProtector.Unprotect(r.GetString(5)),
        IsEnabled = r.GetBoolean(6),
        IsInboundEnabled = !r.IsDBNull(7) && r.GetBoolean(7)
    };

    public void Save(DeliveryPlatformSetting s, int? userId)
    {
        using var c = Database.OpenCompany();
        int? id;
        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id FROM dbo.DeliveryPlatformSettings WHERE BranchId=@b AND Platform=@p;";
            q.Parameters.AddWithValue("@b", s.BranchId); q.Parameters.AddWithValue("@p", (int)s.Platform);
            id = q.ExecuteScalar() as int?;
        }
        // API/Secret anahtarları BOŞ geldiğinde mevcut (şifreli) değer KORUNUR — boş yazıp DB'deki gerçek
        // sırrı silmeyiz (veri kaybı önlenir). Yalnız DOLU geldiğinde yeniden şifrele-yaz.
        bool hasApiKey = !string.IsNullOrWhiteSpace(s.ApiKey);
        bool hasSecretKey = !string.IsNullOrWhiteSpace(s.SecretKey);

        using var cmd = c.CreateCommand();
        if (id is null)
        {
            // Yeni kayıt: korunacak mevcut değer yok → boş anahtar NULL yazılır.
            cmd.CommandText = "INSERT INTO dbo.DeliveryPlatformSettings (BranchId, Platform, StoreId, ApiKey, SecretKey, IsEnabled, IsInboundEnabled, CreatedBy) VALUES (@b,@p,@s,@a,@k,@e,@in,@by);";
            CompanyService.AddNullable(cmd, "@a", hasApiKey ? SecretProtector.Protect(s.ApiKey) : null);
            CompanyService.AddNullable(cmd, "@k", hasSecretKey ? SecretProtector.Protect(s.SecretKey) : null);
        }
        else
        {
            // Güncelleme: boş gelen anahtar kolonu SET listesinden ÇIKARILIR (mevcut değer ezilmez).
            string apiSet = hasApiKey ? "ApiKey=@a, " : "";
            string secretSet = hasSecretKey ? "SecretKey=@k, " : "";
            cmd.CommandText = $"UPDATE dbo.DeliveryPlatformSettings SET StoreId=@s, {apiSet}{secretSet}IsEnabled=@e, IsInboundEnabled=@in, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id.Value);
            if (hasApiKey) CompanyService.AddNullable(cmd, "@a", SecretProtector.Protect(s.ApiKey));
            if (hasSecretKey) CompanyService.AddNullable(cmd, "@k", SecretProtector.Protect(s.SecretKey));
        }
        cmd.Parameters.AddWithValue("@b", s.BranchId);
        cmd.Parameters.AddWithValue("@p", (int)s.Platform);
        CompanyService.AddNullable(cmd, "@s", s.StoreId);
        cmd.Parameters.AddWithValue("@e", s.IsEnabled);
        cmd.Parameters.AddWithValue("@in", s.IsInboundEnabled);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Platform gerçekten kullanıma hazır mı: etkin + mağaza/anahtarlar dolu.
    /// (Gerçek online entegrasyon henüz YOK; bu bayrak gönderim öncesi FAIL-CLOSED kontrol içindir.)</summary>
    public static bool IsConfigured(DeliveryPlatformSetting s)
        => s.IsEnabled
           && !string.IsNullOrWhiteSpace(s.StoreId)
           && !string.IsNullOrWhiteSpace(s.ApiKey)
           && !string.IsNullOrWhiteSpace(s.SecretKey);

    /// <summary>Online platforma herhangi bir gönderim DENENMEDEN önce çağrılır. Yapılandırma eksikse
    /// (ya da entegrasyon henüz mevcut değilse) anlamlı hata fırlatır → sessiz/sahte "gönderildi" olmaz.</summary>
    public static void EnsureConfiguredForSend(DeliveryPlatformSetting s)
    {
        if (!IsConfigured(s))
            throw new InvalidOperationException(
                $"{s.PlatformText} entegrasyonu yapılandırılmadı (etkin/mağaza/anahtar eksik); gönderim yapılamaz.");
        // Gerçek online entegrasyon eklenti modülde gelecek; o gelene kadar gönderim FAIL-CLOSED.
        throw new InvalidOperationException(
            $"{s.PlatformText} online entegrasyonu henüz aktif değil (eklenti modül). GERÇEK GÖNDERİM YAPILMADI.");
    }
}
