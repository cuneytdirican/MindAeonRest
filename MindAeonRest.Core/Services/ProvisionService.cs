using System.Security.Cryptography;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

// ===== Provisioning sonuç modelleri (Core; Api DTO'larından bağımsız) =====

/// <summary>Yeni tenant kurulum sonucu. TempPassword YALNIZ burada, tek seferliktir (asla saklanmaz/tekrar dönmez).</summary>
public sealed record TenantCreatedResult(string Code, string Name, string DbName, string AdminUsername, string TempPassword);

/// <summary>Tenant özeti (salt-okuma denetim listesi).</summary>
public sealed record TenantSummary(string Code, string Name, string DbName, int BranchCount, int UserCount);

/// <summary>Tenant kullanıcı satırı (salt-okuma denetim).</summary>
public sealed record TenantUserInfo(string Username, string FullName, string RoleName, bool IsActive);

/// <summary>Tenant şube satırı (salt-okuma denetim).</summary>
public sealed record TenantBranchInfo(int Id, string Name, bool IsActive);

/// <summary>Aynı kodlu tenant zaten mevcut → çağıran 409 üretmeli.</summary>
public sealed class TenantAlreadyExistsException : Exception
{
    public TenantAlreadyExistsException(string code)
        : base($"'{code}' kodlu bir tenant zaten mevcut.") { }
}

/// <summary>
/// GÜVENLİ tenant PROVISIONING servisi. CREATE DATABASE gücü taşıdığından çağıranlar (ProvisionController)
/// FAIL-CLOSED kapı ardında olmalıdır. Bu servis yalnız şirket+DB+MERKEZ kurar ve tenant'a KİLİTLİ ilk yöneticiyi tohumlar.
///
/// GÜVENLİK: ilk-admin ASLA SysAdmin DEĞİLDİR (SysAdmin çapraz-tenant/bypass). Yalnız <b>Yönetici</b> rolü +
/// YALNIZ bu yeni şirkete <see cref="UserCompanyAccess"/> verilir → pratikte kendi tenant'ına kilitli.
/// Güçlü rastgele geçici parola üretilir, MustChangePassword=1 damgalanır ve TempPassword TEK SEFER döner.
/// </summary>
public class ProvisionService
{
    private readonly CompanyService _companies = new();
    private readonly BranchService _branches = new();

    /// <summary>
    /// Yeni tenant kurar: (a) şirket + MindAeonRest_{Kod} DB + MERKEZ şubesi (CompanyService.Create),
    /// (b) tenant'a kilitli ilk yönetici (Yönetici rolü + tek-şirket UserCompanyAccess).
    /// KRİTİK: ilk-admin seed'i <c>EnforceUserLimit</c>'i ATLAR (henüz lisans/Session yok) — doğrudan INSERT.
    /// </summary>
    /// <exception cref="TenantAlreadyExistsException">Kod zaten kullanılıyor.</exception>
    public TenantCreatedResult CreateTenant(string code, string name, string adminEmail, string adminFullName)
    {
        string normalized = CodeHelper.Normalize(code);
        if (normalized.Length == 0)
            throw new ArgumentException("Geçerli bir tenant kodu giriniz (yalnızca harf/rakam).");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant adı boş olamaz.");
        if (string.IsNullOrWhiteSpace(adminFullName))
            throw new ArgumentException("Yönetici ad soyad boş olamaz.");

        // Ön kontrol: zaten varsa (409). CompanyService.Create içinde de yarış-korumalı kontrol var.
        if (_companies.CodeExists(normalized))
            throw new TenantAlreadyExistsException(normalized);

        // ÖN KOŞULLAR ÖNCE (fail-fast): DB oluşturmadan önce Yönetici rolü + kullanıcı adı + parola hazır olsun;
        // biri başarısızsa (ör. Yönetici rolü yok) YETİM tenant (DB var, admin yok, 409'a takılıp yeniden
        // kurulamaz) OLUŞMAZ — hata fiziksel DB oluşmadan önce fırlar.
        int yoneticiRoleId = GetYoneticiRoleId();
        string username = ResolveUsername(adminEmail, normalized);
        string tempPassword = GenerateTempPassword();

        // (a) Şirket + DB + MERKEZ. createdBy YOK (sistem provisioning; oturum yok).
        Company company;
        try
        {
            company = _companies.Create(normalized, name, currencyId: null, createdByUserId: null);
        }
        catch (InvalidOperationException)
        {
            // Yarış: iki eşzamanlı istek → ikinci CodeExists 409'a çevrilir.
            throw new TenantAlreadyExistsException(normalized);
        }

        // (b) Tenant'a kilitli ilk yönetici. Seed başarısızsa TELAFİ: yeni oluşan (boş) DB + Companies kaydı
        // geri alınır → kod yeniden kullanılabilir olur, yetim tenant (DB var/admin yok) kalmaz.
        try
        {
            SeedTenantAdmin(company.Id, username, adminFullName.Trim(), yoneticiRoleId, tempPassword);
        }
        catch
        {
            try { CompensateFailedProvision(company); } catch { /* telafi best-effort; asıl hata yeniden fırlatılır */ }
            throw;
        }

        return new TenantCreatedResult(normalized, company.Name, company.DatabaseName, username, tempPassword);
    }

