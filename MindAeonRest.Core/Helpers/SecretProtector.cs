using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MindAeonRest.Data;

namespace MindAeonRest.Helpers;

/// <summary>
/// Veritabanında DÜZ saklanmaması gereken küçük sırları (entegrasyon API/secret anahtarları) şifreler.
///
/// <para><b>v2 (güncel):</b> AES-256-GCM ile platform-bağımsız şifreleme. Anahtar ŞİRKET VERİTABANINDAN
/// (<c>dbo.AppSecretKeys</c>, ilk kullanımda üretilip yazılır) okunur → aynı şirket DB'sine bağlanan TÜM
/// terminaller ve web AYNI anahtarı görür; sır, şifreleyen makine dışında da çözülür. Çıktı biçimi:
/// <c>v2:</c> + Base64(iv[12] ‖ tag[16] ‖ ciphertext).</para>
///
/// <para><b>Neden makineye özel DEĞİL:</b> Eski sürüm DPAPI <see cref="DataProtectionScope.LocalMachine"/>
/// kullanıyordu; şifreleyen makine dışında (2. terminal / web) çözülemiyordu. Anahtarı şirket DB'sinde
/// tutmak, sırrı okuyan her düğümün zaten AYNI şirket DB'sini açıyor olmasından yararlanır → anahtar
/// dağıtımı problemsiz ve makineden bağımsızdır. (appsettings'e koymak per-kurulum farklılaşabilir /
/// repoya kaçabilir; ProgramData makineye özel olurdu — ikisi de çok-terminal için yanlış.)</para>
///
/// <para><b>Geriye uyum (okuma):</b> <c>v2:</c> → AES-GCM; <c>enc:v1:</c> → eski DPAPI (yalnız Windows,
/// orijinal makinede çözülür); ön ek yoksa eski DÜZ kayıt kabul edilip aynen döner.</para>
/// </summary>
public static class SecretProtector
{
    private const string PrefixV2 = "v2:";        // AES-256-GCM
    private const string PrefixV1 = "enc:v1:";    // eski DPAPI (LocalMachine) — yalnız geriye-okuma
    private const string KeyName = "SecretProtector";
    private const int IvSize = 12;                // AES-GCM nonce
    private const int TagSize = 16;               // AES-GCM auth tag

    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("MindAeonRest.SecretProtector.v1");

