using Microsoft.Data.SqlClient;
using MindAeonRest.Helpers;

namespace MindAeonRest.Data;

/// <summary>
/// Sistem veritabanını (MindAeonRest) hazırlar: tablolar, audit kolonları ve varsayılan kayıtlar.
/// Tüm adımlar idempotenttir; yeni eklenen tablolar her açılışta otomatik oluşturulur.
/// </summary>
public static class DatabaseInitializer
{
    // CanUseAi: rol İLK KEZ oluşturulurken tohumlanacak AI Hub yetkisi (SeedRoles INSERT'ine gömülür).
    // Yalnız yönetim personası (SysAdmin/Yönetici) açık; diğerleri kapalı. Sonradan admin kapatırsa
    // yeniden açılış rolü tekrar OLUŞTURMAZ (IF NOT EXISTS) → kararı ezmez.
    private static readonly (string Name, string Description, bool CanUseAi)[] DefaultRoles =
    {
        ("SysAdmin", "Sistem yöneticisi - tam yetki", true),
        ("Yönetici", "Şube/işletme yöneticisi — raporlar, AI Hub, yönetim ekranları", true),
        ("Garson",   "Servis personeli - sipariş ve masa yönetimi", false),
        ("Kasiyer",  "Kasa işlemleri - ödeme ve adisyon kapatma", false),
        ("Mutfak",   "Mutfak personeli - sipariş hazırlama", false),
        ("İade",     "İade / geri ödeme yetkisi (kasiyere eklenir)", false),
        ("Z_Rapor",  "Z (gün sonu) raporu alma yetkisi (kasiyere eklenir)", false),
    };

    private const string DefaultUserPassword = "User123!";

    public static void Initialize()
    {
        Schema.EnsureDatabase(AppConfig.DatabaseName);

        using var connection = Database.OpenConnection();
        CreateTables(connection);
        RetrofitAuditColumns(connection);
        EnsureExtendedColumns(connection);
        Schema.EnsureDeleteAuditOnAllTables(connection);
        SeedRoles(connection);
        SeedUsers(connection);
        SeedCurrencies(connection);
        SeedLocations(connection);
        SeedMainUnits(connection);
    }

    /// <summary>Yalnız Lisans tablosu şemasını (terminal-bazlı migration dahil) garanti eder.
    /// API gibi tam Initialize çalıştırmayan süreçler login öncesi bunu çağırır.</summary>
    public static void EnsureLicenseSchema()
    {
        using var connection = Database.OpenConnection();
        LicensesSchema(connection);
    }

    /// <summary>Yalnız 2FA (TOTP) kolonlarını (dbo.Users additive) garanti eder. Web gibi tam Initialize
    /// çalıştırmayan süreçler açılışta çağırır. Additive + idempotent; mevcut girişi/SSO'yu etkilemez.</summary>
    public static void EnsureTwoFactorSchema()
    {
        using var connection = Database.OpenConnection();
        TwoFactorColumns(connection);
    }

    /// <summary>dbo.Users üzerinde 2FA kolonları (secret ŞİFRELİ saklanır; enabled varsayılan 0 → kimse zorla kilitlenmez).</summary>
    private static void TwoFactorColumns(SqlConnection connection)
    {
        Schema.EnsureColumn(connection, "Users", "TwoFactorSecret", "NVARCHAR(200) NULL");
        Schema.EnsureColumn(connection, "Users", "TwoFactorEnabled", "BIT NOT NULL CONSTRAINT DF_Users_TwoFactorEnabled DEFAULT(0)");
    }

