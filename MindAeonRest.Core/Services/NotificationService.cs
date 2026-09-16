using System.Text;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Basit yer-tutucu şablon motoru: <c>{ad}</c>, <c>{tutar}</c>, <c>{kod}</c> gibi
/// <c>{anahtar}</c> kalıplarını modeldeki değerlerle değiştirir (DB/ağ'sız — saf, test edilebilir).</summary>
public static class NotificationTemplateEngine
{
    public static string Render(string? template, IReadOnlyDictionary<string, string?>? model)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        if (model is null || model.Count == 0) return template;
        var sb = new StringBuilder(template);
        foreach (var kv in model)
            sb.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty);
        return sb.ToString();
    }
}

/// <summary>Kod-içi (MVP) hazır bildirim şablonları. İleride dbo.NotificationTemplates tablosu ile genişletilebilir.
/// Anahtarlar: rezervasyon onayı, sadakat kampanyası, teslimat bildirimi.</summary>
public static class NotificationTemplates
{
    public static readonly IReadOnlyDictionary<string, string> Builtins =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reservation-confirm"] = "Sayın {ad}, {tarih} tarihli rezervasyonunuz onaylanmıştır. Rezervasyon kodu: {kod}.",
            ["loyalty-campaign"] = "Sayın {ad}, size özel kampanya! Tutar: {tutar}. Kod: {kod}",
            ["delivery-status"] = "Sayın {ad}, {kod} numaralı siparişiniz {durum}.",
        };

    public static string? Get(string? key)
        => key is not null && Builtins.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Bildirim (SMS/e-posta/WhatsApp) altyapısı: ŞUBE+KANAL bazlı ayar CRUD (SecretProtector ile şifreli sır)
/// + şablon çözümü + sağlayıcı seçimi + gönderim + NotificationLog (best-effort). EF YOK, ADO.NET.
/// FAIL-CLOSED: yapılandırılmamış/kapalı kanal gerçek gönderim yapmaz; TEST modunda SIMULASYON döner.</summary>
public class NotificationService
{
    private static readonly NotificationChannel[] AllChannels =
        { NotificationChannel.Sms, NotificationChannel.Email, NotificationChannel.WhatsApp };

    /// <summary>Şubenin tüm kanal ayarları (kayıt yoksa o kanal için varsayılan boş satır döner).</summary>
    public List<NotificationSetting> GetByBranch(int branchId)
    {
        var stored = new Dictionary<int, NotificationSetting>();
        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT Id, BranchId, Channel, Provider, ApiKey, ApiSecret, Sender, Host, Port, UseSsl, IsEnabled, TestMode
                                FROM dbo.NotificationSettings WHERE BranchId=@b;";
            cmd.Parameters.AddWithValue("@b", branchId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                stored[r.GetInt32(2)] = Map(r);
        }
        var list = new List<NotificationSetting>();
        foreach (var ch in AllChannels)
            list.Add(stored.TryGetValue((int)ch, out var s) ? s
                : new NotificationSetting { BranchId = branchId, Channel = ch });
        return list;
    }

