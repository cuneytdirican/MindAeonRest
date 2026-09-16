using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Ürünlerin şube bazlı ayarları: satış durdurma (ve ileride tüketim deposu).</summary>
public class ProductBranchSettingsService
{
    /// <summary>Belirtilen şube için tüm ürünlerin satış durumu.</summary>
    public List<ProductSalesStatus> GetSalesStatus(int branchId)
    {
        var list = new List<ProductSalesStatus>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT p.Id, p.Code, p.Name, ISNULL(s.IsSalesStopped, 0)
FROM dbo.Products p
LEFT JOIN dbo.ProductBranchSettings s ON s.ProductId = p.Id AND s.BranchId = @b AND s.IsDeleted = 0
WHERE p.IsDeleted = 0
ORDER BY p.Code;";
        command.Parameters.AddWithValue("@b", branchId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductSalesStatus
            {
                ProductId = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                IsSalesStopped = reader.GetBoolean(3)
            });
        return list;
    }

    /// <summary>Seçili ürünlerin satışını bu şube için durdurur/açar (upsert).</summary>
    public void SetSalesStopped(IEnumerable<int> productIds, int branchId, bool stopped, int? userId)
    {
        using var connection = Database.OpenCompany();
        foreach (int productId in productIds.Distinct())
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ProductBranchSettings WHERE ProductId=@p AND BranchId=@b AND IsDeleted=0)
    UPDATE dbo.ProductBranchSettings SET IsSalesStopped=@s, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
        WHERE ProductId=@p AND BranchId=@b AND IsDeleted=0;
ELSE
    INSERT INTO dbo.ProductBranchSettings (ProductId, BranchId, IsSalesStopped, CreatedBy)
        VALUES (@p, @b, @s, @By);";
            command.Parameters.AddWithValue("@p", productId);
            command.Parameters.AddWithValue("@b", branchId);
            command.Parameters.AddWithValue("@s", stopped);
            command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }
}
