using Microsoft.Data.SqlClient;
using MindAeonRest.Models;

namespace MindAeonRest.Data;

/// <summary>
/// Şirket veritabanlarını (MindAeonRest_{Code}) oluşturur ve şemasını günceller.
/// EnsureSchema her giriş/oluşturmada çağrılır; yeni eklenen tablolar otomatik oluşur.
/// </summary>
public static class CompanyDatabase
{
    public const string Prefix = "MindAeonRest_";

    /// <summary>Şirket kodundan veritabanı adı üretir. Kod normalize edilmiş olmalıdır.</summary>
    public static string NameFor(string companyCode) => $"{Prefix}{companyCode}";

    /// <summary>Şirket veritabanını yoksa oluşturur ve şemasını hazırlar.</summary>
    public static void Provision(Company company)
    {
        Schema.EnsureDatabase(company.DatabaseName);
        EnsureSchema(company.DatabaseName);
    }

    // Süreç ömrü boyunca DB başına şema garantisi bir kez çalışsın (web/API her istekte çağırabilir).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _ensuredSchemas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>MEVCUT şirket DB'sinin şemasını (Shifts vb. yeni tablolar) süreç başına BİR KEZ garanti eder.
    /// Provision'dan farkı: DB OLUŞTURMAZ (public web'de oto-üretim yok); yalnız var olan DB'yi migrate eder.
    /// WPF login CompanyDatabase.Provision→EnsureSchema çağırırken, web/API bunu login/istek yolunda çağırır.</summary>
    public static void EnsureSchemaOnce(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || _ensuredSchemas.ContainsKey(databaseName)) return;
        try { EnsureSchema(databaseName); _ensuredSchemas[databaseName] = 1; }
        catch { /* geçici hata: işaretleme, sonraki istekte yeniden dene */ }
    }

    /// <summary>Şirket veritabanındaki tabloları yoksa oluşturur (otomatik migration).</summary>
    public static void EnsureSchema(string databaseName)
    {
        using var connection = Database.OpenConnection(databaseName);
        CreateTables(connection);
        EnsureColumns(connection);
        Schema.EnsureDeleteAuditOnAllTables(connection);
        SeedDefaults(connection);
    }

    /// <summary>Şirket DB'sine sabit varsayılan kayıtları ekler (idempotent).</summary>
    private static void SeedDefaults(SqlConnection connection)
    {
        // Sabit perakende müşteri kartı: PA / "Müşterilerimiz" + varsayılan adres
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Code = N'PA')
BEGIN
    INSERT INTO dbo.Customers (Code, Name) VALUES (N'PA', N'Müşterilerimiz');
    DECLARE @cid INT = SCOPE_IDENTITY();
    INSERT INTO dbo.CustomerAddresses (CustomerId, Title, IsDefault) VALUES (@cid, N'Varsayılan Adres', 1);