    /// <summary>Şube+kanal ayarı (kayıt yoksa varsayılan boş/kapalı ayar).</summary>
    public NotificationSetting GetSetting(int branchId, NotificationChannel channel)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT Id, BranchId, Channel, Provider, ApiKey, ApiSecret, Sender, Host, Port, UseSsl, IsEnabled, TestMode
                            FROM dbo.NotificationSettings WHERE BranchId=@b AND Channel=@ch;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@ch", (int)channel);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : new NotificationSetting { BranchId = branchId, Channel = channel };
    }

    private static NotificationSetting Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        BranchId = r.GetInt32(1),
        Channel = (NotificationChannel)r.GetInt32(2),
        Provider = (NotificationProvider)r.GetInt32(3),
        // Sırlar DB'de şifreli → okurken çöz (sağlayıcılara düz metin gider).
        ApiKey = r.IsDBNull(4) ? null : SecretProtector.Unprotect(r.GetString(4)),
        ApiSecret = r.IsDBNull(5) ? null : SecretProtector.Unprotect(r.GetString(5)),
        Sender = r.IsDBNull(6) ? null : r.GetString(6),
        Host = r.IsDBNull(7) ? null : r.GetString(7),
        Port = r.IsDBNull(8) ? null : r.GetInt32(8),
        UseSsl = r.GetBoolean(9),
        IsEnabled = r.GetBoolean(10),
        TestMode = r.GetBoolean(11)
    };

    /// <summary>Ayarı ekler/günceller (şube+kanal benzersiz). Sır (ApiKey/ApiSecret) BOŞ gelirse mevcut şifreli
    /// değer KORUNUR (DeliveryPlatformService deseni) — boş yazıp DB'deki gerçek sırrı silmeyiz.</summary>
    public void SaveSettings(NotificationSetting s, int? userId)
    {
        using var c = Database.OpenCompany();
        int? id;
        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id FROM dbo.NotificationSettings WHERE BranchId=@b AND Channel=@ch;";
            q.Parameters.AddWithValue("@b", s.BranchId);
            q.Parameters.AddWithValue("@ch", (int)s.Channel);
            id = q.ExecuteScalar() as int?;
        }

        bool hasApiKey = !string.IsNullOrWhiteSpace(s.ApiKey);
        bool hasSecret = !string.IsNullOrWhiteSpace(s.ApiSecret);

        using var cmd = c.CreateCommand();
        if (id is null)
        {
            cmd.CommandText = @"INSERT INTO dbo.NotificationSettings
                (BranchId, Channel, Provider, ApiKey, ApiSecret, Sender, Host, Port, UseSsl, IsEnabled, TestMode, CreatedBy)
                VALUES (@b,@ch,@p,@a,@k,@sn,@h,@pt,@ssl,@e,@t,@by);";
            CompanyService.AddNullable(cmd, "@a", hasApiKey ? SecretProtector.Protect(s.ApiKey) : null);
            CompanyService.AddNullable(cmd, "@k", hasSecret ? SecretProtector.Protect(s.ApiSecret) : null);
        }
        else
        {
            string apiSet = hasApiKey ? "ApiKey=@a, " : "";
            string secretSet = hasSecret ? "ApiSecret=@k, " : "";
            cmd.CommandText = $@"UPDATE dbo.NotificationSettings SET Provider=@p, {apiSet}{secretSet}
                Sender=@sn, Host=@h, Port=@pt, UseSsl=@ssl, IsEnabled=@e, TestMode=@t,
                UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id.Value);
            if (hasApiKey) CompanyService.AddNullable(cmd, "@a", SecretProtector.Protect(s.ApiKey));
            if (hasSecret) CompanyService.AddNullable(cmd, "@k", SecretProtector.Protect(s.ApiSecret));
        }
        cmd.Parameters.AddWithValue("@b", s.BranchId);
        cmd.Parameters.AddWithValue("@ch", (int)s.Channel);
        cmd.Parameters.AddWithValue("@p", (int)s.Provider);
        CompanyService.AddNullable(cmd, "@sn", s.Sender);
        CompanyService.AddNullable(cmd, "@h", s.Host);
        cmd.Parameters.AddWithValue("@pt", (object?)s.Port ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ssl", s.UseSsl);
        cmd.Parameters.AddWithValue("@e", s.IsEnabled);
        cmd.Parameters.AddWithValue("@t", s.TestMode);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Bildirim gönderir (senkron sarmalayıcı). Bkz. <see cref="SendAsync"/>.</summary>
    public NotifyResult Send(NotificationChannel channel, string to, string? templateKey, string? body,
        IReadOnlyDictionary<string, string?>? model, int branchId, string? subject = null, int? userId = null)
        => SendAsync(channel, to, templateKey, body, model, branchId, subject, userId).GetAwaiter().GetResult();

    /// <summary>Bildirim gönderir: şablon/gövde çöz → ayara göre sağlayıcı seç → gönder → NotificationLog yaz.
    /// Gönderim hatası çağırana <see cref="NotifyResult"/> ile döner; log best-effort (log hatası akışı bozmaz).</summary>
    public async Task<NotifyResult> SendAsync(NotificationChannel channel, string to, string? templateKey, string? body,
        IReadOnlyDictionary<string, string?>? model, int branchId, string? subject = null, int? userId = null)
    {
        // Gövde: body verilmişse onu, yoksa şablon anahtarından hazır şablonu al; sonra yer-tutucuları doldur.
        string template = body ?? NotificationTemplates.Get(templateKey) ?? string.Empty;
        string finalBody = NotificationTemplateEngine.Render(template, model);

        var setting = GetSetting(branchId, channel);
        NotifyResult result;
        try
        {
            if (!setting.IsEnabled || setting.Provider == NotificationProvider.Yok)
            {
                result = NotifyResult.Fail(channel.ToTr(), $"{channel.ToTr()} bildirim kanalı etkin değil (ayarlardan açın).");
            }
            else
            {
                var provider = NotificationProviderFactory.Get(setting.Provider);
                result = provider is null
                    ? NotifyResult.Fail(setting.ProviderText, "Sağlayıcı bulunamadı.")
                    : await provider.SendAsync(new NotifyMessage
                    {
                        Channel = channel, To = to, Subject = subject, Body = finalBody, TemplateKey = templateKey
                    }, setting);
            }
        }
        catch (Exception ex)
        {
            result = NotifyResult.Fail(channel.ToTr(), ex.Message);
        }

        TryLog(branchId, channel, setting.Provider, to, templateKey, finalBody, result);
        return result;
    }

    /// <summary>NotificationLog kaydı (best-effort; hata yut → gönderim sonucunu etkilemez).</summary>
    private static void TryLog(int branchId, NotificationChannel channel, NotificationProvider provider,
        string to, string? templateKey, string body, NotifyResult result)
    {
        try
        {
            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO dbo.NotificationLog
                (BranchId, Channel, Provider, Recipient, TemplateKey, Body, Status, ProviderResponse, Error)
                VALUES (@b,@ch,@p,@to,@tk,@body,@st,@resp,@err);";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@ch", (int)channel);
            cmd.Parameters.AddWithValue("@p", (int)provider);
            CompanyService.AddNullable(cmd, "@to", to);
            CompanyService.AddNullable(cmd, "@tk", templateKey);
            CompanyService.AddNullable(cmd, "@body", body);
            cmd.Parameters.AddWithValue("@st", (int)result.Status);
            CompanyService.AddNullable(cmd, "@resp", result.ProviderResponse);
            CompanyService.AddNullable(cmd, "@err", result.Error);
            cmd.ExecuteNonQuery();
        }
        catch { /* log yazılamadı → gönderim akışını bozma */ }
    }
}