    // Şirket (DB) başına AES anahtarı — çok-kiracılı (web) için DB adına göre önbelleklenir.
    private static readonly ConcurrentDictionary<string, byte[]> _keyCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Düz metni AES-256-GCM ile şifreler (<c>v2:</c> + base64). Boş/null ise aynen döner;
    /// zaten şifreli (v2:/enc:v1:) ise dokunmaz. Boş OLMAYAN bir sır şifrelenemezse (anahtar okunamadı vb.)
    /// veri kaybını önlemek için DÜZ ya da null yazmaz, <see cref="InvalidOperationException"/> fırlatır.</summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        if (plain.StartsWith(PrefixV2, StringComparison.Ordinal) ||
            plain.StartsWith(PrefixV1, StringComparison.Ordinal)) return plain; // zaten şifreli
        try
        {
            byte[] key = GetKey();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plain);
            byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
            byte[] cipher = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSize];
            using (var gcm = new AesGcm(key, TagSize))
                gcm.Encrypt(iv, plainBytes, cipher, tag);

            byte[] payload = new byte[IvSize + TagSize + cipher.Length];
            Buffer.BlockCopy(iv, 0, payload, 0, IvSize);
            Buffer.BlockCopy(tag, 0, payload, IvSize, TagSize);
            Buffer.BlockCopy(cipher, 0, payload, IvSize + TagSize, cipher.Length);
            return PrefixV2 + Convert.ToBase64String(payload);
        }
        catch (Exception ex)
        {
            // Sırrı DÜZ ya da null yazmaktansa hata ver (fail-closed) → çağıran veriyi ezmez/kaybetmez.
            throw new InvalidOperationException("Sır şifrelenemedi (anahtar okunamadı); değer yazılmadı.", ex);
        }
    }

    /// <summary>Şifreli değeri çözer. <c>v2:</c> → AES-GCM; <c>enc:v1:</c> → eski DPAPI (Windows);
    /// ön ek yoksa eski DÜZ kayıt kabul edilip aynen döner. Çözülemezse null.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;

        if (stored.StartsWith(PrefixV2, StringComparison.Ordinal))
        {
            try
            {
                byte[] payload = Convert.FromBase64String(stored[PrefixV2.Length..]);
                if (payload.Length < IvSize + TagSize) return null;
                byte[] key = GetKey();
                var iv = payload.AsSpan(0, IvSize);
                var tag = payload.AsSpan(IvSize, TagSize);
                var cipher = payload.AsSpan(IvSize + TagSize);
                byte[] plain = new byte[cipher.Length];
                using (var gcm = new AesGcm(key, TagSize))
                    gcm.Decrypt(iv, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }

        if (stored.StartsWith(PrefixV1, StringComparison.Ordinal))
        {
            // Eski DPAPI kayıtları: yalnız şifreleyen (orijinal) Windows makinesinde çözülebilir.
            try
            {
                byte[] cipher = Convert.FromBase64String(stored[PrefixV1.Length..]);
                byte[] plain = ProtectedData.Unprotect(cipher, LegacyEntropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }

        return stored; // ön ek yok → eski düz kayıt
    }

    /// <summary>Aktif şirket DB'sindeki AES-256 anahtarını döndürür (DB adına göre önbellekli).
    /// Yoksa üretip yazar → tüm terminaller/web aynı değeri okur.</summary>
    private static byte[] GetKey()
    {
        string db = Session.CurrentCompany?.DatabaseName
            ?? throw new InvalidOperationException("Aktif şirket bulunamadı; sır anahtarı çözülemez.");
        return _keyCache.GetOrAdd(db, LoadOrCreateKey);
    }

    private static byte[] LoadOrCreateKey(string databaseName)
    {
        using var c = Database.OpenConnection(databaseName);

        // Anahtar tablosunu (yoksa) oluştur — mevcut DB'ler için tembel migration.
        using (var ddl = c.CreateCommand())
        {
            ddl.CommandText = @"
IF OBJECT_ID(N'dbo.AppSecretKeys', N'U') IS NULL
CREATE TABLE dbo.AppSecretKeys (
    Name NVARCHAR(100) NOT NULL PRIMARY KEY,
    KeyValue NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
);";
            ddl.ExecuteNonQuery();
        }

        byte[]? existing = ReadKey(c);
        if (existing != null) return existing;

        // Yoksa üret + atomik/idempotent yaz (yarış olursa PK ihlalini yut, sonra yeniden oku).
        byte[] fresh = RandomNumberGenerator.GetBytes(32); // AES-256
        try
        {
            using var ins = c.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.AppSecretKeys (Name, KeyValue)
SELECT @n, @v WHERE NOT EXISTS (SELECT 1 FROM dbo.AppSecretKeys WHERE Name=@n);";
            ins.Parameters.AddWithValue("@n", KeyName);
            ins.Parameters.AddWithValue("@v", Convert.ToBase64String(fresh));
            ins.ExecuteNonQuery();
        }
        catch { /* eşzamanlı başka düğüm yazmış olabilir → aşağıda yeniden oku */ }

        return ReadKey(c) ?? throw new InvalidOperationException("Sır anahtarı okunamadı/yazılamadı.");
    }

    private static byte[]? ReadKey(Microsoft.Data.SqlClient.SqlConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT KeyValue FROM dbo.AppSecretKeys WHERE Name=@n;";
        cmd.Parameters.AddWithValue("@n", KeyName);
        var v = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(v)) return null;
        try { return Convert.FromBase64String(v); } catch { return null; }
    }
}
