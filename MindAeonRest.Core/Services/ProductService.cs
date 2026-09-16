using System.Data;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Stok kartları (aktif şirket DB'sinde). Ana birim sistem DB'sinden cross-db join ile.</summary>
public class ProductService
{
    private static string SelectBase => $@"
SELECT p.Id, p.Code, p.Name, p.Description, p.MainUnitId,
       p.SalesTaxGroupId, p.PurchaseTaxGroupId, p.ReturnTaxGroupId, p.TrackingType, p.IsActive,
       mu.Name AS MainUnitName, st.Name AS SalesTaxName, p.IsDeleted,
       (SELECT STRING_AGG(pc.Name, N', ') FROM dbo.ProductCategoryLinks pcl
        JOIN dbo.ProductCategories pc ON pc.Id = pcl.CategoryId
        WHERE pcl.ProductId = p.Id AND pcl.IsDeleted = 0 AND pc.IsDeleted = 0) AS CategoryNames,
       p.IsSpicy,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.ProductImages pi WHERE pi.ProductId = p.Id AND pi.IsDeleted = 0 AND DATALENGTH(pi.Image) > 0) THEN 1 ELSE 0 END AS HasImage
FROM dbo.Products p
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = p.MainUnitId
LEFT JOIN dbo.TaxGroups st ON st.Id = p.SalesTaxGroupId";

    public List<Product> GetAll(bool includeDeleted = false)
    {
        var list = new List<Product>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc = 1 OR p.IsDeleted = 0) ORDER BY p.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Product? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        Product? product;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SelectBase + " WHERE p.Id = @Id AND p.IsDeleted = 0;";
            command.Parameters.AddWithValue("@Id", id);
            using var reader = command.ExecuteReader();
            product = reader.Read() ? Map(reader) : null;
        }
        if (product is null) return null;

        using (var cat = connection.CreateCommand())
        {
            cat.CommandText = "SELECT CategoryId FROM dbo.ProductCategoryLinks WHERE ProductId=@p AND IsDeleted=0;";
            cat.Parameters.AddWithValue("@p", id);
            using var reader = cat.ExecuteReader();
            while (reader.Read()) product.CategoryIds.Add(reader.GetInt32(0));
        }
        return product;
    }

    public bool CodeExists(string code, int excludeId = 0)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Products WHERE Code=@c AND Id<>@ex;";
        command.Parameters.AddWithValue("@c", code);
        command.Parameters.AddWithValue("@ex", excludeId);
        return (int)command.ExecuteScalar() > 0;
    }

    public List<ProductImage> GetImages(int productId)
    {
        var list = new List<ProductImage>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProductId, Image, IsMain, SortOrder FROM dbo.ProductImages WHERE ProductId=@p AND IsDeleted=0 ORDER BY SortOrder, Id;";
        command.Parameters.AddWithValue("@p", productId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductImage
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                Image = (byte[])reader[2],
                IsMain = reader.GetBoolean(3),
                SortOrder = reader.GetInt32(4)
            });
        return list;
    }

    /// <summary>Ürünün ana resmi (yoksa ilk resim) - kiosk için.</summary>
    /// <summary>Tüm ürünlerin GENEL satış fiyatı (şube/müşteri/grup/liste YOK): productId → ProductPrice.</summary>
    public Dictionary<int, ProductPrice> GetGeneralSalePrices() => GetGeneralPrices(PriceType.Satis);

    /// <summary>Tüm ürünlerin GENEL fiyatı (satış/alış; şube/müşteri/grup/liste YOK): productId → ProductPrice.</summary>
    public Dictionary<int, ProductPrice> GetGeneralPrices(PriceType type)
    {
        var map = new Dictionary<int, ProductPrice>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT p.Id, p.ProductId, p.UnitId, p.Price, p.CurrencyId, p.IsTaxIncluded
FROM dbo.ProductPrices p
WHERE p.IsDeleted=0 AND p.PriceType=@type
  AND p.BranchId IS NULL AND p.CustomerId IS NULL AND p.CustomerGroupId IS NULL AND p.PriceListId IS NULL
ORDER BY p.Id;";
        command.Parameters.AddWithValue("@type", (int)type);
        using var r = command.ExecuteReader();
        while (r.Read())
            map[r.GetInt32(1)] = new ProductPrice   // aynı ürün için sonuncusu (en güncel Id) kalır
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1), UnitId = r.GetInt32(2),
                PriceType = type, Price = r.GetDecimal(3),
                CurrencyId = r.IsDBNull(4) ? null : r.GetInt32(4), IsTaxIncluded = r.GetBoolean(5)
            };
        return map;
    }

    /// <summary>Tüm şube/fiyat-listesi bazlı fiyatlar (müşteri/grup hariç) — fiyat matrisi ekranı için.</summary>
    public List<ProductPrice> GetScopedPrices()
    {
        var list = new List<ProductPrice>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT pp.Id, pp.ProductId, pr.Code, pr.Name, pp.UnitId, mu.Name, pp.PriceType, pp.Price, pp.IsTaxIncluded,
       pp.BranchId, br.Name, pp.PriceListId, pl.Name, pp.CurrencyId, cur.Code, pp.StartDateTime, pp.EndDateTime
FROM dbo.ProductPrices pp
JOIN dbo.Products pr ON pr.Id = pp.ProductId AND pr.IsDeleted = 0
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = pp.UnitId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches br ON br.Id = pp.BranchId
LEFT JOIN dbo.PriceLists pl ON pl.Id = pp.PriceListId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = pp.CurrencyId
WHERE pp.IsDeleted = 0 AND pp.CustomerId IS NULL AND pp.CustomerGroupId IS NULL
ORDER BY pr.Name, pp.PriceType, br.Name, pl.Name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProductPrice
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1), ProductCode = r.GetString(2), ProductName = r.GetString(3),
                UnitId = r.GetInt32(4), UnitName = r.IsDBNull(5) ? null : r.GetString(5),
                PriceType = (PriceType)r.GetInt32(6), Price = r.GetDecimal(7), IsTaxIncluded = r.GetBoolean(8),
                BranchId = r.IsDBNull(9) ? null : r.GetInt32(9), BranchName = r.IsDBNull(10) ? null : r.GetString(10),
                PriceListId = r.IsDBNull(11) ? null : r.GetInt32(11), PriceListName = r.IsDBNull(12) ? null : r.GetString(12),
                CurrencyId = r.IsDBNull(13) ? null : r.GetInt32(13), CurrencyCode = r.IsDBNull(14) ? null : r.GetString(14),
                StartDateTime = r.IsDBNull(15) ? null : r.GetDateTime(15), EndDateTime = r.IsDBNull(16) ? null : r.GetDateTime(16)
            });
        return list;
    }

    /// <summary>Matris satır-içi düzenleme: tek fiyat satırının fiyat + KDV dahil bilgisini günceller.</summary>
    public void UpdatePriceById(int priceId, decimal price, bool taxIncluded, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ProductPrices SET Price=@pr, IsTaxIncluded=@t, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@pr", price);
        cmd.Parameters.AddWithValue("@t", taxIncluded);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", priceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Matrise tek bir (şube/liste kapsamlı) fiyat satırı ekler.</summary>
    public void AddScopedPrice(int productId, ProductPrice p, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.ProductPrices (ProductId, UnitId, PriceType, Price, ListPrice, IsTaxIncluded, CurrencyId, BranchId, StartDateTime, EndDateTime, CustomerId, CustomerGroupId, PriceListId, IsActive, CreatedBy)
VALUES (@p,@u,@type,@pr,@lp,@t,@c,@b,@s,@e,NULL,NULL,@pl,1,@by);";
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@u", p.UnitId);
        cmd.Parameters.AddWithValue("@type", (int)p.PriceType);
        cmd.Parameters.AddWithValue("@pr", p.Price);
        cmd.Parameters.AddWithValue("@lp", (object?)p.ListPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", p.IsTaxIncluded);
        cmd.Parameters.AddWithValue("@c", (object?)p.CurrencyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", (object?)p.BranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", (object?)p.StartDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", (object?)p.EndDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pl", (object?)p.PriceListId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Matristen fiyat satırı siler (soft-delete).</summary>
    public void DeletePriceById(int priceId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ProductPrices SET IsDeleted=1, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", priceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Bir ürünün GENEL satış fiyatını ekler/günceller (varsa güncelle, yoksa oluştur). Toplu fiyat ekranı için.</summary>
    public void UpsertGeneralSalePrice(int productId, int unitId, decimal price, bool taxIncluded, int currencyId, int? userId)
        => UpsertGeneralPrice(productId, unitId, PriceType.Satis, price, taxIncluded, currencyId, userId);

    /// <summary>Bir ürünün GENEL fiyatını (satış/alış) ekler/günceller (varsa güncelle, yoksa oluştur).</summary>
    public void UpsertGeneralPrice(int productId, int unitId, PriceType type, decimal price, bool taxIncluded, int currencyId, int? userId)
    {
        using var connection = Database.OpenCompany();
        int existingId;
        using (var find = connection.CreateCommand())
        {
            find.CommandText = @"
SELECT TOP 1 Id FROM dbo.ProductPrices
WHERE ProductId=@p AND PriceType=@type AND IsDeleted=0
  AND BranchId IS NULL AND CustomerId IS NULL AND CustomerGroupId IS NULL AND PriceListId IS NULL
ORDER BY Id DESC;";
            find.Parameters.AddWithValue("@p", productId);
            find.Parameters.AddWithValue("@type", (int)type);
            var o = find.ExecuteScalar();
            existingId = o is null || o == DBNull.Value ? 0 : Convert.ToInt32(o);
        }
        using var cmd = connection.CreateCommand();
        if (existingId > 0)
        {
            cmd.CommandText = "UPDATE dbo.ProductPrices SET Price=@pr, IsTaxIncluded=@t, UnitId=@u, CurrencyId=@c, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", existingId);
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO dbo.ProductPrices (ProductId, UnitId, PriceType, Price, ListPrice, IsTaxIncluded, CurrencyId, BranchId, StartDateTime, EndDateTime, CustomerId, CustomerGroupId, PriceListId, IsActive, CreatedBy)
VALUES (@p, @u, @type, @pr, NULL, @t, @c, NULL, NULL, NULL, NULL, NULL, NULL, 1, @by);";
            cmd.Parameters.AddWithValue("@p", productId);
            cmd.Parameters.AddWithValue("@type", (int)type);
        }
        cmd.Parameters.AddWithValue("@pr", price);
        cmd.Parameters.AddWithValue("@t", taxIncluded);
        cmd.Parameters.AddWithValue("@u", unitId);
        cmd.Parameters.AddWithValue("@c", currencyId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public byte[]? GetMainImage(int productId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1 Image FROM dbo.ProductImages WHERE ProductId=@p AND IsDeleted=0
ORDER BY CASE WHEN IsMain=1 THEN 0 ELSE 1 END, SortOrder, Id;";
        command.Parameters.AddWithValue("@p", productId);
        return command.ExecuteScalar() is byte[] b ? b : null;
    }

    public List<ProductDescription> GetDescriptions(int productId)
    {
        var list = new List<ProductDescription>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProductId, Description, SortOrder, IsActive FROM dbo.ProductDescriptions WHERE ProductId=@p AND IsDeleted=0 ORDER BY SortOrder, Id;";
        command.Parameters.AddWithValue("@p", productId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductDescription
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                Description = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4)
            });
        return list;
    }

    public List<ProductUnit> GetUnits(int productId)
    {
        var list = new List<ProductUnit>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT u.Id, u.ProductId, u.UnitId, mu.Name, u.IsMainUnit, u.ConversionFactor1, u.ConversionFactor2, u.Width, u.Length, u.Height, u.Weight
FROM dbo.ProductUnits u
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = u.UnitId
WHERE u.ProductId=@p AND u.IsDeleted=0 ORDER BY u.IsMainUnit DESC, u.Id;";
        command.Parameters.AddWithValue("@p", productId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductUnit
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                UnitId = reader.GetInt32(2),
                UnitName = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsMainUnit = reader.GetBoolean(4),
                ConversionFactor1 = reader.GetDecimal(5),
                ConversionFactor2 = reader.GetDecimal(6),
                Width = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                Length = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                Height = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                Weight = reader.IsDBNull(10) ? null : reader.GetDecimal(10)
            });
        return list;
    }

    public List<ProductBarcode> GetBarcodes(int productId)
    {
        var list = new List<ProductBarcode>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT b.Id, b.ProductId, b.UnitId, mu.Name, b.Barcode
FROM dbo.ProductBarcodes b
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = b.UnitId
WHERE b.ProductId=@p AND b.IsDeleted=0 ORDER BY b.Id;";
        command.Parameters.AddWithValue("@p", productId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductBarcode
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                UnitId = reader.GetInt32(2),
                UnitName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Barcode = reader.GetString(4)
            });
        return list;
    }

    /// <summary>FAZ 4b mobil sayım: okutulan değeri (barkod) veya ürün kodunu ürüne çözer.
    /// Önce aktif barkod eşleşmesi, yoksa ürün kodu. Bulunamazsa null.</summary>
    public (int Id, string Code, string Name)? ResolveByBarcodeOrCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1 p.Id, p.Code, p.Name
FROM dbo.Products p
LEFT JOIN dbo.ProductBarcodes b ON b.ProductId=p.Id AND b.IsDeleted=0 AND b.Barcode=@v
WHERE p.IsDeleted=0 AND (b.Id IS NOT NULL OR p.Code=@v)
ORDER BY CASE WHEN b.Id IS NOT NULL THEN 0 ELSE 1 END, p.Id;";
        command.Parameters.AddWithValue("@v", v);
        using var reader = command.ExecuteReader();
        if (reader.Read()) return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
        return null;
    }

    public List<ProductPrice> GetPrices(int productId)
    {
        var list = new List<ProductPrice>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT p.Id, p.ProductId, p.UnitId, mu.Name, p.PriceType, p.Price, p.CurrencyId, cur.Code,
       p.BranchId, br.Name, p.StartDateTime, p.EndDateTime, p.CustomerId, p.CustomerGroupId, p.PriceListId, p.IsActive,
       p.ListPrice, p.IsTaxIncluded
FROM dbo.ProductPrices p
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits      mu  ON mu.Id  = p.UnitId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies     cur ON cur.Id = p.CurrencyId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches br ON br.Id  = p.BranchId
WHERE p.ProductId=@p AND p.IsDeleted=0 ORDER BY p.Id;";
        command.Parameters.AddWithValue("@p", productId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductPrice
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                UnitId = reader.GetInt32(2),
                UnitName = reader.IsDBNull(3) ? null : reader.GetString(3),
                PriceType = (PriceType)reader.GetInt32(4),
                Price = reader.GetDecimal(5),
                CurrencyId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                CurrencyCode = reader.IsDBNull(7) ? null : reader.GetString(7),
                BranchId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                BranchName = reader.IsDBNull(9) ? null : reader.GetString(9),
                StartDateTime = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                EndDateTime = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                CustomerId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                CustomerGroupId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                PriceListId = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                IsActive = reader.GetBoolean(15),
                ListPrice = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                IsTaxIncluded = reader.GetBoolean(17)
            });
        return list;
    }

    public List<ProductRecipe> GetRecipe(int menuProductId)
    {
        var list = new List<ProductRecipe>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT r.Id, r.MenuProductId, r.ComponentProductId, cp.Name, r.Quantity, r.UnitId, mu.Name
FROM dbo.ProductRecipes r
LEFT JOIN dbo.Products cp ON cp.Id = r.ComponentProductId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = r.UnitId
WHERE r.MenuProductId=@p AND r.IsDeleted=0 ORDER BY r.Id;";
        command.Parameters.AddWithValue("@p", menuProductId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductRecipe
            {
                Id = reader.GetInt32(0),
                MenuProductId = reader.GetInt32(1),
                ComponentProductId = reader.GetInt32(2),
                ComponentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Quantity = reader.GetDecimal(4),
                UnitId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                UnitName = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        return list;
    }

    /// <summary>Ürünün şirketteki tüm şubeler için tüketim deposu ayarları (boşsa null).</summary>
    public List<ProductBranchConsume> GetConsumeSettings(int productId)
    {
        var list = new List<ProductBranchConsume>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT b.Id, b.Name, s.ConsumeWarehouseId, w.Name
FROM [{AppConfig.DatabaseName}].dbo.CompanyBranches b
LEFT JOIN dbo.ProductBranchSettings s ON s.ProductId=@p AND s.BranchId=b.Id AND s.IsDeleted=0
LEFT JOIN dbo.Warehouses w ON w.Id=s.ConsumeWarehouseId AND w.IsDeleted=0
WHERE b.CompanyId=@c AND b.IsDeleted=0
ORDER BY b.Code;";
        command.Parameters.AddWithValue("@p", productId);
        command.Parameters.AddWithValue("@c", Session.CurrentCompany?.Id ?? 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductBranchConsume
            {
                BranchId = reader.GetInt32(0),
                BranchName = reader.GetString(1),
                ConsumeWarehouseId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                WarehouseName = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        return list;
    }

    public Product Save(Product p, IEnumerable<int> categoryIds,
        IEnumerable<ProductImage> images, IEnumerable<ProductDescription> descriptions,
        IEnumerable<ProductUnit> units, IEnumerable<ProductBarcode> barcodes,
        IEnumerable<ProductPrice> prices, IEnumerable<ProductRecipe> recipe,
        IEnumerable<ProductBranchConsume> consume, int? userId,
        IEnumerable<ProductOption>? options = null)
    {
        string code = CodeHelper.Normalize(p.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir ürün kodu giriniz.");
        if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("Ürün adı boş olamaz.");
        if (CodeExists(code, p.Id)) throw new InvalidOperationException($"'{code}' kodlu ürün zaten var.");

        // AUDIT: güncelleme öncesi durumu (fiyat/tanım değişimi izi için) — transaction DIŞINDA snapshot.
        bool isNew = p.Id == 0;
        var beforeSnapshot = isNew ? null : GetById(p.Id);

        // Tüm alt-tablo yazımları (ürün + kategori + birim + barkod + fiyat + reçete + seçenek + tüketim) atomik:
        // tek bağlantı TransactionScope'a otomatik enlist olur; hata olursa Complete() çağrılmaz → hepsi geri alınır.
        using var scope = new System.Transactions.TransactionScope();
        using var connection = Database.OpenCompany();

        if (p.Id == 0)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.Products
    (Code, Name, Description, MainUnitId, SalesTaxGroupId, PurchaseTaxGroupId, ReturnTaxGroupId, TrackingType, IsActive, IsSpicy, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Code, @Name, @Desc, @MU, @ST, @PT, @RT, @TT, @Active, @Spicy, @By);";
            Bind(ins, p, code);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            p.Id = (int)ins.ExecuteScalar()!;
        }
        else
        {
            using var upd = connection.CreateCommand();
            upd.CommandText = @"
UPDATE dbo.Products SET Code=@Code, Name=@Name, Description=@Desc, MainUnitId=@MU,
    SalesTaxGroupId=@ST, PurchaseTaxGroupId=@PT, ReturnTaxGroupId=@RT, TrackingType=@TT, IsActive=@Active, IsSpicy=@Spicy,
    UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
            Bind(upd, p, code);
            upd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Id", p.Id);
            upd.ExecuteNonQuery();
        }

        // Kategori bağlantılarını yeniden yaz (eskiler soft-delete, yeniler eklenir)
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductCategoryLinks SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", p.Id);
            del.ExecuteNonQuery();
        }
        foreach (int cid in categoryIds.Distinct())
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductCategoryLinks (ProductId, CategoryId, CreatedBy) VALUES (@p, @c, @By);";
            ins.Parameters.AddWithValue("@p", p.Id);
            ins.Parameters.AddWithValue("@c", cid);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }

        SaveImages(connection, p.Id, images.ToList(), userId);
        SaveDescriptions(connection, p.Id, descriptions.ToList(), userId);
        SaveUnits(connection, p.Id, units.ToList(), userId);
        SaveBarcodes(connection, p.Id, barcodes.ToList(), userId);
        SavePrices(connection, p.Id, prices.ToList(), userId);
        SaveRecipe(connection, p.Id, recipe.ToList(), userId);
        SaveConsume(connection, p.Id, consume.ToList(), userId);
        SaveOptions(connection, p.Id, (options ?? Enumerable.Empty<ProductOption>()).ToList(), userId);

        scope.Complete();
        p.Code = code;

        // AUDIT (best-effort, transaction sonrası): ürün kartı ekleme/güncelleme izi.
        AuditService.Write("Product", p.Id, isNew ? "Insert" : "Update",
            beforeSnapshot is null ? null : new { beforeSnapshot.Code, beforeSnapshot.Name, beforeSnapshot.IsActive, beforeSnapshot.TrackingType, beforeSnapshot.MainUnitId, beforeSnapshot.SalesTaxGroupId },
            new { p.Code, p.Name, p.IsActive, p.TrackingType, p.MainUnitId, p.SalesTaxGroupId },
            userId);
        return p;
    }

    /// <summary>Tüketim deposunu şube bazlı upsert eder (IsSalesStopped'a dokunmaz).</summary>
    private static void SaveConsume(SqlConnection connection, int productId, List<ProductBranchConsume> consume, int? userId)
    {
        foreach (var c in consume)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ProductBranchSettings WHERE ProductId=@p AND BranchId=@b AND IsDeleted=0)
    UPDATE dbo.ProductBranchSettings SET ConsumeWarehouseId=@w, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
        WHERE ProductId=@p AND BranchId=@b AND IsDeleted=0;
ELSE IF @w IS NOT NULL
    INSERT INTO dbo.ProductBranchSettings (ProductId, BranchId, ConsumeWarehouseId, CreatedBy)
        VALUES (@p, @b, @w, @By);";
            command.Parameters.AddWithValue("@p", productId);
            command.Parameters.AddWithValue("@b", c.BranchId);
            command.Parameters.AddWithValue("@w", (object?)c.ConsumeWarehouseId ?? DBNull.Value);
            command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private static void SaveRecipe(SqlConnection connection, int menuProductId, List<ProductRecipe> recipe, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductRecipes SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE MenuProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", menuProductId);
            del.ExecuteNonQuery();
        }
        foreach (var r in recipe)
        {
            if (r.ComponentProductId == 0) continue;
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductRecipes (MenuProductId, ComponentProductId, Quantity, UnitId, CreatedBy) VALUES (@m,@c,@q,@u,@By);";
            ins.Parameters.AddWithValue("@m", menuProductId);
            ins.Parameters.AddWithValue("@c", r.ComponentProductId);
            ins.Parameters.AddWithValue("@q", r.Quantity);
            ins.Parameters.AddWithValue("@u", (object?)r.UnitId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void SavePrices(SqlConnection connection, int productId, List<ProductPrice> prices, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductPrices SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        foreach (var pr in prices)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.ProductPrices (ProductId, UnitId, PriceType, Price, ListPrice, IsTaxIncluded, CurrencyId, BranchId, StartDateTime, EndDateTime, CustomerId, CustomerGroupId, PriceListId, IsActive, CreatedBy)
VALUES (@p,@u,@t,@price,@list,@taxincl,@cur,@br,@sd,@ed,@cu,@cg,@pl,@act,@By);";
            ins.Parameters.AddWithValue("@p", productId);
            ins.Parameters.AddWithValue("@u", pr.UnitId);
            ins.Parameters.AddWithValue("@t", (int)pr.PriceType);
            ins.Parameters.AddWithValue("@price", pr.Price);
            ins.Parameters.AddWithValue("@list", (object?)pr.ListPrice ?? DBNull.Value);
            ins.Parameters.AddWithValue("@taxincl", pr.IsTaxIncluded);
            ins.Parameters.AddWithValue("@cur", (object?)pr.CurrencyId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@br", (object?)pr.BranchId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@sd", (object?)pr.StartDateTime ?? DBNull.Value);
            ins.Parameters.AddWithValue("@ed", (object?)pr.EndDateTime ?? DBNull.Value);
            ins.Parameters.AddWithValue("@cu", (object?)pr.CustomerId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@cg", (object?)pr.CustomerGroupId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@pl", (object?)pr.PriceListId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@act", pr.IsActive);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void SaveOptions(SqlConnection connection, int productId, List<ProductOption> options, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductOptions SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        foreach (var o in options)
        {
            if (string.IsNullOrWhiteSpace(o.GroupName) || string.IsNullOrWhiteSpace(o.Name)) continue;
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductOptions (ProductId, GroupName, Name, PriceDelta, IsDefault, SortOrder, CreatedBy) VALUES (@p,@g,@n,@d,@def,@s,@By);";
            ins.Parameters.AddWithValue("@p", productId);
            ins.Parameters.AddWithValue("@g", o.GroupName.Trim());
            ins.Parameters.AddWithValue("@n", o.Name.Trim());
            ins.Parameters.AddWithValue("@d", o.PriceDelta);
            ins.Parameters.AddWithValue("@def", o.IsDefault);
            ins.Parameters.AddWithValue("@s", o.SortOrder);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    public List<ProductOption> GetOptions(int productId)
    {
        var list = new List<ProductOption>();
        if (productId == 0) return list;
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, GroupName, Name, PriceDelta, IsDefault, SortOrder FROM dbo.ProductOptions WHERE ProductId=@p AND IsDeleted=0 ORDER BY GroupName, SortOrder, Id;";
        cmd.Parameters.AddWithValue("@p", productId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProductOption { Id = r.GetInt32(0), ProductId = productId, GroupName = r.GetString(1), Name = r.GetString(2), PriceDelta = r.GetDecimal(3), IsDefault = r.GetBoolean(4), SortOrder = r.GetInt32(5) });
        return list;
    }

    private static void SaveUnits(SqlConnection connection, int productId, List<ProductUnit> units, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductUnits SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        if (units.Count == 0) return;
        if (!units.Any(u => u.IsMainUnit)) units[0].IsMainUnit = true;
        bool mainSeen = false;
        foreach (var u in units)
        {
            bool isMain = u.IsMainUnit && !mainSeen;
            if (isMain) mainSeen = true;
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.ProductUnits (ProductId, UnitId, IsMainUnit, ConversionFactor1, ConversionFactor2, Width, Length, Height, Weight, CreatedBy)
VALUES (@p, @u, @main, @c1, @c2, @w, @l, @h, @wg, @By);";
            ins.Parameters.AddWithValue("@p", productId);
            ins.Parameters.AddWithValue("@u", u.UnitId);
            ins.Parameters.AddWithValue("@main", isMain);
            ins.Parameters.AddWithValue("@c1", u.ConversionFactor1);
            ins.Parameters.AddWithValue("@c2", u.ConversionFactor2);
            ins.Parameters.AddWithValue("@w", (object?)u.Width ?? DBNull.Value);
            ins.Parameters.AddWithValue("@l", (object?)u.Length ?? DBNull.Value);
            ins.Parameters.AddWithValue("@h", (object?)u.Height ?? DBNull.Value);
            ins.Parameters.AddWithValue("@wg", (object?)u.Weight ?? DBNull.Value);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void SaveBarcodes(SqlConnection connection, int productId, List<ProductBarcode> barcodes, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductBarcodes SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        foreach (var b in barcodes)
        {
            if (string.IsNullOrWhiteSpace(b.Barcode)) continue;
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductBarcodes (ProductId, UnitId, Barcode, CreatedBy) VALUES (@p, @u, @bc, @By);";
            ins.Parameters.AddWithValue("@p", productId);
            ins.Parameters.AddWithValue("@u", b.UnitId);
            ins.Parameters.AddWithValue("@bc", b.Barcode.Trim());
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void SaveImages(SqlConnection connection, int productId, List<ProductImage> images, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductImages SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        if (images.Count == 0) return;

        // En az bir ana resim olsun
        if (!images.Any(i => i.IsMain)) images[0].IsMain = true;
        bool mainSeen = false;
        int order = 0;
        foreach (var img in images)
        {
            bool isMain = img.IsMain && !mainSeen;
            if (isMain) mainSeen = true;
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductImages (ProductId, Image, IsMain, SortOrder, CreatedBy) VALUES (@p, @img, @main, @ord, @By);";
            ins.Parameters.AddWithValue("@p", productId);
            var ip = new SqlParameter("@img", SqlDbType.VarBinary, -1) { Value = img.Image };
            ins.Parameters.Add(ip);
            ins.Parameters.AddWithValue("@main", isMain);
            ins.Parameters.AddWithValue("@ord", order++);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void SaveDescriptions(SqlConnection connection, int productId, List<ProductDescription> descriptions, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.ProductDescriptions SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE ProductId=@p AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@p", productId);
            del.ExecuteNonQuery();
        }
        int order = 0;
        foreach (var d in descriptions)
        {
            if (string.IsNullOrWhiteSpace(d.Description)) continue;
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ProductDescriptions (ProductId, Description, SortOrder, IsActive, CreatedBy) VALUES (@p, @d, @ord, 1, @By);";
            ins.Parameters.AddWithValue("@p", productId);
            ins.Parameters.AddWithValue("@d", d.Description.Trim());
            ins.Parameters.AddWithValue("@ord", order++);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static void Bind(SqlCommand cmd, Product p, string code)
    {
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", p.Name.Trim());
        CompanyService.AddNullable(cmd, "@Desc", p.Description);
        cmd.Parameters.AddWithValue("@MU", (object?)p.MainUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ST", (object?)p.SalesTaxGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PT", (object?)p.PurchaseTaxGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RT", (object?)p.ReturnTaxGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TT", (int)p.TrackingType);
        cmd.Parameters.AddWithValue("@Active", p.IsActive);
        cmd.Parameters.AddWithValue("@Spicy", p.IsSpicy);
    }

    private static Product Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        MainUnitId = r.IsDBNull(4) ? null : r.GetInt32(4),
        SalesTaxGroupId = r.IsDBNull(5) ? null : r.GetInt32(5),
        PurchaseTaxGroupId = r.IsDBNull(6) ? null : r.GetInt32(6),
        ReturnTaxGroupId = r.IsDBNull(7) ? null : r.GetInt32(7),
        TrackingType = (TrackingType)r.GetInt32(8),
        IsActive = r.GetBoolean(9),
        MainUnitName = r.IsDBNull(10) ? null : r.GetString(10),
        SalesTaxName = r.IsDBNull(11) ? null : r.GetString(11),
        IsDeleted = r.GetBoolean(12),
        CategoryNames = r.IsDBNull(13) ? null : r.GetString(13),
        IsSpicy = !r.IsDBNull(14) && r.GetBoolean(14),
        HasImage = !r.IsDBNull(15) && r.GetInt32(15) == 1
    };
}