    /// <summary>Lisans tablosu: (CompanyCode, HardwareId) terminal-bazlı + eski tek-PK tabloyu göç ettirir.</summary>
    private static void LicensesSchema(SqlConnection connection)
    {
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.Licenses', N'U') IS NULL
CREATE TABLE dbo.Licenses (
    CompanyCode   NVARCHAR(50)  NOT NULL,
    HardwareId    NVARCHAR(80)  NOT NULL CONSTRAINT DF_Licenses_Hw DEFAULT(''),
    LicenseKey    NVARCHAR(MAX) NULL,
    TrialStartUtc DATETIME2     NULL,
    UpdatedUtc    DATETIME2     NOT NULL CONSTRAINT DF_Licenses_Upd DEFAULT(SYSUTCDATETIME()),
    CONSTRAINT PK_Licenses PRIMARY KEY (CompanyCode, HardwareId)
);");
        // ① Kolon yoksa ekle (kendi batch'i — sonraki batch'te PK'da kullanılabilsin).
        Schema.Exec(connection,
            "IF COL_LENGTH('dbo.Licenses','HardwareId') IS NULL " +
            "ALTER TABLE dbo.Licenses ADD HardwareId NVARCHAR(80) NOT NULL CONSTRAINT DF_Licenses_Hw DEFAULT('');");
        // ② PK HardwareId içermiyorsa: eski (tek-CompanyCode) PK'yı kaldır + composite PK ekle. (Ayrı batch → HardwareId artık var.)
        Schema.Exec(connection, @"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    WHERE i.is_primary_key=1 AND i.object_id=OBJECT_ID('dbo.Licenses')
      AND COL_NAME(ic.object_id, ic.column_id)='HardwareId')
BEGIN
    DECLARE @pk SYSNAME = (SELECT kc.name FROM sys.key_constraints kc
                           WHERE kc.parent_object_id=OBJECT_ID('dbo.Licenses') AND kc.type='PK');
    -- EXEC('...' + QUOTENAME(...)) çalışmaz (EXEC string'inde fonksiyon çağrısına izin yok →
    -- 'Incorrect syntax near QUOTENAME'). Önce değişkende kur, sonra sp_executesql ile çalıştır.
    IF @pk IS NOT NULL
    BEGIN
        DECLARE @dropPk NVARCHAR(300) = N'ALTER TABLE dbo.Licenses DROP CONSTRAINT ' + QUOTENAME(@pk);
        EXEC sys.sp_executesql @dropPk;
    END
    ALTER TABLE dbo.Licenses ADD CONSTRAINT PK_Licenses PRIMARY KEY (CompanyCode, HardwareId);
END");
    }

    private static void CreateTables(SqlConnection connection)
    {
        // Roles
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
CREATE TABLE dbo.Roles (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(50)  NOT NULL UNIQUE,
    Description NVARCHAR(200) NULL,
    IsActive    BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // Users
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
CREATE TABLE dbo.Users (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Username     NVARCHAR(50)  NOT NULL UNIQUE,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    FullName     NVARCHAR(100) NOT NULL,
    RoleId       INT           NOT NULL,
    IsActive     BIT           NOT NULL DEFAULT(1),
    LastLoginAt  DATETIME2     NULL,
    MustChangePassword BIT     NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_Users_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id)
);");

        // UserRoles: kullanıcı başına ÇOKLU rol (RoleId = birincil/gösterim rolü; tüm roller burada)
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL
CREATE TABLE dbo.UserRoles (
    UserId INT NOT NULL, RoleId INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id)
);");

