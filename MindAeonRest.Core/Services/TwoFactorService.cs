using MindAeonRest.Data;

namespace MindAeonRest.Services;

/// <summary>
/// Kullanıcıların 2FA (TOTP) durumuna SİSTEM (master) DB'sinde erişim — <c>dbo.Users</c> üzerindeki
/// additive kolonlar <c>TwoFactorEnabled</c> / <c>TwoFactorSecret</c>.
///
/// <para>Secret ŞİFRELİ bir katar olarak saklanır/döner; bu servis şifrelemeye KARIŞMAZ (opak taşır).
/// Şifreleme web katmanında master-seviye <c>IDataProtector</c> ile yapılır (kullanıcı master DB'de olduğundan
/// şirket-DB anahtarlı SecretProtector kullanılamaz). Kolon garantisi: DatabaseInitializer.EnsureTwoFactorSchema.</para>
/// </summary>
public class TwoFactorService
{
    /// <summary>Kullanıcının 2FA durumu + (varsa) şifreli secret. Kullanıcı yoksa (false, null).</summary>
    public (bool Enabled, string? ProtectedSecret) Get(int userId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT ISNULL(TwoFactorEnabled, 0), TwoFactorSecret
FROM dbo.Users WHERE Id = @Id AND IsDeleted = 0;";
        cmd.Parameters.AddWithValue("@Id", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (false, null);
        bool enabled = !r.IsDBNull(0) && r.GetBoolean(0);
        string? secret = r.IsDBNull(1) ? null : r.GetString(1);
        return (enabled, secret);
    }

    /// <summary>Kullanıcının 2FA'sı etkin mi (login akışında hızlı kontrol; secret çözmeye gerek yok).</summary>
    public bool IsEnabled(int userId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(TwoFactorEnabled, 0) FROM dbo.Users WHERE Id = @Id AND IsDeleted = 0;";
        cmd.Parameters.AddWithValue("@Id", userId);
        var v = cmd.ExecuteScalar();
        return v is bool b && b;
    }

    /// <summary>Şifreli secret'ı yazar + 2FA'yı ETKİNLEŞTİRİR (kayıt onayı sonrası tek adım).</summary>
    public void EnableWithSecret(int userId, string protectedSecret, int? updatedByUserId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.Users
SET TwoFactorSecret = @Secret, TwoFactorEnabled = 1,
    UpdatedBy = @By, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Secret", protectedSecret);
        cmd.Parameters.AddWithValue("@By", (object?)updatedByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>2FA'yı DEVRE DIŞI bırakır + secret'ı temizler.</summary>
    public void Disable(int userId, int? updatedByUserId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.Users
SET TwoFactorSecret = NULL, TwoFactorEnabled = 0,
    UpdatedBy = @By, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@By", (object?)updatedByUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", userId);
        cmd.ExecuteNonQuery();
    }
}
