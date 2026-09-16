using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class UserService
{
    public List<User> GetAll()
    {
        var users = new List<User>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE u.IsDeleted = 0 ORDER BY r.Name, u.Username;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) users.Add(Map(reader));
        return users;
    }

    /// <summary>Seçili şirkete erişimi olan kullanıcılar (+ şube erişim özeti).</summary>
    /// <summary>Seçili şirkete erişimi olan kullanıcılar (+ şube erişim özeti).</summary>
    public List<User> GetForCompany(int companyId, bool includeDeleted = false)
    {
        var users = new List<User>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        // TÜM kullanıcıları listeler; AccessSummary seçili şirketteki erişimi gösterir.
        command.CommandText = @"
SELECT u.Id, u.Username, u.PasswordHash, u.FullName, u.RoleId, r.Name AS RoleName,
       u.IsActive, u.CreatedAt, u.LastLoginAt,
       CASE WHEN r.Name = N'SysAdmin' THEN N'SysAdmin · Tüm şubeler'
            WHEN COUNT(a.Id) = 0 THEN N'Erişim yok'
            WHEN MAX(CASE WHEN a.BranchId IS NULL THEN 1 ELSE 0 END) = 1 THEN N'Tüm Şubeler'
            ELSE STRING_AGG(b.Name, N', ') END AS AccessSummary,
       u.IsDeleted,
       (SELECT STRING_AGG(r2.Name, N', ') FROM dbo.UserRoles ur JOIN dbo.Roles r2 ON r2.Id=ur.RoleId
        WHERE ur.UserId=u.Id AND r2.IsDeleted=0) AS RolesAgg
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
LEFT JOIN dbo.UserCompanyAccess a ON a.UserId = u.Id AND a.CompanyId = @CompanyId AND a.IsDeleted = 0
LEFT JOIN dbo.CompanyBranches b ON b.Id = a.BranchId
WHERE (@inc = 1 OR u.IsDeleted = 0)
GROUP BY u.Id, u.Username, u.PasswordHash, u.FullName, u.RoleId, r.Name, u.IsActive, u.CreatedAt, u.LastLoginAt, u.IsDeleted
ORDER BY r.Name, u.Username;";
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var u = Map(reader);
            u.AccessSummary = reader.IsDBNull(9) ? null : reader.GetString(9);
            u.IsDeleted = reader.GetBoolean(10);
            if (!reader.IsDBNull(11))
                u.Roles = reader.GetString(11).Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
            users.Add(u);
        }
        return users;
    }

    /// <summary>
    /// Seçili şirket+şubeye giriş yapabilecek kullanıcılar (KIOSK kullanıcı seçimi için).
    /// SysAdmin'ler her zaman dahildir.
    /// </summary>
    public List<User> GetForLogin(int companyId, int? branchId)
    {
        var users = new List<User>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT DISTINCT u.Id, u.Username, u.PasswordHash, u.FullName, u.RoleId, r.Name AS RoleName,
       u.IsActive, u.CreatedAt, u.LastLoginAt
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
LEFT JOIN dbo.UserCompanyAccess a
       ON a.UserId = u.Id AND a.CompanyId = @CompanyId AND a.IsDeleted = 0
      AND (a.BranchId IS NULL OR a.BranchId = @BranchId)
WHERE u.IsDeleted = 0 AND u.IsActive = 1
      AND (r.Name = N'SysAdmin' OR a.Id IS NOT NULL)
ORDER BY u.FullName;";
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read()) users.Add(Map(reader));
        return users;
    }

    /// <summary>Tek kullanıcı (sistem DB, silinmemiş). Web back-office düzenleme için (additive; parola hash döner ama UI'da gösterilmez).</summary>
    public User? GetById(int id)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE u.Id = @Id AND u.IsDeleted = 0;";
        command.Parameters.AddWithValue("@Id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool UsernameExists(string username, int excludeId = 0)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Users WHERE Username=@u AND IsDeleted=0 AND Id<>@ex;";
        command.Parameters.AddWithValue("@u", username.Trim());
        command.Parameters.AddWithValue("@ex", excludeId);
        return (int)command.ExecuteScalar() > 0;
    }

    /// <summary>LİSANS: aktif kullanıcı sayısı paket limitine ulaştıysa yeni kullanıcı engellenir.
    /// Sayım İLGİLİ ŞİRKETE kapsamlıdır (çok-kiracılı SaaS): o şirkete erişimi olan aktif kullanıcılar
    /// (UserCompanyAccess üzerinden) + global SysAdmin'ler sayılır. Enterprise (MaxUsers=int.MaxValue) sınırsızdır.</summary>
    private static void EnforceUserLimit(int companyId)
    {
        var lic = Helpers.Session.CurrentLicense;
        if (lic is null || lic.MaxUsers == int.MaxValue) return;
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        // Kapsam: yalnız bu şirkete erişimi olan aktif/silinmemiş kullanıcılar; SysAdmin (global) korunur.
        cmd.CommandText = @"
SELECT COUNT(DISTINCT u.Id)
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
LEFT JOIN dbo.UserCompanyAccess uca ON uca.UserId = u.Id AND uca.CompanyId = @c AND uca.IsDeleted = 0
WHERE u.IsActive = 1 AND u.IsDeleted = 0
  AND (r.Name = N'SysAdmin' OR uca.Id IS NOT NULL);";
        cmd.Parameters.AddWithValue("@c", companyId);
        int active = Convert.ToInt32(cmd.ExecuteScalar());
        if (active >= lic.MaxUsers)
            throw new InvalidOperationException($"Lisans paketiniz ({lic.PackageName}) en fazla {lic.MaxUsers} aktif kullanıcıya izin veriyor. Yeni kullanıcı eklemek için paketi yükseltin ya da bir kullanıcıyı pasife alın.");
    }

    public User Create(string username, string fullName, IReadOnlyList<int> roleIds, bool isActive, string password, int? createdByUserId)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Kullanıcı adı boş olamaz.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Ad soyad boş olamaz.");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Parola boş olamaz.");
        if (roleIds.Count == 0) throw new ArgumentException("En az bir rol seçiniz.");
        if (UsernameExists(username)) throw new InvalidOperationException($"'{username}' kullanıcı adı zaten kullanılıyor.");
        // Limit sayımı aktif şirkete kapsamlı; şirket bağlamı yoksa 0 (yalnız global SysAdmin'ler sayılır).
        EnforceUserLimit(Helpers.Session.CurrentCompany?.Id ?? 0);

        int primary = roleIds[0];
        using var connection = Database.OpenConnection();
        using var tx = connection.BeginTransaction();
        try
        {
            int id; DateTime createdAt;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = @"
INSERT INTO dbo.Users (Username, PasswordHash, FullName, RoleId, IsActive, CreatedBy)
OUTPUT INSERTED.Id, INSERTED.CreatedAt
VALUES (@Username, @PasswordHash, @FullName, @RoleId, @IsActive, @CreatedBy);";
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@PasswordHash", PasswordHasher.Hash(password));
                command.Parameters.AddWithValue("@FullName", fullName.Trim());
                command.Parameters.AddWithValue("@RoleId", primary);
                command.Parameters.AddWithValue("@IsActive", isActive);
                command.Parameters.AddWithValue("@CreatedBy", (object?)createdByUserId ?? DBNull.Value);
                using var reader = command.ExecuteReader();
                reader.Read();
                id = reader.GetInt32(0); createdAt = reader.GetDateTime(1);
            }
            new UserRoleService().SetRoles(id, roleIds, connection, tx);
            tx.Commit();
            // AUDIT (best-effort): yeni kullanıcı (parola HARİÇ). Yetki/erişim izi.
            AuditService.Write("User", id, "Insert", null,
                new { Username = username, FullName = fullName.Trim(), RoleId = primary, RoleIds = roleIds, IsActive = isActive },
                createdByUserId);
            return new User { Id = id, Username = username, FullName = fullName.Trim(), RoleId = primary, IsActive = isActive, CreatedAt = createdAt };
        }
        catch { tx.Rollback(); throw; }
    }

    public void Update(int id, string fullName, IReadOnlyList<int> roleIds, bool isActive, int? updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Ad soyad boş olamaz.");
        if (roleIds.Count == 0) throw new ArgumentException("En az bir rol seçiniz.");
        int primary = roleIds[0];
        // AUDIT: güncelleme öncesi durum (yetki/aktiflik değişimi izi) — transaction DIŞINDA snapshot.
        var beforeUser = GetById(id);
        // KİLİTLENME KORUMASI (S4): kendi ya da SON aktif Süper Admin'in yetkisini/aktifliğini kaldırma engeli.
        if (beforeUser is not null && beforeUser.IsActive && UserHasSysAdminRole(id))
        {
            int saRoleId = GetRoleIdByName("SysAdmin");
            bool stillActiveSysAdmin = isActive && saRoleId != 0 && roleIds.Contains(saRoleId);
            if (!stillActiveSysAdmin)
            {
                if (updatedByUserId == id)
                    throw new InvalidOperationException("Kendi Süper Admin yetkinizi veya aktifliğinizi kaldıramazsınız.");
                if (CountActiveSysAdmins() <= 1)
                    throw new InvalidOperationException("Sistemdeki son Süper Admin yetkisi kaldırılamaz; önce başka bir aktif Süper Admin tanımlayın.");
            }
        }
        using var connection = Database.OpenConnection();
        using var tx = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = @"
UPDATE dbo.Users SET FullName=@FullName, RoleId=@RoleId, IsActive=@IsActive,
    UpdatedBy=@UpdatedBy, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
                command.Parameters.AddWithValue("@FullName", fullName.Trim());
                command.Parameters.AddWithValue("@RoleId", primary);
                command.Parameters.AddWithValue("@IsActive", isActive);
                command.Parameters.AddWithValue("@UpdatedBy", (object?)updatedByUserId ?? DBNull.Value);
                command.Parameters.AddWithValue("@Id", id);
                command.ExecuteNonQuery();
            }
            new UserRoleService().SetRoles(id, roleIds, connection, tx);
            tx.Commit();
            // AUDIT (best-effort): kullanıcı güncelleme (parola HARİÇ).
            AuditService.Write("User", id, "Update",
                beforeUser is null ? null : new { beforeUser.FullName, beforeUser.RoleId, beforeUser.IsActive },
                new { FullName = fullName.Trim(), RoleId = primary, RoleIds = roleIds, IsActive = isActive },
                updatedByUserId);
        }
        catch { tx.Rollback(); throw; }
    }

    public void SetPassword(int id, string password, int? updatedByUserId)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Parola boş olamaz.");
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        // Parola değişince ilk-giriş-değişim zorunluluğu kalkar (zayıf varsayılan artık geçersiz).
        command.CommandText = @"