END");
    }

    /// <summary>Mevcut tablolara sonradan eklenen kolonları yoksa ekler.</summary>
    private static void EnsureColumns(SqlConnection connection)
    {
        Schema.EnsureColumn(connection, "Products", "Description", "NVARCHAR(500) NULL");
        Schema.EnsureColumn(connection, "Products", "MainUnitId", "INT NULL");
        Schema.EnsureColumn(connection, "Products", "SalesTaxGroupId", "INT NULL");
        Schema.EnsureColumn(connection, "Products", "PurchaseTaxGroupId", "INT NULL");
        Schema.EnsureColumn(connection, "Products", "ReturnTaxGroupId", "INT NULL");
        Schema.EnsureColumn(connection, "Products", "TrackingType", "INT NOT NULL CONSTRAINT DF_Products_TrackingType DEFAULT(0)");
        Schema.EnsureColumn(connection, "Products", "IsActive", "BIT NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT(1)");
        // FAZ 1a Maliyet Motoru: ürün-seviyesi ağırlıklı-ortalama alış maliyeti + son alış maliyeti (alış evrağı POST edilince güncellenir).
        Schema.EnsureColumn(connection, "Products", "AvgCost", "DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_AvgCost DEFAULT(0)");
        Schema.EnsureColumn(connection, "Products", "LastCost", "DECIMAL(18,4) NOT NULL CONSTRAINT DF_Products_LastCost DEFAULT(0)");
        // FAZ 4b Stok Zekâsı: ürün-seviyesi minimum stok eşiği (düşük-stok tespiti + oto-sipariş taslağı). 0 = eşik yok/takip edilmez.
        Schema.EnsureColumn(connection, "Products", "MinStock", "DECIMAL(18,3) NOT NULL CONSTRAINT DF_Products_MinStock DEFAULT(0)");
        Schema.EnsureColumn(connection, "Customers", "CustomerType", "INT NOT NULL CONSTRAINT DF_Customers_CustomerType DEFAULT(0)");
        Schema.EnsureColumn(connection, "Customers", "PriceListId", "INT NULL");
        // FAZ 5b Kampanya Otomasyonu: e-posta (e-posta kampanyası) + doğum tarihi (doğum günü segmenti). Additive; boş kalabilir.
        Schema.EnsureColumn(connection, "Customers", "Email", "NVARCHAR(200) NULL");
        Schema.EnsureColumn(connection, "Customers", "BirthDate", "DATE NULL");
        Schema.EnsureColumn(connection, "CodeSeries", "BranchId", "INT NULL");
        Schema.EnsureColumn(connection, "StockMovements", "DocumentId", "INT NULL");
        Schema.EnsureColumn(connection, "StockMovements", "DocumentLineId", "INT NULL");

        // KIOSK kapsamı: ödeme araçlarına Scope (Şube=0/KIOSK=1) + KioskId. (CreateTables SONRASI çalışır → tablolar mevcut)
        foreach (var tbl in new[] { "CashRegisters", "CreditCardPos", "MealVoucherAccounts" })
        {
            Schema.EnsureColumn(connection, tbl, "Scope", "INT NOT NULL CONSTRAINT DF_" + tbl + "_Scope DEFAULT(0)");
            Schema.EnsureColumn(connection, tbl, "KioskId", "INT NULL");
        }
        // V2: Reyon istasyon tipi (0=Mutfak/KDS, 1=Bar/İçecek-Meşrubat) — garson terminalinde teslim ayrımı
        Schema.EnsureColumn(connection, "Reyons", "StationType", "INT NOT NULL CONSTRAINT DF_Reyons_StationType DEFAULT(0)");
        // V2: Masa/sipariş alerji notu (KDS + ödeme ekranında kırmızı ALERJİ rozeti)
        Schema.EnsureColumn(connection, "Orders", "AllergyNote", "NVARCHAR(300) NULL");
        // V2: KDS rozetleri — ürün ACI; sipariş ÖNCELİK + VIP (her iki KDS sürümünde gösterilir)
        Schema.EnsureColumn(connection, "Products", "IsSpicy", "BIT NOT NULL CONSTRAINT DF_Products_IsSpicy DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "Priority", "BIT NOT NULL CONSTRAINT DF_Orders_Priority DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "IsVip", "BIT NOT NULL CONSTRAINT DF_Orders_IsVip DEFAULT(0)");

        // İptal satırlarında sebep + iptal eden/zaman (iptal raporu için)
        Schema.EnsureColumn(connection, "OrderLines", "CancelReason", "NVARCHAR(300) NULL");
        Schema.EnsureColumn(connection, "OrderLines", "CancelledBy", "INT NULL");
        Schema.EnsureColumn(connection, "OrderLines", "CancelledAt", "DATETIME2 NULL");
        Schema.EnsureColumn(connection, "OrderLines", "ReturnedToStock", "BIT NULL");
        // KIOSKNO: terminal kimliği. Kiosks'a No + fiş yazıcısı + tüm hareketlere KioskNo (varsayılan 1; mevcut kayıtlar 1'e backfill).
        Schema.EnsureColumn(connection, "Kiosks", "No", "INT NOT NULL CONSTRAINT DF_Kiosks_No DEFAULT(1)");
        Schema.EnsureColumn(connection, "Kiosks", "ReceiptPrinterName", "NVARCHAR(200) NULL");
        foreach (var tbl in new[] { "Orders", "ClFiches", "CashMovements", "PosMovements", "ZReports" })
            Schema.EnsureColumn(connection, tbl, "KioskNo", "INT NOT NULL CONSTRAINT DF_" + tbl + "_KioskNo DEFAULT(1)");

        // VARDİYA: fişin kesildiği KIOSK vardiyası (geriye uyumlu nullable; eski fişler NULL kalır).
        Schema.EnsureColumn(connection, "ClFiches", "ShiftId", "INT NULL");

        // SADAKAT: harcama/kazanım anındaki RedeemRate'i hareketle sakla → iade GÜNCEL değil harcama anı oranıyla hesaplansın (additive, nullable; eski hareketler NULL → güncel orana düşer).
        Schema.EnsureColumn(connection, "LoyaltyMovements", "RateUsed", "DECIMAL(9,4) NULL");

        // Evrak FişNo benzersizlik backstop'u (mükerrer kayıt varsa atlanır, şema init'i kırılmaz)
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.Documents', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Documents_Fis')
   AND NOT EXISTS (SELECT FisNo FROM dbo.Documents GROUP BY BranchId, DocType, FisNo HAVING COUNT(*) > 1)
   CREATE UNIQUE INDEX UX_Documents_Fis ON dbo.Documents (BranchId, DocType, FisNo);");
    }

    private static void CreateTables(SqlConnection connection)
    {
        // Products (stok kartı)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
CREATE TABLE dbo.Products (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    Code               NVARCHAR(50)  NOT NULL UNIQUE,
    Name               NVARCHAR(200) NOT NULL,
    Description        NVARCHAR(500) NULL,
    MainUnitId         INT           NULL,
    SalesTaxGroupId    INT           NULL,
    PurchaseTaxGroupId INT           NULL,
    ReturnTaxGroupId   INT           NULL,
    TrackingType       INT           NOT NULL DEFAULT(0),
    IsActive           BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // ProductCategories (FK_PCL_Category bundan önce gelmeli — ProductCategoryLinks'ten ÖNCE oluştur)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NULL
CREATE TABLE dbo.ProductCategories (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    Image    VARBINARY(MAX) NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // ProductCategoryLinks (ürün-kategori çoka-çok)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductCategoryLinks', N'U') IS NULL
CREATE TABLE dbo.ProductCategoryLinks (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    ProductId  INT NOT NULL,
    CategoryId INT NOT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_PCL_Product  FOREIGN KEY (ProductId)  REFERENCES dbo.Products(Id),
    CONSTRAINT FK_PCL_Category FOREIGN KEY (CategoryId) REFERENCES dbo.ProductCategories(Id)
);");

        // ProductImages (ürün resimleri; binary, ana resim - kiosk için)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductImages', N'U') IS NULL
CREATE TABLE dbo.ProductImages (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    ProductId INT            NOT NULL,
    Image     VARBINARY(MAX) NOT NULL,
    IsMain    BIT            NOT NULL DEFAULT(0),
    SortOrder INT            NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductImages_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // ProductDescriptions (ürün başına sınırsız hazır açıklama; kiosk/mutfak'ta gösterilir)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductDescriptions', N'U') IS NULL
CREATE TABLE dbo.ProductDescriptions (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    ProductId   INT           NOT NULL,
    Description NVARCHAR(300) NOT NULL,
    SortOrder   INT           NOT NULL DEFAULT(0),
    IsActive    BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductDescriptions_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // ProductUnits (ürün birimleri; çevrim katsayıları ve ölçüler)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductUnits', N'U') IS NULL
CREATE TABLE dbo.ProductUnits (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    ProductId         INT           NOT NULL,
    UnitId            INT           NOT NULL,
    IsMainUnit        BIT           NOT NULL DEFAULT(0),
    ConversionFactor1 DECIMAL(18,4) NOT NULL DEFAULT(1),
    ConversionFactor2 DECIMAL(18,4) NOT NULL DEFAULT(1),
    Width             DECIMAL(18,3) NULL,
    Length            DECIMAL(18,3) NULL,
    Height            DECIMAL(18,3) NULL,
    Weight            DECIMAL(18,3) NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductUnits_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // ProductBranchSettings (ürün+şube: tüketim deposu ve satış durdurma - şube bazlı)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductBranchSettings', N'U') IS NULL
CREATE TABLE dbo.ProductBranchSettings (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    ProductId          INT NOT NULL,
    BranchId           INT NOT NULL,
    ConsumeWarehouseId INT NULL,
    IsSalesStopped     BIT NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_PBS_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT UQ_PBS UNIQUE (ProductId, BranchId)
);");

        // ProductRecipes (menü/reçete bileşenleri: menü de bir Product'tır)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductRecipes', N'U') IS NULL
CREATE TABLE dbo.ProductRecipes (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    MenuProductId      INT           NOT NULL,
    ComponentProductId INT           NOT NULL,
    Quantity           DECIMAL(18,4) NOT NULL DEFAULT(1),
    UnitId             INT           NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_Recipes_Menu      FOREIGN KEY (MenuProductId)      REFERENCES dbo.Products(Id),
    CONSTRAINT FK_Recipes_Component FOREIGN KEY (ComponentProductId) REFERENCES dbo.Products(Id)
);");

        // ProductBarcodes (birim bazlı barkod; parti/seri/renk-beden takipleri ileride eklenecek)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductBarcodes', N'U') IS NULL
CREATE TABLE dbo.ProductBarcodes (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    ProductId INT          NOT NULL,
    UnitId    INT          NOT NULL,
    Barcode   NVARCHAR(50) NOT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductBarcodes_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // Warehouses (Depo) - Code firma bazında unique; BranchId hangi şubenin deposu (sistem DB'sindeki CompanyBranches)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Warehouses', N'U') IS NULL
CREATE TABLE dbo.Warehouses (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    BranchId INT           NOT NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // CustomerGroups (Müşteri Grubu)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CustomerGroups', N'U') IS NULL
CREATE TABLE dbo.CustomerGroups (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // Customers (Cari) - lokasyon id'leri sistem DB'sinde
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Code            NVARCHAR(50)  NOT NULL UNIQUE,
    Name            NVARCHAR(200) NOT NULL,
    CustomerGroupId INT           NULL,
    Address         NVARCHAR(300) NULL,
    CountryId       INT           NULL,
    CityId          INT           NULL,
    DistrictId      INT           NULL,
    TaxOffice       NVARCHAR(100) NULL,
    TaxNumber       NVARCHAR(20)  NULL,
    Phone           NVARCHAR(30)  NULL,
    EInvoiceType    INT           NOT NULL DEFAULT(0),
    IsActive        BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_Customers_Group FOREIGN KEY (CustomerGroupId) REFERENCES dbo.CustomerGroups(Id)
);");

        // CustomerAddresses (müşteri birden fazla adres)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CustomerAddresses', N'U') IS NULL
CREATE TABLE dbo.CustomerAddresses (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT           NOT NULL,
    Title      NVARCHAR(100) NULL,
    Address    NVARCHAR(300) NULL,
    CountryId  INT           NULL,
    CityId     INT           NULL,
    DistrictId INT           NULL,
    Phone      NVARCHAR(30)  NULL,
    IsDefault  BIT           NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_CustomerAddresses_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
);");

        // ===== Finans tanımları =====
        // Banks (Banka) - şirket lookup
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Banks', N'U') IS NULL
CREATE TABLE dbo.Banks (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // BankAccounts (Banka Hesabı) - döviz zorunlu
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.BankAccounts', N'U') IS NULL
CREATE TABLE dbo.BankAccounts (
    Id INT IDENTITY(1,1) PRIMARY KEY, BankId INT NOT NULL, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    CurrencyId INT NOT NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns},
    CONSTRAINT FK_BankAccounts_Bank FOREIGN KEY (BankId) REFERENCES dbo.Banks(Id)
);");

        // CreditCardPos (Kredi Kartı POS) - şube + banka + döviz
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CreditCardPos', N'U') IS NULL
CREATE TABLE dbo.CreditCardPos (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, BankId INT NOT NULL, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    CurrencyId INT NOT NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns},
    CONSTRAINT FK_Pos_Bank FOREIGN KEY (BankId) REFERENCES dbo.Banks(Id)
);");

        // CashRegisters (Nakit Kasa) - şube + döviz
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CashRegisters', N'U') IS NULL
CREATE TABLE dbo.CashRegisters (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    CurrencyId INT NOT NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // MealVouchers (Yemek Çeki firması) - şirket lookup
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.MealVouchers', N'U') IS NULL
CREATE TABLE dbo.MealVouchers (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // MealVoucherAccounts (Yemek Çek Hesabı) - şube + yemek çeki + döviz
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.MealVoucherAccounts', N'U') IS NULL
CREATE TABLE dbo.MealVoucherAccounts (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, MealVoucherId INT NOT NULL, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    CurrencyId INT NOT NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns},
    CONSTRAINT FK_MVA_Voucher FOREIGN KEY (MealVoucherId) REFERENCES dbo.MealVouchers(Id)
);");

        // KIOSK tanımları (şube bazlı self-servis cihaz)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Kiosks', N'U') IS NULL
CREATE TABLE dbo.Kiosks (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");
        // NOT: KIOSK kapsam (Scope/KioskId) + KioskNo + iptal-sebep kolon migration'ları EnsureColumns'a taşındı.
        // Burada (CreateTables içinde) henüz oluşturulmamış OrderLines/ClFiches/ZReports'a ALTER atıyordu →
        // SIFIRDAN yeni şirket DB'sinde "Cannot find the object dbo.OrderLines" hatası veriyordu (mevcut DB'lerde tablo zaten vardı).

        // Zones (Lokasyon) - şube altında
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Zones', N'U') IS NULL
CREATE TABLE dbo.Zones (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    BranchId INT           NOT NULL,
    ReceiptPrinterName NVARCHAR(200) NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, "IF COL_LENGTH('dbo.Zones','ReceiptPrinterName') IS NULL ALTER TABLE dbo.Zones ADD ReceiptPrinterName NVARCHAR(200) NULL;");

        // Reyons (Mutfak/hazırlık birimi) - zone altında, opsiyonel tek yazıcı
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Reyons', N'U') IS NULL
CREATE TABLE dbo.Reyons (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Code        NVARCHAR(50)  NOT NULL UNIQUE,
    Name        NVARCHAR(150) NOT NULL,
    ZoneId      INT           NOT NULL,
    PrinterName NVARCHAR(200) NULL,
    IsActive    BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_Reyons_Zone FOREIGN KEY (ZoneId) REFERENCES dbo.Zones(Id)
);");

        // Tables (Masalar) - zone altında, yerleşim koordinatlarıyla; kod ŞUBE BAZLI unique
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Tables', N'U') IS NULL
CREATE TABLE dbo.Tables (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    ZoneId   INT           NOT NULL,
    BranchId INT           NOT NULL DEFAULT(0),
    Code     NVARCHAR(50)  NOT NULL,
    Name     NVARCHAR(150) NULL,
    Shape    INT           NOT NULL DEFAULT(0),
    Seats    INT           NOT NULL DEFAULT(4),
    X        FLOAT         NOT NULL DEFAULT(0),
    Y        FLOAT         NOT NULL DEFAULT(0),
    Width    FLOAT         NOT NULL DEFAULT(80),
    Height   FLOAT         NOT NULL DEFAULT(80),
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_Tables_Zone FOREIGN KEY (ZoneId) REFERENCES dbo.Zones(Id),
    CONSTRAINT UQ_Tables_BranchCode UNIQUE (BranchId, Code)
);");

        // Tables migration: eski tek-kolon Code unique -> BranchId + Seats + şube bazlı unique
        // (ayrı batch'ler: kolon ekleme ile aynı batch'te o kolona referans verilemez)
        Schema.Exec(connection, "IF COL_LENGTH('dbo.Tables','BranchId') IS NULL ALTER TABLE dbo.Tables ADD BranchId INT NOT NULL DEFAULT(0);");
        Schema.Exec(connection, "IF COL_LENGTH('dbo.Tables','Seats') IS NULL ALTER TABLE dbo.Tables ADD Seats INT NOT NULL DEFAULT(4);");
        Schema.Exec(connection, "UPDATE t SET t.BranchId = z.BranchId FROM dbo.Tables t JOIN dbo.Zones z ON z.Id = t.ZoneId WHERE t.BranchId = 0;");
        Schema.Exec(connection, @"
DECLARE @uq sysname;
SELECT @uq = kc.name FROM sys.key_constraints kc
  JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
  JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
 WHERE kc.parent_object_id = OBJECT_ID('dbo.Tables') AND kc.type = 'UQ' AND c.name = 'Code'
   AND (SELECT COUNT(*) FROM sys.index_columns ic2 WHERE ic2.object_id = kc.parent_object_id AND ic2.index_id = kc.unique_index_id) = 1;
IF @uq IS NOT NULL EXEC('ALTER TABLE dbo.Tables DROP CONSTRAINT ' + @uq);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Tables_BranchCode' AND parent_object_id = OBJECT_ID('dbo.Tables'))
  ALTER TABLE dbo.Tables ADD CONSTRAINT UQ_Tables_BranchCode UNIQUE (BranchId, Code);");

        // ===== Rezervasyon =====
        // ReservationTags (açıklama etiketleri: Doğum Günü, İş Yemeği...) - ekranda anında görünür (renkli)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ReservationTags', N'U') IS NULL
CREATE TABLE dbo.ReservationTags (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    Color NVARCHAR(20) NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // Reservations
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Reservations', N'U') IS NULL
CREATE TABLE dbo.Reservations (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE,
    BranchId INT NOT NULL, ZoneId INT NOT NULL, CustomerId INT NOT NULL, TagId INT NULL,
    Note NVARCHAR(500) NULL, StartTime DATETIME2 NOT NULL, EndTime DATETIME2 NOT NULL,
    PartySize INT NOT NULL DEFAULT(0), Status INT NOT NULL DEFAULT(0),
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns},
    CONSTRAINT FK_Res_Zone FOREIGN KEY (ZoneId) REFERENCES dbo.Zones(Id),
    CONSTRAINT FK_Res_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
    CONSTRAINT FK_Res_Tag FOREIGN KEY (TagId) REFERENCES dbo.ReservationTags(Id)
);");

        // ReservationTables (bir rezervasyon birden fazla masa)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ReservationTables', N'U') IS NULL
CREATE TABLE dbo.ReservationTables (
    Id INT IDENTITY(1,1) PRIMARY KEY, ReservationId INT NOT NULL, TableId INT NOT NULL,
    CONSTRAINT FK_ResTbl_Res FOREIGN KEY (ReservationId) REFERENCES dbo.Reservations(Id),
    CONSTRAINT FK_ResTbl_Table FOREIGN KEY (TableId) REFERENCES dbo.Tables(Id),
    CONSTRAINT UQ_ResTbl UNIQUE (ReservationId, TableId)
);");

        // Waitlist (Bekleme Listesi) — FAZ 5c: online rezervasyonda uygun masa yoksa müşteri buraya alınır.
        // Rezervasyon tablosundan BAĞIMSIZ, kendi tablosu; şube kapsamlı; audit + soft-delete standart.
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Waitlist', N'U') IS NULL
CREATE TABLE dbo.Waitlist (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BranchId INT NOT NULL, Name NVARCHAR(200) NOT NULL, Phone NVARCHAR(40) NULL,
    PartySize INT NOT NULL DEFAULT(0), RequestedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    Status INT NOT NULL DEFAULT(0), Note NVARCHAR(500) NULL, {Schema.AuditColumns}
);");

        // Örnek rezervasyon etiketleri
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM dbo.ReservationTags WHERE Code='DOGUMGUNU') INSERT INTO dbo.ReservationTags (Code,Name,Color) VALUES (N'DOGUMGUNU',N'Doğum Günü',N'#E91E63');
IF NOT EXISTS (SELECT 1 FROM dbo.ReservationTags WHERE Code='ISYEMEGI') INSERT INTO dbo.ReservationTags (Code,Name,Color) VALUES (N'ISYEMEGI',N'İş Yemeği',N'#3F51B5');
IF NOT EXISTS (SELECT 1 FROM dbo.ReservationTags WHERE Code='VIP') INSERT INTO dbo.ReservationTags (Code,Name,Color) VALUES (N'VIP',N'VIP',N'#FF9800');
IF NOT EXISTS (SELECT 1 FROM dbo.ReservationTags WHERE Code='YILDONUMU') INSERT INTO dbo.ReservationTags (Code,Name,Color) VALUES (N'YILDONUMU',N'Yıl Dönümü',N'#9C27B0');");

        // ===== Sipariş / Adisyon =====
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
CREATE TABLE dbo.Orders (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE,
    BranchId INT NOT NULL, ZoneId INT NOT NULL, CustomerId INT NOT NULL,
    Status INT NOT NULL DEFAULT(0), Note NVARCHAR(500) NULL,
    OrderType INT NOT NULL DEFAULT(0), CoverCount INT NOT NULL DEFAULT(0),
    DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT(0), ReservationId INT NULL,
    OpenedAt DATETIME2 NOT NULL, ClosedAt DATETIME2 NULL, {Schema.AuditColumns}
);");
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.OrderTables', N'U') IS NULL
CREATE TABLE dbo.OrderTables (
    Id INT IDENTITY(1,1) PRIMARY KEY, OrderId INT NOT NULL, TableId INT NOT NULL,
    CONSTRAINT FK_OrdTbl_Order FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id),
    CONSTRAINT UQ_OrdTbl UNIQUE (OrderId, TableId)
);");
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.OrderLines', N'U') IS NULL
CREATE TABLE dbo.OrderLines (
    Id INT IDENTITY(1,1) PRIMARY KEY, OrderId INT NOT NULL, ProductId INT NOT NULL,
    ReyonId INT NULL, SeatNo INT NOT NULL DEFAULT(0), ParentLineId INT NULL,
    Qty DECIMAL(18,3) NOT NULL DEFAULT(1), UnitPrice DECIMAL(18,2) NOT NULL DEFAULT(0),
    TaxRate DECIMAL(5,2) NOT NULL DEFAULT(0), LineStatus INT NOT NULL DEFAULT(0), KitchenNote NVARCHAR(500) NULL,
    PrintCount INT NOT NULL DEFAULT(0), LastPrintedAt DATETIME2 NULL,
    IsTreat BIT NOT NULL DEFAULT(0), DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT(0), SettledClFicheId INT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_OrdLine_Order FOREIGN KEY (OrderId) REFERENCES dbo.Orders(Id)
);");

        // Orders/OrderLines migration (mevcut DB'ler için yeni kolonlar)
        Schema.EnsureColumn(connection, "Orders", "OrderType", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "CoverCount", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "DiscountAmount", "DECIMAL(18,2) NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "ReservationId", "INT NULL");
        Schema.EnsureColumn(connection, "OrderLines", "SeatNo", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "OrderLines", "ParentLineId", "INT NULL");
        Schema.EnsureColumn(connection, "OrderLines", "PrintCount", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "OrderLines", "LastPrintedAt", "DATETIME2 NULL");
        Schema.EnsureColumn(connection, "OrderLines", "IsTreat", "BIT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "OrderLines", "DiscountAmount", "DECIMAL(18,2) NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "OrderLines", "SettledClFicheId", "INT NULL");
        Schema.EnsureColumn(connection, "OrderLines", "RefundedQty", "DECIMAL(18,3) NOT NULL DEFAULT(0)");
        // Satır-bazlı kampanya atıfı: bu satıra hangi kampanya (iskonto/ikram) uygulandı → satırda kesin kampanya işareti (elle iskonto/ikramdan ayrılır).
        Schema.EnsureColumn(connection, "OrderLines", "CampaignId", "INT NULL");

        // Couriers (kurye)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Couriers', N'U') IS NULL
CREATE TABLE dbo.Couriers (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    Phone NVARCHAR(30) NULL, IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // Projeler (evraklarda zorunlu)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Projects', N'U') IS NULL
CREATE TABLE dbo.Projects (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // Gider tipleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ExpenseTypes', N'U') IS NULL
CREATE TABLE dbo.ExpenseTypes (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, Name NVARCHAR(150) NOT NULL,
    IsSalary BIT NOT NULL DEFAULT(0), IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // Personel (login kullanıcısından bağımsız)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Personnel', N'U') IS NULL
CREATE TABLE dbo.Personnel (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, FullName NVARCHAR(150) NOT NULL,
    Title NVARCHAR(100) NULL, Department NVARCHAR(100) NULL, NationalId NVARCHAR(20) NULL, Phone NVARCHAR(30) NULL,
    BranchId INT NULL, HireDate DATE NULL, Salary DECIMAL(18,2) NOT NULL DEFAULT(0),
    IsActive BIT NOT NULL DEFAULT(1), {Schema.AuditColumns}
);");

        // Personel izinleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.PersonnelLeaves', N'U') IS NULL
CREATE TABLE dbo.PersonnelLeaves (
    Id INT IDENTITY(1,1) PRIMARY KEY, PersonnelId INT NOT NULL, LeaveType INT NOT NULL DEFAULT(0),
    StartDate DATE NOT NULL, EndDate DATE NOT NULL, Days INT NOT NULL DEFAULT(0), Note NVARCHAR(300) NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_PersLeave_Pers FOREIGN KEY (PersonnelId) REFERENCES dbo.Personnel(Id)
);");

        // Cari hareketler (defter) - faturalar + ödeme/tahsilat üretir
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CariHareketler', N'U') IS NULL
CREATE TABLE dbo.CariHareketler (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, CustomerId INT NOT NULL, TxnDate DATETIME2 NOT NULL,
    DocType INT NOT NULL DEFAULT(0), DocRefType NVARCHAR(30) NULL, DocRefId INT NULL, DocNo NVARCHAR(60) NULL,
    Description NVARCHAR(300) NULL, CurrencyId INT NOT NULL, Debt DECIMAL(18,2) NOT NULL DEFAULT(0), Credit DECIMAL(18,2) NOT NULL DEFAULT(0),
    ProjectId INT NULL, {Schema.AuditColumns}
);");
        Schema.Exec(connection, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CariHareketler_Cust') CREATE INDEX IX_CariHareketler_Cust ON dbo.CariHareketler (CustomerId, TxnDate);");

        // Banka hesabı hareketleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.BankMovements', N'U') IS NULL
CREATE TABLE dbo.BankMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, BankAccountId INT NOT NULL, CurrencyId INT NOT NULL,
    Type INT NOT NULL DEFAULT(0), Amount DECIMAL(18,2) NOT NULL DEFAULT(0), CustomerId INT NULL,
    CounterBankAccountId INT NULL, Note NVARCHAR(300) NULL, MoveDate DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    {Schema.AuditColumns}
);");

        // POS / Kredi kartı hareketleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.PosMovements', N'U') IS NULL
CREATE TABLE dbo.PosMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, PosId INT NOT NULL, CurrencyId INT NOT NULL,
    Type INT NOT NULL DEFAULT(0), Amount DECIMAL(18,2) NOT NULL DEFAULT(0), CustomerId INT NULL,
    CounterPosId INT NULL, Note NVARCHAR(300) NULL, MoveDate DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    {Schema.AuditColumns}
);");

        // Çek / Senet portföyü
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CheckNotes', N'U') IS NULL
CREATE TABLE dbo.CheckNotes (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, Kind INT NOT NULL DEFAULT(0), Direction INT NOT NULL DEFAULT(0),
    CustomerId INT NOT NULL, Serial NVARCHAR(60) NULL, BankName NVARCHAR(150) NULL,
    Amount DECIMAL(18,2) NOT NULL DEFAULT(0), CurrencyId INT NOT NULL,
    IssueDate DATE NOT NULL, DueDate DATE NOT NULL, Status INT NOT NULL DEFAULT(0), Note NVARCHAR(300) NULL,
    CariHareketId INT NULL, {Schema.AuditColumns}
);");

        // Evrak başlığı (tek genel motor: sipariş/irsaliye/fatura/transfer/sayım/fire/gider)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Documents', N'U') IS NULL
CREATE TABLE dbo.Documents (
    Id INT IDENTITY(1,1) PRIMARY KEY, DocType INT NOT NULL, FisNo NVARCHAR(60) NOT NULL, BelgeNo NVARCHAR(60) NULL,
    DocDate DATETIME2 NOT NULL, BelgeTarihi DATE NULL,
    BranchId INT NOT NULL, WarehouseId INT NULL, TargetBranchId INT NULL, TargetWarehouseId INT NULL,
    ProjectId INT NULL, CustomerId INT NULL, AddressId INT NULL, PersonnelId INT NULL, ExpenseTypeId INT NULL,
    CurrencyId INT NOT NULL, UsdRate DECIMAL(18,6) NULL, EurRate DECIMAL(18,6) NULL,
    Status INT NOT NULL DEFAULT(0), Note NVARCHAR(500) NULL,
    SubTotal DECIMAL(18,2) NOT NULL DEFAULT(0), DiscountTotal DECIMAL(18,2) NOT NULL DEFAULT(0),
    TaxTotal DECIMAL(18,2) NOT NULL DEFAULT(0), GrandTotal DECIMAL(18,2) NOT NULL DEFAULT(0),
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Documents_Type') CREATE INDEX IX_Documents_Type ON dbo.Documents (DocType, DocDate);");

        // Evrak satırları (10 iskonto, KDV grubu)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.DocumentLines', N'U') IS NULL
CREATE TABLE dbo.DocumentLines (
    Id INT IDENTITY(1,1) PRIMARY KEY, DocumentId INT NOT NULL, [LineNo] INT NOT NULL,
    ProductId INT NOT NULL, Description NVARCHAR(300) NULL, Qty DECIMAL(18,3) NOT NULL DEFAULT(0),
    UnitId INT NULL, UnitPrice DECIMAL(18,4) NOT NULL DEFAULT(0), LineCurrencyId INT NOT NULL,
    Disc1 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc2 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc3 DECIMAL(9,4) NOT NULL DEFAULT(0),
    Disc4 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc5 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc6 DECIMAL(9,4) NOT NULL DEFAULT(0),
    Disc7 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc8 DECIMAL(9,4) NOT NULL DEFAULT(0), Disc9 DECIMAL(9,4) NOT NULL DEFAULT(0),
    Disc10 DECIMAL(9,4) NOT NULL DEFAULT(0),
    TaxGroupId INT NULL, TaxRate DECIMAL(5,2) NOT NULL DEFAULT(0),
    LineGross DECIMAL(18,2) NOT NULL DEFAULT(0), DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT(0),
    LineNet DECIMAL(18,2) NOT NULL DEFAULT(0), LineTax DECIMAL(18,2) NOT NULL DEFAULT(0), LineTotal DECIMAL(18,2) NOT NULL DEFAULT(0),
    ReceivedQty DECIMAL(18,3) NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_DocLines_Doc FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(Id)
);");

        // AI Hub ayarları (tek satır)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.AiSettings', N'U') IS NULL
CREATE TABLE dbo.AiSettings (
    Id INT NOT NULL PRIMARY KEY DEFAULT(1), Provider INT NOT NULL DEFAULT(0),
    ApiKey NVARCHAR(MAX) NULL, Model NVARCHAR(120) NULL, BaseUrl NVARCHAR(300) NULL,
    Temperature DECIMAL(4,2) NOT NULL DEFAULT(0.2), VoiceOutput BIT NOT NULL DEFAULT(1), VoiceInput BIT NOT NULL DEFAULT(0),
    {Schema.AuditColumns}
);");
        // KRİTİK RETROFIT: AiSettings eski bir AI sürümünde (BaseUrl/Custom yokken) oluşturulmuş olabilir → IF OBJECT_ID
        // IS NULL yeni kolonları eklemez, DeepSeek/Custom (BaseUrl) kaydında "Invalid column name" → kayıt tutulmaz.
        // İdempotent EnsureColumn ile yükseltilen DB'lerdeki kolon drift'i kapatılır.
        Schema.EnsureColumn(connection, "AiSettings", "Provider", "INT NOT NULL CONSTRAINT DF_AiSettings_Provider DEFAULT(0)");
        Schema.EnsureColumn(connection, "AiSettings", "ApiKey", "NVARCHAR(MAX) NULL");
        // ApiKey eski DB'lerde NVARCHAR(300) olarak oluşmuş olabilir; EnsureColumn yalnız EKLER, tip GENİŞLETMEZ.
        // Şifreli anahtar (SecretProtector "v2:"+Base64) uzun bir token'da 300'ü aşabilir → truncation. Açıkça MAX'a genişlet.
        Schema.Exec(connection, @"IF COL_LENGTH('dbo.AiSettings','ApiKey') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.AiSettings') AND name='ApiKey' AND max_length<>-1)
ALTER TABLE dbo.AiSettings ALTER COLUMN ApiKey NVARCHAR(MAX) NULL;");
        Schema.EnsureColumn(connection, "AiSettings", "Model", "NVARCHAR(120) NULL");
        Schema.EnsureColumn(connection, "AiSettings", "BaseUrl", "NVARCHAR(300) NULL");
        Schema.EnsureColumn(connection, "AiSettings", "Temperature", "DECIMAL(4,2) NOT NULL CONSTRAINT DF_AiSettings_Temperature DEFAULT(0.2)");
        Schema.EnsureColumn(connection, "AiSettings", "VoiceOutput", "BIT NOT NULL CONSTRAINT DF_AiSettings_VoiceOutput DEFAULT(1)");
        Schema.EnsureColumn(connection, "AiSettings", "VoiceInput", "BIT NOT NULL CONSTRAINT DF_AiSettings_VoiceInput DEFAULT(0)");
        Schema.EnsureAuditColumns(connection, "AiSettings");

        // Adisyon/Mutfak ekran sürümü (tek satır id=1). Varsayılan=mevcut ekranlar, V2=yeni ekranlar.
        // Mevcut kurulumlar satırsız → Varsayılan'da kalır; yeni kurulumlar Create'te V2'ye set edilir.
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CompanyUiSettings', N'U') IS NULL
CREATE TABLE dbo.CompanyUiSettings (
    Id INT NOT NULL PRIMARY KEY DEFAULT(1),
    AdisyonVersion NVARCHAR(20) NOT NULL DEFAULT('Varsayilan'),
    MutfakVersion NVARCHAR(20) NOT NULL DEFAULT('Varsayilan'),
    {Schema.AuditColumns}
);");

        // V2: Masa servis istekleri (çağrı / masa temizliği / hesap) — garson terminali "Yeni Servis" + masa rozetleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ServiceRequests', N'U') IS NULL
CREATE TABLE dbo.ServiceRequests (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BranchId INT NOT NULL, TableId INT NULL, ZoneId INT NULL, OrderId INT NULL,
    Type INT NOT NULL DEFAULT(0), Status INT NOT NULL DEFAULT(0),
    Note NVARCHAR(300) NULL, KioskNo INT NOT NULL DEFAULT(1), CompletedAt DATETIME2 NULL,
    {Schema.AuditColumns}
);");

        // Giderler (kasa/banka çıkışı)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Expenses', N'U') IS NULL
CREATE TABLE dbo.Expenses (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(60) NOT NULL, BranchId INT NOT NULL,
    ExpenseTypeId INT NOT NULL, PersonnelId INT NULL, ProjectId INT NULL,
    Amount DECIMAL(18,2) NOT NULL DEFAULT(0), CurrencyId INT NOT NULL,
    PayKind INT NOT NULL DEFAULT(0), InstrumentId INT NOT NULL,
    ExpenseDate DATETIME2 NOT NULL, Description NVARCHAR(400) NULL,
    {Schema.AuditColumns}
);");

        // Evrak bağlantıları (Sipariş→İrsaliye→Fatura hiyerarşisi, satır bazlı çok-çoğa)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.DocumentLinks', N'U') IS NULL
CREATE TABLE dbo.DocumentLinks (
    Id INT IDENTITY(1,1) PRIMARY KEY, DocumentId INT NOT NULL, DocumentLineId INT NULL,
    SourceDocumentId INT NOT NULL, SourceLineId INT NULL, Qty DECIMAL(18,3) NOT NULL DEFAULT(0),
    {Schema.AuditColumns}
);");

        // Orders teslimat (paket/take-away) kolonları
        Schema.EnsureColumn(connection, "Orders", "Platform", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "DeliveryStatus", "INT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Orders", "CourierId", "INT NULL");
        Schema.EnsureColumn(connection, "Orders", "DeliveryName", "NVARCHAR(150) NULL");
        Schema.EnsureColumn(connection, "Orders", "DeliveryPhone", "NVARCHAR(30) NULL");
        Schema.EnsureColumn(connection, "Orders", "DeliveryAddress", "NVARCHAR(400) NULL");

        // Belge sayaçları (şube bazlı, eşzamanlı kod üretimi için atomik)
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.DocumentCounters', N'U') IS NULL
CREATE TABLE dbo.DocumentCounters (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, DocType NVARCHAR(20) NOT NULL,
    NextNumber INT NOT NULL DEFAULT(0),
    CONSTRAINT UQ_DocCounter UNIQUE (BranchId, DocType)
);");

        // ===== Stok fişi / hareketleri =====
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.StFiches', N'U') IS NULL
CREATE TABLE dbo.StFiches (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL,
    BranchId INT NOT NULL, OrderId INT NULL, FicheType INT NOT NULL DEFAULT(0), FicheDate DATETIME2 NOT NULL,
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.StockMovements', N'U') IS NULL
CREATE TABLE dbo.StockMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, StFicheId INT NULL, OrderLineId INT NULL,
    ProductId INT NOT NULL, WarehouseId INT NOT NULL, Qty DECIMAL(18,3) NOT NULL,
    MovementType INT NOT NULL DEFAULT(0), BranchId INT NOT NULL, MoveDate DATETIME2 NOT NULL,
    UnitCost DECIMAL(18,4) NULL, {Schema.AuditColumns}
);");

        // ===== Cari fiş / ödeme hareketleri =====
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ClFiches', N'U') IS NULL
CREATE TABLE dbo.ClFiches (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL,
    BranchId INT NOT NULL, OrderId INT NOT NULL, CustomerId INT NOT NULL,
    Total DECIMAL(18,2) NOT NULL DEFAULT(0), Paid DECIMAL(18,2) NOT NULL DEFAULT(0), FicheDate DATETIME2 NOT NULL,
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.PaymentLines', N'U') IS NULL
CREATE TABLE dbo.PaymentLines (
    Id INT IDENTITY(1,1) PRIMARY KEY, ClFicheId INT NOT NULL, Kind INT NOT NULL,
    InstrumentId INT NULL, CurrencyId INT NOT NULL, Amount DECIMAL(18,2) NOT NULL, PayDate DATETIME2 NOT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_PayLine_ClFiche FOREIGN KEY (ClFicheId) REFERENCES dbo.ClFiches(Id)
);");
        // Yemek sitesi / paket platform entegrasyon ayarları — ŞUBE BAZLI
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.DeliveryPlatformSettings', N'U') IS NULL
CREATE TABLE dbo.DeliveryPlatformSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, Platform INT NOT NULL,
    StoreId NVARCHAR(200) NULL, ApiKey NVARCHAR(400) NULL, SecretKey NVARCHAR(400) NULL,
    IsEnabled BIT NOT NULL DEFAULT(0), {Schema.AuditColumns},
    CONSTRAINT UQ_DeliverySettings UNIQUE (BranchId, Platform)
);");
        // FAZ 3b: gelen sipariş (inbound) otomatik paket akışı açık mı — additive kolon.
        Schema.EnsureColumn(connection, "DeliveryPlatformSettings", "IsInboundEnabled", "BIT NOT NULL CONSTRAINT DF_DeliverySettings_Inbound DEFAULT(0)");

        // FAZ 3b: dış sipariş no ↔ yerel Order eşlemesi — idempotency (UQ Platform+ExternalOrderNo) + durum senkron.
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.DeliveryInboundMap', N'U') IS NULL
CREATE TABLE dbo.DeliveryInboundMap (
    Id INT IDENTITY(1,1) PRIMARY KEY, Platform INT NOT NULL, ExternalOrderNo NVARCHAR(100) NOT NULL,
    LocalOrderId INT NOT NULL, Status INT NOT NULL DEFAULT(0), RawJson NVARCHAR(MAX) NULL, {Schema.AuditColumns},
    CONSTRAINT UQ_DeliveryInboundMap UNIQUE (Platform, ExternalOrderNo)
);");
        // FAZ 3b: platform ürün kodu/adı → yerel ProductId eşlemesi (opsiyonel, elle bakım). ŞUBE bağımsız (firma geneli).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.DeliveryProductMap', N'U') IS NULL
CREATE TABLE dbo.DeliveryProductMap (
    Id INT IDENTITY(1,1) PRIMARY KEY, Platform INT NOT NULL, ExternalCode NVARCHAR(100) NOT NULL,
    ProductId INT NOT NULL, {Schema.AuditColumns},
    CONSTRAINT UQ_DeliveryProductMap UNIQUE (Platform, ExternalCode)
);");

        // e-Fatura ayarları (eklenti modül; sağlayıcı EDM/Uyumsoft)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.EInvoiceSettings', N'U') IS NULL
CREATE TABLE dbo.EInvoiceSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY, Provider INT NOT NULL DEFAULT(0),
    Username NVARCHAR(200) NULL, Password NVARCHAR(200) NULL,
    IsTest BIT NOT NULL DEFAULT(1), IsEnabled BIT NOT NULL DEFAULT(0), {Schema.AuditColumns}
);");

        // FAZ 4a: e-Fatura gönderim kaydı (fiş→ETTN eşlemesi + durum + sağlayıcı yanıtı).
        // Yalnız INSERT/UPDATE(status) + okuma; standart audit YOK (NotificationLog muadili, kendi tablosu).
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.EInvoiceLog', N'U') IS NULL
CREATE TABLE dbo.EInvoiceLog (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT NOT NULL, BranchId INT NULL,
    Provider INT NOT NULL DEFAULT(0), IsEArsiv BIT NOT NULL DEFAULT(0), IsTest BIT NOT NULL DEFAULT(0),
    Status INT NOT NULL DEFAULT(0),          -- 0 Bekliyor,1 Gonderildi,2 Iptal,3 Hata
    Ettn NVARCHAR(64) NULL, InvoiceNo NVARCHAR(64) NULL,
    CustomerName NVARCHAR(300) NULL, TaxNumber NVARCHAR(20) NULL,
    GrandTotal DECIMAL(18,2) NOT NULL DEFAULT(0), TaxTotal DECIMAL(18,2) NOT NULL DEFAULT(0),
    Message NVARCHAR(MAX) NULL, ProviderResponse NVARCHAR(MAX) NULL,
    CreatedBy INT NULL, CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    UpdatedBy INT NULL, UpdatedAt DATETIME2 NULL
);");
        // Idempotency + eşzamanlı çift gönderim koruması (BULGU 15): tekil index Status IN (0,1)'i kapsar
        // (0=Gönderiliyor/claim, 1=Gönderildi). Gönderimden önce claim satırı açılır → ikinci eşzamanlı çağrı
        // claim alamaz, provider.Send çağrılmaz. Eski (yalnız Status=1) filtreli index'ten migrasyon: filtre
        // 0'ı içermiyorsa DROP edilir, sonra genişletilmiş filtre ile yeniden oluşturulur.
        Schema.Exec(connection, @"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_EInvoiceLog_OrderSent' AND object_id=OBJECT_ID(N'dbo.EInvoiceLog')
           AND (filter_definition IS NULL OR filter_definition NOT LIKE '%[[]Status]=(0)%'))
    DROP INDEX UX_EInvoiceLog_OrderSent ON dbo.EInvoiceLog;");
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_EInvoiceLog_OrderSent')
   AND NOT EXISTS (SELECT OrderId FROM dbo.EInvoiceLog WHERE Status IN (0,1) GROUP BY OrderId HAVING COUNT(*)>1)
CREATE UNIQUE INDEX UX_EInvoiceLog_OrderSent ON dbo.EInvoiceLog(OrderId) WHERE Status IN (0,1);");

        // FAZ 1b Bildirim altyapısı: ŞUBE+KANAL bazlı ayar (SMS/e-posta/WhatsApp). ApiKey/ApiSecret ŞİFRELİ (SecretProtector).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.NotificationSettings', N'U') IS NULL
CREATE TABLE dbo.NotificationSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, Channel INT NOT NULL, Provider INT NOT NULL DEFAULT(0),
    ApiKey NVARCHAR(400) NULL, ApiSecret NVARCHAR(400) NULL, Sender NVARCHAR(200) NULL,
    Host NVARCHAR(200) NULL, Port INT NULL, UseSsl BIT NOT NULL DEFAULT(1),
    IsEnabled BIT NOT NULL DEFAULT(0), TestMode BIT NOT NULL DEFAULT(1), {Schema.AuditColumns},
    CONSTRAINT UQ_NotificationSettings UNIQUE (BranchId, Channel)
);");
        // NotificationLog: gönderim denemesi kaydı (yalnız INSERT + okuma; standart audit/soft-delete YOK — AuditLogs muadili).
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.NotificationLog', N'U') IS NULL
CREATE TABLE dbo.NotificationLog (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NULL, Channel INT NOT NULL, Provider INT NOT NULL DEFAULT(0),
    Recipient NVARCHAR(200) NULL, TemplateKey NVARCHAR(100) NULL, Body NVARCHAR(MAX) NULL,
    Status INT NOT NULL DEFAULT(0), ProviderResponse NVARCHAR(MAX) NULL, Error NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
);");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.NotificationLog', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_NotificationLog_CreatedAt') CREATE INDEX IX_NotificationLog_CreatedAt ON dbo.NotificationLog (CreatedAt DESC);");

        // FAZ 5d Geri-bildirim / yorum hunisi: müşteri anketi (1-5 yıldız + yorum). KENDİ tablosu (dbo.Feedback).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Feedback', N'U') IS NULL
CREATE TABLE dbo.Feedback (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, OrderId INT NULL, CustomerPhone NVARCHAR(30) NULL,
    Rating INT NOT NULL, Comment NVARCHAR(MAX) NULL, GoogleRedirected BIT NOT NULL DEFAULT(0), {Schema.AuditColumns}
);");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.Feedback', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Feedback_BranchDate') CREATE INDEX IX_Feedback_BranchDate ON dbo.Feedback (BranchId, CreatedAt DESC);");
        // FeedbackSettings (opsiyonel): şube bazlı Google yorum linki + huni açık/kapalı (şube başına tek satır).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.FeedbackSettings', N'U') IS NULL
CREATE TABLE dbo.FeedbackSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, GoogleReviewUrl NVARCHAR(500) NULL,
    IsEnabled BIT NOT NULL DEFAULT(0), {Schema.AuditColumns},
    CONSTRAINT UQ_FeedbackSettings UNIQUE (BranchId)
);");

        // FAZ 5a Sadakat Portalı: müşteri (telefon+OTP) tek-kullanımlık kod tablosu.
        // NOT: CustomerPortalService.EnsureSchema() bunu ayrıca tembel/idempotent kurar (SecretProtector deseni);
        //      burada da GARANTİ ederiz ki portal hiç açılmasa bile fresh şirket DB'sinde tablo mevcut olsun.
        //      Yalnız INSERT/UPDATE + okuma (OTP/log muadili) → standart audit/soft-delete YOK, yalnız CreatedAt.
        //      DDL, servisteki tanımla BİREBİR aynı (idempotent; çift tanım çakışmaz).
        Schema.Exec(connection, @"
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
);");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.CustomerPortalOtp', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CustomerPortalOtp_Phone') CREATE INDEX IX_CustomerPortalOtp_Phone ON dbo.CustomerPortalOtp (Phone, ExpiresUtc);");

        // ZReports: gün sonu (Z) raporu snapshot kaydı
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ZReports', N'U') IS NULL
CREATE TABLE dbo.ZReports (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, ReportDate DATE NOT NULL,
    SalesTotal DECIMAL(18,2) NOT NULL, FisCount INT NOT NULL, TakenAt DATETIME2 NOT NULL, {Schema.AuditColumns}
);");
        // Mükerrer Z koruması: aynı şube+gün için tek Z (yarış-korumalı; yalnız çift kayıt yoksa oluştur)
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_ZReports_BranchDate')
   AND NOT EXISTS (SELECT BranchId, ReportDate FROM dbo.ZReports GROUP BY BranchId, ReportDate HAVING COUNT(*)>1)
CREATE UNIQUE INDEX UX_ZReports_BranchDate ON dbo.ZReports(BranchId, ReportDate);");

        // Shifts: KIOSK (terminal) bazlı vardiya. Bir KIOSK'ta tek AÇIK vardiya (Status=0).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Shifts', N'U') IS NULL
CREATE TABLE dbo.Shifts (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, KioskNo INT NOT NULL,
    OpenedBy INT NULL, OpenedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    ClosedBy INT NULL, ClosedAt DATETIME2 NULL,
    OpeningFloat DECIMAL(18,2) NOT NULL DEFAULT(0), ClosingCounted DECIMAL(18,2) NULL,
    Status INT NOT NULL DEFAULT(0), Note NVARCHAR(500) NULL,
    {Schema.AuditColumns}
);");
        // Bir KIOSK'ta tek açık vardiya: filtered unique index (mevcut çift açık varsa atla, şema init'i kırılmaz).
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Shifts_OpenPerKiosk')
   AND NOT EXISTS (SELECT BranchId, KioskNo FROM dbo.Shifts WHERE Status=0 GROUP BY BranchId, KioskNo HAVING COUNT(*)>1)
CREATE UNIQUE INDEX UX_Shifts_OpenPerKiosk ON dbo.Shifts(BranchId, KioskNo) WHERE Status=0;");

        // CashMovements: nakit kasa hareketleri defteri (tahsilat/çıkış/transfer; dövizli; bakiye = SUM)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CashMovements', N'U') IS NULL
CREATE TABLE dbo.CashMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, CashRegisterId INT NOT NULL, CurrencyId INT NOT NULL,
    Type INT NOT NULL, Amount DECIMAL(18,2) NOT NULL, RefClFicheId INT NULL, CounterCashRegisterId INT NULL,
    Note NVARCHAR(300) NULL, MoveDate DATETIME2 NOT NULL, {Schema.AuditColumns}
);");

        // MealVoucherMovements: yemek çek hesabı hareketleri defteri (adisyon tahsilatı; bakiye = SUM)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.MealVoucherMovements', N'U') IS NULL
CREATE TABLE dbo.MealVoucherMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, MealVoucherAccountId INT NOT NULL, CurrencyId INT NOT NULL,
    Type INT NOT NULL, Amount DECIMAL(18,2) NOT NULL, RefClFicheId INT NULL,
    Note NVARCHAR(300) NULL, MoveDate DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()), {Schema.AuditColumns}
);");

        // GiftCards: hediye çeki/kart (firma içi unique kod, bakiye, durum)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.GiftCards', N'U') IS NULL
CREATE TABLE dbo.GiftCards (
    Id INT IDENTITY(1,1) PRIMARY KEY, Code NVARCHAR(50) NOT NULL UNIQUE, BranchId INT NOT NULL,
    InitialAmount DECIMAL(18,2) NOT NULL DEFAULT(0), Balance DECIMAL(18,2) NOT NULL DEFAULT(0), CurrencyId INT NOT NULL,
    Status INT NOT NULL DEFAULT(0), CustomerId INT NULL, IssuedUtc DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()), ExpiresUtc DATETIME2 NULL,
    Note NVARCHAR(300) NULL, {Schema.AuditColumns}
);");
        // GiftCardMovements: hediye çeki yükleme/kullanım/iptal hareketleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.GiftCardMovements', N'U') IS NULL
CREATE TABLE dbo.GiftCardMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, GiftCardId INT NOT NULL, BranchId INT NOT NULL, Type INT NOT NULL,
    Amount DECIMAL(18,2) NOT NULL, OrderId INT NULL, RefClFicheId INT NULL, Note NVARCHAR(300) NULL,
    MoveDate DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()), {Schema.AuditColumns},
    CONSTRAINT FK_GiftCardMov_Card FOREIGN KEY (GiftCardId) REFERENCES dbo.GiftCards(Id)
);");

        // LoyaltyAccounts: müşteri puan/sadakat bakiyesi (müşteri başına tek)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.LoyaltyAccounts', N'U') IS NULL
CREATE TABLE dbo.LoyaltyAccounts (
    Id INT IDENTITY(1,1) PRIMARY KEY, CustomerId INT NOT NULL UNIQUE, Points DECIMAL(18,2) NOT NULL DEFAULT(0), {Schema.AuditColumns}
);");
        // LoyaltyMovements: puan kazanım/harcama hareketleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.LoyaltyMovements', N'U') IS NULL
CREATE TABLE dbo.LoyaltyMovements (
    Id INT IDENTITY(1,1) PRIMARY KEY, CustomerId INT NOT NULL, Type INT NOT NULL, Points DECIMAL(18,2) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL DEFAULT(0), OrderId INT NULL, RefClFicheId INT NULL, Note NVARCHAR(300) NULL,
    MoveDate DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()), {Schema.AuditColumns}
);");
        // LoyaltySettings: sadakat programı oranları (tek satır, Id=1)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.LoyaltySettings', N'U') IS NULL