        // Currencies (DovizTipleri)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Currencies', N'U') IS NULL
CREATE TABLE dbo.Currencies (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(3)  NOT NULL UNIQUE,
    Name     NVARCHAR(50) NOT NULL,
    Symbol   NVARCHAR(5)  NULL,
    IsActive BIT          NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // CurrencyRates (TCMB döviz kurları - günlük)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CurrencyRates', N'U') IS NULL
CREATE TABLE dbo.CurrencyRates (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    CurrencyCode    NVARCHAR(3)    NOT NULL,
    RateDate        DATE           NOT NULL,
    ForexBuying     DECIMAL(18,6)  NULL,
    ForexSelling    DECIMAL(18,6)  NULL,
    BanknoteBuying  DECIMAL(18,6)  NULL,
    BanknoteSelling DECIMAL(18,6)  NULL,
    Source          NVARCHAR(20)   NOT NULL DEFAULT(N'TCMB'),
    {Schema.AuditColumns},
    CONSTRAINT UQ_CurrencyRates UNIQUE (CurrencyCode, RateDate)
);");

        // MainUnits (Ana Birimler)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.MainUnits', N'U') IS NULL
CREATE TABLE dbo.MainUnits (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(20) NOT NULL UNIQUE,
    Name     NVARCHAR(50) NOT NULL,
    IsActive BIT          NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // Countries (Ülke)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Countries', N'U') IS NULL
CREATE TABLE dbo.Countries (
    Id   INT IDENTITY(1,1) PRIMARY KEY,
    Code NVARCHAR(3)   NULL,
    Name NVARCHAR(100) NOT NULL UNIQUE,
    {Schema.AuditColumns}
);");

        // Cities (Şehir)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Cities', N'U') IS NULL
CREATE TABLE dbo.Cities (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    CountryId INT           NOT NULL,
    Name      NVARCHAR(100) NOT NULL,
    PlateCode NVARCHAR(5)   NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_Cities_Countries FOREIGN KEY (CountryId) REFERENCES dbo.Countries(Id)
);");

        // Districts (İlçe)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Districts', N'U') IS NULL
CREATE TABLE dbo.Districts (
    Id     INT IDENTITY(1,1) PRIMARY KEY,
    CityId INT           NOT NULL,
    Name   NVARCHAR(100) NOT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_Districts_Cities FOREIGN KEY (CityId) REFERENCES dbo.Cities(Id)
);");

        // Companies (Şirket)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL
CREATE TABLE dbo.Companies (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Code         NVARCHAR(50)  NOT NULL UNIQUE,
    Name         NVARCHAR(150) NOT NULL,
    DatabaseName NVARCHAR(150) NOT NULL,
    IsActive     BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // CompanyBranches (Şirket Şube)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CompanyBranches', N'U') IS NULL
CREATE TABLE dbo.CompanyBranches (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId INT           NOT NULL,
    Code      NVARCHAR(50)  NOT NULL,
    Name      NVARCHAR(150) NOT NULL,
    IsActive  BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_Branches_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id),
    CONSTRAINT UQ_Branches_Company_Code UNIQUE (CompanyId, Code)
);");

        // UserCompanyAccess (Kullanıcı şirket/şube erişimi)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.UserCompanyAccess', N'U') IS NULL
CREATE TABLE dbo.UserCompanyAccess (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    UserId    INT NOT NULL,
    CompanyId INT NOT NULL,
    BranchId  INT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_Access_Users     FOREIGN KEY (UserId)    REFERENCES dbo.Users(Id),
    CONSTRAINT FK_Access_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id),
    CONSTRAINT FK_Access_Branches  FOREIGN KEY (BranchId)  REFERENCES dbo.CompanyBranches(Id)
);");

        LicensesSchema(connection);
    }

    /// <summary>Daha önce audit kolonu olmadan oluşturulmuş tablolara kolonları ekler.</summary>
    private static void RetrofitAuditColumns(SqlConnection connection)
    {
        Schema.EnsureAuditColumns(connection, "Roles");
        Schema.EnsureAuditColumns(connection, "Users");
    }

