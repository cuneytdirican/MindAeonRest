using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Adisyon ekranı kataloğu: kategoriler + fiyatı/resmi/satış-durumu çözülmüş ürünler.</summary>
public class PosCatalogService
{
    private readonly ProductCategoryService _categories = new();
    private readonly ProductService _products = new();

    public List<ProductCategory> GetCategories() => _categories.GetAll();

    public List<PosProduct> GetProducts(int branchId, int? priceListId = null)
    {
        var list = new List<PosProduct>();
        var now = DateTime.Now;

        using var connection = Database.OpenCompany();

        // Ürünler
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Code, Name, Description FROM dbo.Products WHERE IsDeleted=0 AND IsActive=1 ORDER BY Name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new PosProduct
                {
                    Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3)
                });
        }
        var byId = list.ToDictionary(p => p.Id);

        // Kategori bağlantıları
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT ProductId, CategoryId FROM dbo.ProductCategoryLinks;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p)) p.CategoryIds.Add(r.GetInt32(1));
        }

        // Satışı durdurulmuş (şube bazlı)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT ProductId FROM dbo.ProductBranchSettings WHERE BranchId=@b AND IsSalesStopped=1 AND IsDeleted=0;";
            cmd.Parameters.AddWithValue("@b", branchId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p)) p.IsSalesStopped = true;
        }

        // Menü mü (reçetesi var mı)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT MenuProductId FROM dbo.ProductRecipes WHERE IsDeleted=0;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p)) p.IsMenu = true;
        }

        // Satış fiyatları: genel + şube + fiyat-listesi adaylarının HEPSİ yüklenir (müşteri/grup özel hariç).
        // Geçerli fiyat istemci tarafında ResolvePrice ile seçilir → fiyat listesi değişince DB'ye gitmeden güncellenir.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ProductId, Price, BranchId, PriceListId, StartDateTime, EndDateTime
FROM dbo.ProductPrices
WHERE IsDeleted=0 AND IsActive=1 AND PriceType=2
  AND CustomerId IS NULL AND CustomerGroupId IS NULL
ORDER BY StartDateTime DESC, Id DESC;";   // çakışan geçerli fiyatlarda EN GÜNCEL öncelikli (deterministik)
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p))
                    p.PriceCandidates.Add(new PosPrice
                    {
                        Price = r.GetDecimal(1),
                        BranchId = r.IsDBNull(2) ? null : r.GetInt32(2),
                        PriceListId = r.IsDBNull(3) ? null : r.GetInt32(3),
                        Start = r.IsDBNull(4) ? null : r.GetDateTime(4),
                        End = r.IsDBNull(5) ? null : r.GetDateTime(5)
                    });
        }
        foreach (var p in list) p.ResolvePrice(branchId, priceListId, now);

        // Varyant/seçenekler (GroupName ile gruplanır)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT ProductId, GroupName, Name, PriceDelta, IsDefault, SortOrder FROM dbo.ProductOptions WHERE IsDeleted=0 ORDER BY GroupName, SortOrder, Id;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p))
                    p.Options.Add(new ProductOption { ProductId = p.Id, GroupName = r.GetString(1), Name = r.GetString(2), PriceDelta = r.GetDecimal(3), IsDefault = r.GetBoolean(4), SortOrder = r.GetInt32(5) });
        }

        // Ana resimler
        foreach (var p in list)
            p.Image = _products.GetMainImage(p.Id);

        return list;
    }
}