CREATE TABLE dbo.LoyaltySettings (
    Id INT NOT NULL PRIMARY KEY DEFAULT(1), IsActive BIT NOT NULL DEFAULT(0),
    EarnRate DECIMAL(9,4) NOT NULL DEFAULT(0), RedeemRate DECIMAL(9,4) NOT NULL DEFAULT(1), {Schema.AuditColumns}
);
IF NOT EXISTS (SELECT 1 FROM dbo.LoyaltySettings WHERE Id=1)
    INSERT INTO dbo.LoyaltySettings (Id, IsActive, EarnRate, RedeemRate) VALUES (1, 0, 0, 1);");

        // ClFicheLines: fiş KDV kırılımı (oran bazında matrah/KDV/tutar)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ClFicheLines', N'U') IS NULL
CREATE TABLE dbo.ClFicheLines (
    Id INT IDENTITY(1,1) PRIMARY KEY, ClFicheId INT NOT NULL, TaxRate DECIMAL(5,2) NOT NULL,
    Matrah DECIMAL(18,2) NOT NULL, TaxAmount DECIMAL(18,2) NOT NULL, LineTotal DECIMAL(18,2) NOT NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_ClLine_ClFiche FOREIGN KEY (ClFicheId) REFERENCES dbo.ClFiches(Id)
);");

        // ===== Ürün → Reyon yönlendirme kuralları =====
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ReyonRoutes', N'U') IS NULL
CREATE TABLE dbo.ReyonRoutes (
    Id INT IDENTITY(1,1) PRIMARY KEY, SourceZoneId INT NULL, ProductId INT NULL, CategoryId INT NULL,
    ReyonId INT NOT NULL, {Schema.AuditColumns},
    CONSTRAINT FK_Route_Reyon FOREIGN KEY (ReyonId) REFERENCES dbo.Reyons(Id)
);");

        // CodeSeries (Stok/Müşteri kodu için otomatik artan seri/patern)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CodeSeries', N'U') IS NULL