    /// <summary>Şirket ve şubelere adres/lokasyon/vergi/telefon kolonlarını ekler (idempotent).</summary>
    private static void EnsureExtendedColumns(SqlConnection connection)
    {
        foreach (string table in new[] { "Companies", "CompanyBranches" })
        {
            Schema.EnsureColumn(connection, table, "Address", "NVARCHAR(300) NULL");
            Schema.EnsureColumn(connection, table, "CountryId", "INT NULL");
            Schema.EnsureColumn(connection, table, "CityId", "INT NULL");
            Schema.EnsureColumn(connection, table, "DistrictId", "INT NULL");
            Schema.EnsureColumn(connection, table, "TaxOffice", "NVARCHAR(100) NULL");
            Schema.EnsureColumn(connection, table, "TaxNumber", "NVARCHAR(20) NULL");
            Schema.EnsureColumn(connection, table, "Phone", "NVARCHAR(30) NULL");
        }

        // İŞ GÜNÜ KESİMİ (şube bazlı): 0 = gece yarısı (mevcut davranış), geriye uyumlu.
        Schema.EnsureColumn(connection, "CompanyBranches", "DayStartMinutes", "INT NOT NULL DEFAULT(0)");

        // Şirket aktiflik durumu (eski DB'lerde yoksa ekle; idempotent). Pasif şirkete NON-SysAdmin girişi kapalı.
        Schema.EnsureColumn(connection, "Companies", "IsActive", "BIT NOT NULL DEFAULT(1)");

        // Döviz tipi yalnızca şirket düzeyinde
        Schema.EnsureColumn(connection, "Companies", "CurrencyId", "INT NULL");

        // Logolar (şirket bazında, tüm şubelerde aynı): renkli (ekran/login) + mono (termal baskı)
        Schema.EnsureColumn(connection, "Companies", "LogoColor", "VARBINARY(MAX) NULL");
        Schema.EnsureColumn(connection, "Companies", "LogoMono", "VARBINARY(MAX) NULL");

        // Rol bazlı AI Hub yetkisi (varsayılan kapalı; SysAdmin her zaman yetkili, diğerlerini admin açar).
        // Yönetim personasına (SysAdmin + Yönetici) varsayılan AÇIK gelme işi artık SeedRoles'taki TEK-SEFERLİK
        // "IF NOT EXISTS ... INSERT" cümlesine gömülüdür (rol yaratılırken CanUseAi seed edilir). Burada
        // koşulsuz bir UPDATE YOKTUR → admin sonradan kapatırsa yeniden açılışta karar EZİLMEZ.
        Schema.EnsureColumn(connection, "Roles", "CanUseAi", "BIT NOT NULL CONSTRAINT DF_Roles_CanUseAi DEFAULT(0)");

        // İlk girişte parola değiştirme zorunluluğu (varsayılan/seed zayıf parolalar için).
        Schema.EnsureColumn(connection, "Users", "MustChangePassword", "BIT NOT NULL CONSTRAINT DF_Users_MustChangePwd DEFAULT(0)");

        // 2FA (TOTP) — additive; secret ŞİFRELİ saklanır, enabled varsayılan 0 (opt-in).
        TwoFactorColumns(connection);

        // Çoklu rol: mevcut kullanıcıların birincil rolünü UserRoles'a taşı (yalnız eksikse)
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.Id, u.RoleId FROM dbo.Users u
WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId=u.Id AND ur.RoleId=u.RoleId);");
    }

    private static void SeedRoles(SqlConnection connection)
    {
        foreach (var role in DefaultRoles)
        {
            using var command = connection.CreateCommand();
            // TEK-SEFERLİK tohum: rol yalnız İLK KEZ oluşturulurken CanUseAi ile eklenir. IF NOT EXISTS
            // sayesinde sonraki açılışlarda no-op → admin yetkiyi kapatırsa yeniden AÇILMAZ.
            command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = @Name)
    INSERT INTO dbo.Roles (Name, Description, CanUseAi) VALUES (@Name, @Description, @CanUseAi);";
            command.Parameters.AddWithValue("@Name", role.Name);
            command.Parameters.AddWithValue("@Description", role.Description);
            command.Parameters.AddWithValue("@CanUseAi", role.CanUseAi);
            command.ExecuteNonQuery();
        }
    }

    private static void SeedUsers(SqlConnection connection)
    {
        var roleIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var roleCmd = connection.CreateCommand())
        {
            roleCmd.CommandText = "SELECT Id, Name FROM dbo.Roles;";
            using var reader = roleCmd.ExecuteReader();
            while (reader.Read())
                roleIds[reader.GetString(1)] = reader.GetInt32(0);
        }

        // Ana yönetici: admin / Admin123! (SysAdmin personasının TEK kullanıcısı — ayrıca sysadmin1 AÇILMAZ).
        InsertUserIfNotExists(connection, "admin", "Admin123!", "Sistem Yöneticisi", roleIds["SysAdmin"]);

        // Her LOGIN PERSONASI için TEK varsayılan kullanıcı (fazlasını admin açar). Atlananlar:
        //  • SysAdmin → zaten 'admin' var (rol başına 1 → tekrar açma).
        //  • İade / Z_Rapor → login personası DEĞİL, kasiyere EKLENEN izin rolleri → ayrı kullanıcı anlamsız.
        var skipSeedUser = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SysAdmin", "İade", "Z_Rapor" };
        foreach (var role in DefaultRoles)
        {
            if (skipSeedUser.Contains(role.Name)) continue;
            int roleId = roleIds[role.Name];
            string prefix = role.Name.ToLowerInvariant();
            InsertUserIfNotExists(connection, $"{prefix}1", DefaultUserPassword, $"{role.Name} Kullanıcı", roleId);
        }
    }

    private static void InsertUserIfNotExists(SqlConnection connection, string username, string password, string fullName, int roleId)
    {
        using var command = connection.CreateCommand();
        // Seed kullanıcıları SABİT/zayıf varsayılan parola ile kurulur → MustChangePassword=1:
        // ilk girişte parola değişimi ZORUNLU (zayıf varsayılanlar canlıda kalamaz).
        command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = @Username)
    INSERT INTO dbo.Users (Username, PasswordHash, FullName, RoleId, MustChangePassword)
    VALUES (@Username, @PasswordHash, @FullName, @RoleId, 1);

