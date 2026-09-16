using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services.Ai;

/// <summary>AI Hub ayarları (şirket DB, tek satır id=1).</summary>
public class AiSettingsService
{
    public AiSettings Get()
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Provider, ApiKey, Model, BaseUrl, Temperature, VoiceOutput, VoiceInput FROM dbo.AiSettings WHERE Id=1;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new AiSettings();
        return new AiSettings
        {
            Provider = (AiProviderType)r.GetInt32(0),
            ApiKey = r.IsDBNull(1) ? null : Decrypt(r.GetString(1)),
            Model = r.IsDBNull(2) ? null : r.GetString(2),
            BaseUrl = r.IsDBNull(3) ? null : r.GetString(3),
            Temperature = (double)r.GetDecimal(4),
            VoiceOutput = r.GetBoolean(5),
            VoiceInput = r.GetBoolean(6)
        };
    }

    public void Save(AiSettings s, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.AiSettings WHERE Id=1)
    UPDATE dbo.AiSettings SET Provider=@p, ApiKey=@k, Model=@m, BaseUrl=@u, Temperature=@t, VoiceOutput=@vo, VoiceInput=@vi, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=1;
ELSE
    INSERT INTO dbo.AiSettings (Id, Provider, ApiKey, Model, BaseUrl, Temperature, VoiceOutput, VoiceInput, CreatedBy)
    VALUES (1,@p,@k,@m,@u,@t,@vo,@vi,@by);";
        cmd.Parameters.AddWithValue("@p", (int)s.Provider);
        cmd.Parameters.AddWithValue("@k", (object?)Encrypt(s.ApiKey) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", (object?)s.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", (object?)s.BaseUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", (decimal)s.Temperature);
        cmd.Parameters.AddWithValue("@vo", s.VoiceOutput);
        cmd.Parameters.AddWithValue("@vi", s.VoiceInput);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>API anahtarını yazmadan önce AES-256-GCM ile şifreler. Boş/null anahtar aynen döner
    /// (şifrelenmez). Oturumda aktif şirket yoksa (anahtar okunamaz) veri kaybını önlemek için ham
    /// değeri döndürür — Save normalde yalnız oturum bağlamında çağrılır.</summary>
    private static string? Encrypt(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return apiKey;                 // boşu şifreleme
        if (Session.CurrentCompany is null) return apiKey;              // null-guard: anahtar okunamaz → ham yaz
        return SecretProtector.Protect(apiKey);
    }

    /// <summary>Okunan API anahtarını çözer. Legacy DÜZ METİN kayıtlar (ön ek yok) SecretProtector
    /// tarafından zaten aynen döner; çözüm başarısız olur/istisna atarsa ham değeri OLDUĞU GİBİ döndürür
    /// (geriye-uyum: eski düz metin anahtarlar).</summary>
    private static string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        try { return SecretProtector.Unprotect(stored) ?? stored; }
        catch { return stored; }
    }
}