CREATE TABLE dbo.CodeSeries (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    EntityType INT          NOT NULL UNIQUE,
    Prefix     NVARCHAR(20) NULL,
    Padding    INT          NOT NULL DEFAULT(6),
    NextNumber INT          NOT NULL DEFAULT(1),
    IsActive   BIT          NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // PriceLists (Fiyat Listeleri)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.PriceLists', N'U') IS NULL
CREATE TABLE dbo.PriceLists (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // Kampanyalar (gelişmiş: tip/değer + hedef + min tutar + X al Y öde + tarih/gün/saat aralığı + şube bazlı + aktif)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Campaigns', N'U') IS NULL
CREATE TABLE dbo.Campaigns (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Code NVARCHAR(50) NOT NULL UNIQUE,
    Name NVARCHAR(150) NOT NULL,
    Description NVARCHAR(400) NULL,
    Type INT NOT NULL DEFAULT(0),
    Value DECIMAL(18,2) NOT NULL DEFAULT(0),
    TargetCategoryId INT NULL,
    TargetProductId INT NULL,
    MinAmount DECIMAL(18,2) NOT NULL DEFAULT(0),
    BuyQty DECIMAL(18,3) NOT NULL DEFAULT(0),
    PayQty DECIMAL(18,3) NOT NULL DEFAULT(0),
    StartDateTime DATETIME2 NOT NULL,
    EndDateTime DATETIME2 NOT NULL,
    DaysOfWeek INT NOT NULL DEFAULT(0),
    DailyStartMin INT NULL,
    DailyEndMin INT NULL,
    AllBranches BIT NOT NULL DEFAULT(1),
    IsActive BIT NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CampaignBranches', N'U') IS NULL
CREATE TABLE dbo.CampaignBranches (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignId INT NOT NULL,
    BranchId INT NOT NULL,
    {Schema.AuditColumns}
);");
        // Hediye kampanyası koşulları (ürün-miktar) ve verilecek ikram ürünleri
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CampaignConditions', N'U') IS NULL
CREATE TABLE dbo.CampaignConditions (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignId INT NOT NULL,
    ProductId INT NOT NULL,
    MinQty DECIMAL(18,3) NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.CampaignGifts', N'U') IS NULL
CREATE TABLE dbo.CampaignGifts (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignId INT NOT NULL,
    ProductId INT NOT NULL,
    Qty DECIMAL(18,3) NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");
        // FAZ 5b Kampanya Otomasyonu: toplu gönderim izi (yalnız INSERT + okuma; NotificationLog muadili, standart audit YOK).
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.CampaignSendLog', N'U') IS NULL
CREATE TABLE dbo.CampaignSendLog (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CampaignId INT NULL, BranchId INT NULL, Segment INT NOT NULL DEFAULT(0), Channel INT NOT NULL,
    CustomerId INT NULL, Recipient NVARCHAR(200) NULL, Status INT NOT NULL DEFAULT(0), Error NVARCHAR(MAX) NULL,
    SentAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME())
);");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.CampaignSendLog', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CampaignSendLog_SentAt') CREATE INDEX IX_CampaignSendLog_SentAt ON dbo.CampaignSendLog (SentAt DESC);");

        // Adisyona uygulanan kampanya (geri alma + gösterim)
        Schema.EnsureColumn(connection, "Orders", "AppliedCampaignId", "INT NULL");

        // ONARIM: tam tahsil edilmiş ama (eski hata yüzünden) açık kalmış adisyonları kapat (masa kırmızı kalmasın)
        Schema.Exec(connection, @"
UPDATE dbo.Orders SET Status=1, ClosedAt=SYSUTCDATETIME()
WHERE Status=0 AND IsDeleted=0
  AND EXISTS (SELECT 1 FROM dbo.OrderLines l WHERE l.OrderId=dbo.Orders.Id AND l.SettledClFicheId IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM dbo.OrderLines l WHERE l.OrderId=dbo.Orders.Id AND l.IsDeleted=0 AND l.LineStatus<>4 AND l.ParentLineId IS NULL AND l.IsTreat=0 AND l.SettledClFicheId IS NULL);");
        Schema.EnsureColumn(connection, "Campaigns", "Stackable", "BIT NOT NULL DEFAULT(0)");
        Schema.EnsureColumn(connection, "Campaigns", "IsMandatory", "BIT NOT NULL DEFAULT(0)");
        // Bir adisyona uygulanan kampanyalar (çoklu/birleştirilebilir kampanyalar için)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.OrderCampaigns', N'U') IS NULL
CREATE TABLE dbo.OrderCampaigns (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT NOT NULL,
    CampaignId INT NOT NULL,
    {Schema.AuditColumns}
);");

        // ProductPrices (şube + döviz + tarih/SAAT aralıklı; müşteri/grup/fiyat listesi opsiyonel)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductPrices', N'U') IS NULL
CREATE TABLE dbo.ProductPrices (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    ProductId       INT           NOT NULL,
    UnitId          INT           NOT NULL,
    PriceType       INT           NOT NULL,
    Price           DECIMAL(18,4) NOT NULL,
    CurrencyId      INT           NULL,
    BranchId        INT           NULL,
    StartDateTime   DATETIME2     NULL,
    EndDateTime     DATETIME2     NULL,
    CustomerId      INT           NULL,
    CustomerGroupId INT           NULL,
    PriceListId     INT           NULL,
    IsActive        BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductPrices_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // ProductOptions (ürün varyant/seçenekleri: GroupName ile gruplanır — Boyut: Küçük/Orta/Büyük, Sıcaklık: Sıcak/Soğuk…)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.ProductOptions', N'U') IS NULL
CREATE TABLE dbo.ProductOptions (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    ProductId  INT           NOT NULL,
    GroupName  NVARCHAR(60)  NOT NULL,
    Name       NVARCHAR(60)  NOT NULL,
    PriceDelta DECIMAL(18,2) NOT NULL DEFAULT(0),
    IsDefault  BIT           NOT NULL DEFAULT(0),
    SortOrder  INT           NOT NULL DEFAULT(0),
    {Schema.AuditColumns},
    CONSTRAINT FK_ProductOptions_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id)
);");

        // ProductPrices ek alanlar: Liste Fiyatı + KDV Dahil bayrağı
        Schema.EnsureColumn(connection, "ProductPrices", "ListPrice", "DECIMAL(18,4) NULL");
        Schema.EnsureColumn(connection, "ProductPrices", "IsTaxIncluded", "BIT NOT NULL CONSTRAINT DF_ProductPrices_TaxIncl DEFAULT(1)");

        // TaxGroups (Vergi/KDV grupları)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.TaxGroups', N'U') IS NULL
CREATE TABLE dbo.TaxGroups (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Code     NVARCHAR(50)  NOT NULL UNIQUE,
    Name     NVARCHAR(150) NOT NULL,
    IsActive BIT           NOT NULL DEFAULT(1),
    {Schema.AuditColumns}
);");

        // TaxGroupRates (tarih aralıklı KDV oranları; VatType: 1 Alış, 2 Satış, 3 İade)
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.TaxGroupRates', N'U') IS NULL
CREATE TABLE dbo.TaxGroupRates (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    TaxGroupId INT           NOT NULL,
    VatType    INT           NOT NULL,
    Rate       DECIMAL(5,2)  NOT NULL,
    StartDate  DATE          NOT NULL,
    EndDate    DATE          NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_TaxGroupRates_Group FOREIGN KEY (TaxGroupId) REFERENCES dbo.TaxGroups(Id)
);");

        // ===== FAZ 4c Personel — Puantaj / Mesai (time-clock) =====
        // Attendance: personel mesai giriş/çıkış (time-clock). Shift (kasa vardiyası) ile KARIŞTIRILMAZ.
        // Bir personelin aynı anda tek AÇIK (ClockOut NULL) kaydı olur (uygulama + filtered unique index).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.Attendance', N'U') IS NULL
CREATE TABLE dbo.Attendance (
    Id INT IDENTITY(1,1) PRIMARY KEY, PersonnelId INT NOT NULL, BranchId INT NOT NULL,
    ClockIn DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()), ClockOut DATETIME2 NULL,
    Note NVARCHAR(300) NULL, KioskNo INT NOT NULL DEFAULT(1),
    {Schema.AuditColumns},
    CONSTRAINT FK_Attendance_Pers FOREIGN KEY (PersonnelId) REFERENCES dbo.Personnel(Id)
);");
        // Bir personelde tek açık mesai (ClockOut NULL): filtered unique index (mevcut çift açık varsa atla, init kırılmaz).
        Schema.Exec(connection, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Attendance_OpenPerPersonnel')
   AND NOT EXISTS (SELECT PersonnelId FROM dbo.Attendance WHERE ClockOut IS NULL AND IsDeleted=0 GROUP BY PersonnelId HAVING COUNT(*)>1)
CREATE UNIQUE INDEX UX_Attendance_OpenPerPersonnel ON dbo.Attendance(PersonnelId) WHERE ClockOut IS NULL AND IsDeleted=0;");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.Attendance', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Attendance_BranchDate') CREATE INDEX IX_Attendance_BranchDate ON dbo.Attendance (BranchId, ClockIn);");

        // WorkSchedule: (opsiyonel) vardiya planı — personel bazlı gün/başlangıç-bitiş saatleri.
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.WorkSchedule', N'U') IS NULL
CREATE TABLE dbo.WorkSchedule (
    Id INT IDENTITY(1,1) PRIMARY KEY, PersonnelId INT NOT NULL, [Date] DATE NOT NULL,
    StartTime TIME NULL, EndTime TIME NULL, Note NVARCHAR(300) NULL,
    {Schema.AuditColumns},
    CONSTRAINT FK_WorkSchedule_Pers FOREIGN KEY (PersonnelId) REFERENCES dbo.Personnel(Id)
);");

        // TipPool: (opsiyonel) bahşiş havuzu — şube+gün toplam tutar + dağıtım (JSON: personelId→pay).
        Schema.Exec(connection, $@"
IF OBJECT_ID(N'dbo.TipPool', N'U') IS NULL
CREATE TABLE dbo.TipPool (
    Id INT IDENTITY(1,1) PRIMARY KEY, BranchId INT NOT NULL, [Date] DATE NOT NULL,
    Amount DECIMAL(18,2) NOT NULL DEFAULT(0), Distribution NVARCHAR(MAX) NULL,
    {Schema.AuditColumns}
);");

        // ===== Denetim İzi (Audit Log) =====
        // Kim-neyi-ne-zaman-değiştirdi GEÇMİŞİ (before/after JSON). Lisans tarafındaki AuditLog + EF interceptor
        // deseninin EF'siz (ham SQL) karşılığı. AuditService.Write merkezi yazıcısı bu tabloya kayıt atar.
        // NOT: Bu tablonun kendisi denetim kaydıdır → standart audit kolonları (CreatedBy/Updated/IsDeleted) YOK,
        //      yalnız CreatedAt tutulur; asla soft-delete/güncelleme uygulanmaz (yalnız INSERT + okuma).
        Schema.Exec(connection, @"
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
CREATE TABLE dbo.AuditLogs (
    Id         BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT           NULL,
    UserName   NVARCHAR(150) NULL,
    Action     NVARCHAR(40)  NOT NULL,        -- Insert / Update / Delete
    EntityType NVARCHAR(150) NOT NULL,
    EntityId   INT           NULL,
    BeforeJson NVARCHAR(MAX) NULL,
    AfterJson  NVARCHAR(MAX) NULL,
    KioskNo    INT           NOT NULL DEFAULT(1),
    BranchId   INT           NULL,
    Ip         NVARCHAR(64)  NULL,
    CreatedAt  DATETIME2     NOT NULL DEFAULT(SYSUTCDATETIME())
);");
        // Filtre indeksleri (tarih / entity / kullanıcı bazlı listeleme için)
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AuditLogs_CreatedAt') CREATE INDEX IX_AuditLogs_CreatedAt ON dbo.AuditLogs (CreatedAt DESC);");
        Schema.Exec(connection, "IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_AuditLogs_Entity') CREATE INDEX IX_AuditLogs_Entity ON dbo.AuditLogs (EntityType, EntityId);");
    }
}
