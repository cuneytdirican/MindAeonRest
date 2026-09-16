using MindAeonRest.Data;

namespace MindAeonRest.Services;

/// <summary>Şubeler arası toplu fiyat aktarımı: kaynak şubenin şube-özel fiyatlarını hedef şubeye kopyalar
/// (hedefin mevcut şube-özel fiyatları önce temizlenir → "varsa güncelle, yoksa oluştur").
/// Menü/reçeteler ürün bazlı (firma geneli) olduğundan şubeye özel kopyalanmaz.</summary>
public class PriceTransferService
{
    public int Transfer(int sourceBranchId, int targetBranchId, int? userId)
    {
        if (sourceBranchId == targetBranchId) throw new ArgumentException("Kaynak ve hedef şube aynı olamaz.");
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            // Yalnız GENEL (müşteri/grup/fiyat-listesi olmayan) şube fiyatları aktarılır; hedefteki müşteri-özel
            // fiyatlar KORUNUR. Hard-delete yerine soft-delete (tutarlılık + geri izlenebilirlik).
            const string scope = "CustomerId IS NULL AND CustomerGroupId IS NULL AND PriceListId IS NULL";
            using (var del = c.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = $"UPDATE dbo.ProductPrices SET IsDeleted=1, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE BranchId=@t AND IsDeleted=0 AND {scope};";
                del.Parameters.AddWithValue("@t", targetBranchId);
                del.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                del.ExecuteNonQuery();
            }
            int n;
            using (var ins = c.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = $@"
INSERT INTO dbo.ProductPrices (ProductId, UnitId, PriceType, Price, ListPrice, IsTaxIncluded, CurrencyId, BranchId, StartDateTime, EndDateTime, CustomerId, CustomerGroupId, PriceListId, IsActive, CreatedBy)
SELECT ProductId, UnitId, PriceType, Price, ListPrice, IsTaxIncluded, CurrencyId, @t, StartDateTime, EndDateTime, CustomerId, CustomerGroupId, PriceListId, IsActive, @by
FROM dbo.ProductPrices WHERE BranchId=@s AND IsDeleted=0 AND {scope};
SELECT @@ROWCOUNT;";
                ins.Parameters.AddWithValue("@t", targetBranchId);
                ins.Parameters.AddWithValue("@s", sourceBranchId);
                ins.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                n = (int)ins.ExecuteScalar()!;
            }
            tx.Commit();
            return n;
        }
        catch { tx.Rollback(); throw; }
    }
}