-- çoklu rol: birincil rolü UserRoles'a da yaz (eksikse)
IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NOT NULL
INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.Id, u.RoleId FROM dbo.Users u
WHERE u.Username=@Username AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId=u.Id AND ur.RoleId=u.RoleId);";
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@PasswordHash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("@FullName", fullName);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.ExecuteNonQuery();
    }

    private static void SeedCurrencies(SqlConnection connection)
    {
        (string Code, string Name, string Symbol)[] currencies =
        {
            ("TRY", "Türk Lirası", "₺"),
            ("USD", "Amerikan Doları", "$"),
            ("EUR", "Euro", "€"),
        };

        foreach (var c in currencies)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.Currencies WHERE Code = @Code)
    INSERT INTO dbo.Currencies (Code, Name, Symbol) VALUES (@Code, @Name, @Symbol);";
            command.Parameters.AddWithValue("@Code", c.Code);
            command.Parameters.AddWithValue("@Name", c.Name);
            command.Parameters.AddWithValue("@Symbol", c.Symbol);
            command.ExecuteNonQuery();
        }
    }

    private static void SeedMainUnits(SqlConnection connection)
    {
        (string Code, string Name)[] units =
        {
            ("ADET", "Adet"), ("PORSIYON", "Porsiyon"), ("KG", "Kilogram"), ("GR", "Gram"),
            ("LT", "Litre"), ("ML", "Mililitre"), ("MT", "Metre"), ("CM", "Santimetre"),
            ("PAKET", "Paket"), ("KUTU", "Kutu"), ("KOLI", "Koli"), ("DUZINE", "Düzine"),
            ("TABAK", "Tabak"), ("SISE", "Şişe"), ("BARDAK", "Bardak"), ("DILIM", "Dilim"),
        };
        foreach (var u in units)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.MainUnits WHERE Code=@Code)
    INSERT INTO dbo.MainUnits (Code, Name) VALUES (@Code, @Name);";
            command.Parameters.AddWithValue("@Code", u.Code);
            command.Parameters.AddWithValue("@Name", u.Name);
            command.ExecuteNonQuery();
        }
    }

    private static void SeedLocations(SqlConnection connection)
    {
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = N'Türkiye')
    INSERT INTO dbo.Countries (Code, Name) VALUES (N'TR', N'Türkiye');

DECLARE @CountryId INT = (SELECT TOP 1 Id FROM dbo.Countries WHERE Name = N'Türkiye');

IF NOT EXISTS (SELECT 1 FROM dbo.Cities WHERE Name = N'İstanbul' AND CountryId = @CountryId)
    INSERT INTO dbo.Cities (CountryId, Name, PlateCode) VALUES (@CountryId, N'İstanbul', N'34');

DECLARE @CityId INT = (SELECT TOP 1 Id FROM dbo.Cities WHERE Name = N'İstanbul' AND CountryId = @CountryId);

IF NOT EXISTS (SELECT 1 FROM dbo.Districts WHERE Name = N'Beşiktaş' AND CityId = @CityId)
    INSERT INTO dbo.Districts (CityId, Name) VALUES (@CityId, N'Beşiktaş');");
    }
}