UPDATE dbo.Users SET PasswordHash=@Hash, MustChangePassword=0, UpdatedBy=@UpdatedBy, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Hash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("@UpdatedBy", (object?)updatedByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Kullanıcıyı aktif/pasif yapar (cross-tenant kullanıcı yönetimi — SysAdmin panelinden).
    /// Pasife alınan kullanıcı bir sonraki girişte AuthService.Authenticate tarafından reddedilir
    /// (AuthResult.UserInactive); ayrı bir oturum-kapatma gerekmez. İz için Audit yazılır.
    /// </summary>
    public void SetActive(int userId, bool isActive, int? byUserId)
    {
        // AUDIT: değişim öncesi durum (yetki/aktiflik izi) — güncelleme öncesi snapshot.
        var before = GetById(userId);
        // KİLİTLENME KORUMASI (S4): kendini ya da SON aktif Süper Admin'i pasifleştirip sistemi kilitleme engeli.
        if (!isActive && UserHasSysAdminRole(userId))
        {
            if (byUserId == userId)
                throw new InvalidOperationException("Kendinizi pasifleştiremezsiniz.");
            if (CountActiveSysAdmins() <= 1)
                throw new InvalidOperationException("Sistemdeki son aktif Süper Admin pasifleştirilemez; önce başka bir aktif Süper Admin tanımlayın.");
        }
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.Users SET IsActive=@a, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        command.Parameters.AddWithValue("@a", isActive);
        command.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", userId);
        command.ExecuteNonQuery();
        // AUDIT (best-effort): aktiflik değişimi.
        AuditService.Write("User", userId, "SetActive",
            before is null ? null : new { before.IsActive },
            new { IsActive = isActive }, byUserId);
    }

    /// <summary>
    /// Kullanıcının parolasını güçlü rastgele bir GEÇİCİ parola ile sıfırlar ve MustChangePassword=1 damgalar
    /// (kullanıcı ilk girişte değiştirmek zorunda kalır). Üretilen düz geçici parola DÖNER — çağıran taraf
    /// bunu kullanıcıya TEK SEFER göstermelidir; hiçbir yerde saklanmaz/loglanmaz.
    /// Parola PasswordHasher (PBKDF2) ile özetlenir; düz metin DB'ye yazılmaz.
    /// </summary>
    /// <returns>Üretilen düz geçici parola (yalnızca çağıran gösterir).</returns>
    public string ResetPassword(int userId, int? byUserId)
    {
        string tempPassword = GenerateTempPassword();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        // MustChangePassword=1 → geçici parola canlıda kalamaz; ilk girişte değişim zorunlu.
        command.CommandText = @"
UPDATE dbo.Users SET PasswordHash=@Hash, MustChangePassword=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Hash", PasswordHasher.Hash(tempPassword));
        command.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", userId);
        command.ExecuteNonQuery();
        // AUDIT (best-effort): parola sıfırlama — PAROLA ASLA LOGLANMAZ, yalnız eylem izi.
        AuditService.Write("User", userId, "ResetPassword", null, new { Reset = true }, byUserId);
        return tempPassword;
    }

    /// <summary>Güçlü rastgele geçici parola (kriptografik RNG; üst/alt/rakam/simge karışık, 18 karakter).
    /// ProvisionService'teki geçici-parola deseninin eşi.</summary>
    private static string GenerateTempPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*+-?";
        string all = upper + lower + digits + symbols;

        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)],
        };
        while (chars.Count < 18)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Fisher-Yates karıştırma → garanti-karakterlerin konumu tahmin edilemesin.
        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    /// <summary>Kullanıcı şu an aktif mi (silinmemiş + IsActive). Cookie oturum yeniden-doğrulaması için hızlı sorgu (S4).</summary>
    public bool IsActive(int userId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CAST(CASE WHEN IsActive=1 AND IsDeleted=0 THEN 1 ELSE 0 END AS bit) FROM dbo.Users WHERE Id=@Id;";
        cmd.Parameters.AddWithValue("@Id", userId);
        var o = cmd.ExecuteScalar();
        return o is bool b && b;
    }

    /// <summary>Kullanıcı (aktiflik gözetmeksizin) SysAdmin rolü taşıyor mu — birincil rol veya UserRoles üzerinden. Kilitlenme korumasında kullanılır.</summary>
    public bool UserHasSysAdminRole(int userId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(1) FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
LEFT JOIN dbo.UserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.Roles r2 ON r2.Id = ur.RoleId AND r2.IsDeleted = 0
WHERE u.Id = @Id AND (r.Name = N'SysAdmin' OR r2.Name = N'SysAdmin');";
        cmd.Parameters.AddWithValue("@Id", userId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Sistemdeki AKTİF (silinmemiş) Süper Admin kullanıcı sayısı — kendini/son SysAdmin'i kilitleme korumasında kullanılır (S4).</summary>
    public int CountActiveSysAdmins()
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(DISTINCT u.Id) FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
LEFT JOIN dbo.UserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.Roles r2 ON r2.Id = ur.RoleId AND r2.IsDeleted = 0
WHERE u.IsDeleted = 0 AND u.IsActive = 1 AND (r.Name = N'SysAdmin' OR r2.Name = N'SysAdmin');";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetRoleIdByName(string name)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 Id FROM dbo.Roles WHERE Name=@n AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@n", name);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? 0 : Convert.ToInt32(o);
    }

    private const string SelectBase = @"
SELECT u.Id, u.Username, u.PasswordHash, u.FullName, u.RoleId, r.Name AS RoleName,
       u.IsActive, u.CreatedAt, u.LastLoginAt
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId";

    private static User Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Username = r.GetString(1),
        PasswordHash = r.GetString(2),
        FullName = r.GetString(3),
        RoleId = r.GetInt32(4),
        RoleName = r.GetString(5),
        IsActive = r.GetBoolean(6),
        CreatedAt = r.GetDateTime(7),
        LastLoginAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
    };
}