    /// <summary>Yarım kalan provisioning'i geri alır: yeni oluşan (boş) tenant DB'sini DROP eder + Companies
    /// kaydını hard-delete eder. Kod alfasayısal (CodeHelper.Normalize) türevi olduğundan DB adı enjeksiyona kapalı.
    /// Sistem DB bağlantısından çalışır (silinen DB'ye bağlı değildir).</summary>
    private static void CompensateFailedProvision(Company company)
    {
        using var connection = Database.OpenConnection();
        var dbName = company.DatabaseName;
        if (!string.IsNullOrWhiteSpace(dbName) && dbName.IndexOfAny(new[] { '[', ']', ';', '\'', '-', ' ' }) < 0)
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = $@"IF DB_ID(@db) IS NOT NULL
BEGIN
    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{dbName}];
END";
            drop.Parameters.AddWithValue("@db", dbName);
            drop.ExecuteNonQuery();
        }
        using var del = connection.CreateCommand();
        del.CommandText = "DELETE FROM dbo.Companies WHERE Id=@id;";
        del.Parameters.AddWithValue("@id", company.Id);
        del.ExecuteNonQuery();
    }

    /// <summary>Tüm tenant'lar: kod, ad, DB + şube sayısı (silinmemiş) + kullanıcı sayısı (UserCompanyAccess distinct).</summary>
    public List<TenantSummary> ListTenants()
    {
        var list = new List<TenantSummary>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT c.Code, c.Name, c.DatabaseName,
       (SELECT COUNT(1) FROM dbo.CompanyBranches b WHERE b.CompanyId = c.Id AND b.IsDeleted = 0) AS BranchCount,
       (SELECT COUNT(DISTINCT a.UserId) FROM dbo.UserCompanyAccess a WHERE a.CompanyId = c.Id AND a.IsDeleted = 0) AS UserCount
FROM dbo.Companies c
WHERE c.IsDeleted = 0
ORDER BY c.Code;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TenantSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }
        return list;
    }

    /// <summary>Tenant kullanıcıları (salt-okuma denetim). UserCompanyAccess üzerinden bu şirkete erişimi olanlar.</summary>
    public List<TenantUserInfo>? ListTenantUsers(string code)
    {
        var company = _companies.GetByCode(code);
        if (company is null) return null;

        var list = new List<TenantUserInfo>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT DISTINCT u.Username, u.FullName, r.Name AS RoleName, u.IsActive
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
INNER JOIN dbo.UserCompanyAccess a ON a.UserId = u.Id AND a.CompanyId = @CompanyId AND a.IsDeleted = 0
WHERE u.IsDeleted = 0
ORDER BY u.Username;";
        command.Parameters.AddWithValue("@CompanyId", company.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TenantUserInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }
        return list;
    }

    /// <summary>Tenant şubeleri (salt-okuma denetim). Bilinmeyen kod → null.</summary>
    public List<TenantBranchInfo>? ListTenantBranches(string code)
    {
        var company = _companies.GetByCode(code);
        if (company is null) return null;

        return _branches.GetByCompany(company.Id)
            .Select(b => new TenantBranchInfo(b.Id, b.Name, b.IsActive))
            .ToList();
    }

    // ===== iç yardımcılar =====

    /// <summary>Yönetici rol Id (SysAdmin DEĞİL). Rol yoksa OLUŞTURUR (provisioning self-contained; eski sistem DB'si
    /// DatabaseInitializer'ı çalıştırmamış olabilir). Minimal insert (Name+Description) — CanUseAi vb. varsa DEFAULT'a
    /// düşer, yoksa da sorun olmaz. Tenant admin'i Yönetici'ye bağlayabilmek için rol garanti edilir.</summary>
    private static int GetYoneticiRoleId()
    {
        using var connection = Database.OpenConnection();
        using (var sel = connection.CreateCommand())
        {
            sel.CommandText = "SELECT Id FROM dbo.Roles WHERE Name = N'Yönetici' AND IsDeleted = 0;";
            var existing = sel.ExecuteScalar();
            if (existing is not null && existing is not DBNull) return Convert.ToInt32(existing);
        }
        using var ins = connection.CreateCommand();
        ins.CommandText = @"IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = N'Yönetici' AND IsDeleted = 0)
    INSERT INTO dbo.Roles (Name, Description) VALUES (N'Yönetici', N'Şube/işletme yöneticisi — yönetim ekranları');
SELECT Id FROM dbo.Roles WHERE Name = N'Yönetici' AND IsDeleted = 0;";
        var id = ins.ExecuteScalar();
        if (id is null || id is DBNull)
            throw new InvalidOperationException("'Yönetici' rolü oluşturulamadı (sistem veritabanı Roles tablosu hazır değil).");
        return Convert.ToInt32(id);
    }

    /// <summary>
    /// İlk-admin'i DOĞRUDAN INSERT ile tohumlar → <c>EnforceUserLimit</c> ATLANIR (henüz lisans/Session yok).
    /// Users + UserRoles + UserCompanyAccess (BranchId NULL = bu şirketin tüm şubeleri) tek transaction'da.
    /// MustChangePassword=1 → ilk girişte parola değişimi zorunlu (geçici parola canlıda kalamaz).
    /// </summary>
    private static void SeedTenantAdmin(int companyId, string username, string fullName, int roleId, string tempPassword)
    {
        using var connection = Database.OpenConnection();
        using var tx = connection.BeginTransaction();
        try
        {
            int userId;
            using (var insUser = connection.CreateCommand())
            {
                insUser.Transaction = tx;
                insUser.CommandText = @"
INSERT INTO dbo.Users (Username, PasswordHash, FullName, RoleId, IsActive, MustChangePassword)
OUTPUT INSERTED.Id
VALUES (@Username, @PasswordHash, @FullName, @RoleId, 1, 1);";
                insUser.Parameters.AddWithValue("@Username", username);
                insUser.Parameters.AddWithValue("@PasswordHash", PasswordHasher.Hash(tempPassword));
                insUser.Parameters.AddWithValue("@FullName", fullName);
                insUser.Parameters.AddWithValue("@RoleId", roleId);
                userId = Convert.ToInt32(insUser.ExecuteScalar());
            }

            using (var insRole = connection.CreateCommand())
            {
                insRole.Transaction = tx;
                insRole.CommandText = @"
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);";
                insRole.Parameters.AddWithValue("@UserId", userId);
                insRole.Parameters.AddWithValue("@RoleId", roleId);
                insRole.ExecuteNonQuery();
            }

            // YALNIZ bu şirkete erişim (BranchId NULL = tüm şubeler). Tek-şirket → tenant'a kilitli.
            using (var insAccess = connection.CreateCommand())
            {
                insAccess.Transaction = tx;
                insAccess.CommandText = @"
INSERT INTO dbo.UserCompanyAccess (UserId, CompanyId, BranchId)
VALUES (@UserId, @CompanyId, NULL);";
                insAccess.Parameters.AddWithValue("@UserId", userId);
                insAccess.Parameters.AddWithValue("@CompanyId", companyId);
                insAccess.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>E-postadan güvenli kullanıcı adı türetir; boşsa admin_{kod}. Çakışırsa sayısal sonek ekler.</summary>
    private static string ResolveUsername(string? adminEmail, string normalizedCode)
    {
        string basis = (adminEmail ?? "").Trim();
        if (basis.Length == 0)
            basis = $"admin_{normalizedCode.ToLowerInvariant()}";

        var users = new UserService();
        string candidate = basis;
        int suffix = 1;
        while (users.UsernameExists(candidate))
            candidate = $"{basis}{++suffix}";
        return candidate;
    }

    /// <summary>Güçlü rastgele geçici parola (kriptografik RNG; üst/alt/rakam/simge karışık, ~18 karakter).</summary>
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
}
