using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Bir ürün için maliyet + marj bilgisi (marj tutarı ve satış fiyatı üzerinden katkı marjı %).</summary>
public class MarginInfo
{
    public int ProductId { get; set; }
    public decimal Cost { get; set; }
    public decimal SalePrice { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal MarginPercent { get; set; }
}

/// <summary>
/// FAZ 1a Maliyet Motoru: ürün başına ağırlıklı-ortalama alış maliyeti (Products.AvgCost) +
/// menü (reçeteli) ürünler için bileşen maliyetlerinin özyinelemeli rollup'ı.
/// Alış evrağı (İrsaliye/Fatura) POST edilince UpdateAvgCostOnPurchase çağrılır (aynı tx).
/// Hesap mantığı saf CostMath'te; bu servis yalnız DB okuma/yazma yapar. ADO.NET; EF yok.
/// </summary>
public class CostService
{
    /// <summary>
    /// Bir alış evrağının stok girişinden SONRA çağrılır: evraktaki her ürün için ağırlıklı-ortalama maliyeti günceller.
    /// Stok zaten bu tx içinde girildiği için mevcut bakiyeden alınan miktar düşülerek "alış öncesi" stok bulunur.
    /// Aynı ürün birden fazla satırdaysa miktar/tutar toplanır (tek parti gibi). posting ile AYNI tx — hata olursa
    /// posting de geri alınır (maliyet doğruluğu önceliklidir). Not: tutarlar evrak dövizindedir (MVP: TL varsayımı).
    /// </summary>
    public void UpdateAvgCostOnPurchase(SqlConnection c, SqlTransaction tx, int documentId, int warehouseId)
    {
        // Evrak satırlarını ürün bazında topla: alınan miktar + NET tutar (KDV hariç, iskonto dahil = gerçek alış maliyeti).
        var agg = new List<(int ProductId, decimal Qty, decimal NetValue)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT ProductId, SUM(Qty) AS Qty, SUM(LineNet) AS NetValue
FROM dbo.DocumentLines WHERE DocumentId=@d AND IsDeleted=0
GROUP BY ProductId;";
            cmd.Parameters.AddWithValue("@d", documentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                agg.Add((r.GetInt32(0), r.GetDecimal(1), r.GetDecimal(2)));
        }

        foreach (var (productId, inQty, netValue) in agg)
        {
            if (inQty <= 0) continue;
            // BULGU 1 — fiyatsız irsaliye (netValue<=0): maliyeti kirletme. Fiyatsız alışta birim fiyat 0 olur ve
            // ağırlıklı-ortalamayı aşağı çeker; oysa gerçek fiyat sonradan gelen faturayla belli olur
            // (UpdateAvgCostOnInvoiceOverWaybill o zaman düzeltir). 0/negatif tutarlı satırı atla, avg korunur.
            if (netValue <= 0) continue;
            decimal balanceNow = BalanceInWarehouse(c, tx, productId, warehouseId);
            decimal oldQty = balanceNow - inQty;              // alış öncesi stok (giriş bu tx'te zaten eklendi)
            decimal oldAvg = ReadAvgCost(c, tx, productId);
            decimal inPrice = netValue / inQty;               // etkin birim alış maliyeti (net)
            decimal newAvg = CostMath.WeightedAvg(oldQty, oldAvg, inQty, inPrice);
            WriteCost(c, tx, productId, newAvg, decimal.Round(inPrice, 4));
        }
    }

    /// <summary>
    /// BULGU 1 — İrsaliyeden üretilen alış FATURASI Post edilirken çağrılır (stok zaten irsaliyede hareket etti,
    /// tekrar edilmez → yalnız MALİYET güncellenir). Faturanın nihai satır tutarı, kaynak irsaliyeninkinden
    /// farklıysa AvgCost düzeltilir. İki durum:
    ///   • Fiyatlı irsaliye (irsaliyeNet>0): irsaliye Post'ta maliyeti zaten güncelledi → yalnız FARKI uygula
    ///     (faturaNet − irsaliyeNet) mevcut bakiyeye dağıtılır. Fiyat aynıysa fark 0 → değişiklik yok.
    ///   • Fiyatsız irsaliye (irsaliyeNet≤0): irsaliye Post'ta maliyeti atladı → faturanın miktarını fatura
    ///     fiyatıyla ağırlıklı-ortalamaya dahil et (yeniden kurma: alış öncesi stok = bakiye − faturaMiktarı).
    /// Fiyatsız fatura (faturaNet≤0) maliyeti kirletmesin diye atlanır. posting ile AYNI tx.
    /// </summary>
    public void UpdateAvgCostOnInvoiceOverWaybill(SqlConnection c, SqlTransaction tx, int invoiceDocumentId, int warehouseId)
    {
        var agg = new List<(int ProductId, decimal Qty, decimal NetValue)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT ProductId, SUM(Qty) AS Qty, SUM(LineNet) AS NetValue
FROM dbo.DocumentLines WHERE DocumentId=@d AND IsDeleted=0
GROUP BY ProductId;";
            cmd.Parameters.AddWithValue("@d", invoiceDocumentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                agg.Add((r.GetInt32(0), r.GetDecimal(1), r.GetDecimal(2)));
        }

        foreach (var (productId, invQty, invNet) in agg)
        {
            if (invQty <= 0) continue;
            if (invNet <= 0) continue;   // fiyatsız fatura → maliyeti kirletme
            decimal waybillNet = SourceWaybillNet(c, tx, invoiceDocumentId, productId);
            decimal balance = BalanceInWarehouse(c, tx, productId, warehouseId);
            decimal currentAvg = ReadAvgCost(c, tx, productId);
            decimal invPrice = invNet / invQty;
            decimal newAvg;
            if (waybillNet > 0)
                // Fiyatlı irsaliye zaten maliyeti güncelledi → yalnız fatura-irsaliye değer farkını uygula.
                newAvg = CostMath.PriceCorrectedAvg(currentAvg, balance, invNet - waybillNet);
            else
                // Fiyatsız irsaliye maliyeti atladı → faturanın miktarını fatura fiyatıyla ağırlıklı-ortalamaya kat.
                newAvg = CostMath.WeightedAvg(balance - invQty, currentAvg, invQty, invPrice);
            WriteCost(c, tx, productId, newAvg, decimal.Round(invPrice, 4));
        }
    }

    /// <summary>
    /// BULGU 8 — Bir alış evrağı iptal edilince çağrılır: ürünün AvgCost'unu KALAN (onaylı, fiyatlı) alışlardan
    /// yeniden türetir. Her fiziksel giriş bir kez sayılır: faturası varsa fatura (nihai fiyat), yoksa irsaliye.
    /// Faturaya dönüşmüş irsaliye (superseded) çift sayılmaz. Fiyatsız (LineNet≤0) satırlar hariç. İptal edilen
    /// evrak Status=İptal olduğu için (Cancel bunu SetStatus'tan SONRA çağırır) doğal olarak dışlanır.
    /// posting ile AYNI tx.
    /// </summary>
    public void RecomputeAvgCost(SqlConnection c, SqlTransaction tx, int productId)
    {
        decimal remainingNet, remainingQty;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT ISNULL(SUM(dl.LineNet),0), ISNULL(SUM(dl.Qty),0)
FROM dbo.DocumentLines dl
JOIN dbo.Documents d ON d.Id = dl.DocumentId
WHERE dl.ProductId=@p AND dl.IsDeleted=0 AND dl.LineNet > 0 AND d.Status=@onay
  AND (
        d.DocType=@fat
     OR (d.DocType=@irs AND NOT EXISTS (
            SELECT 1 FROM dbo.DocumentLinks lk
            JOIN dbo.Documents t ON t.Id=lk.DocumentId
            WHERE lk.SourceDocumentId=d.Id AND t.DocType=@fat AND t.Status=@onay))
  );";
            cmd.Parameters.AddWithValue("@p", productId);
            cmd.Parameters.AddWithValue("@onay", (int)DocStatus.Onaylandi);
            cmd.Parameters.AddWithValue("@fat", (int)DocType.AlisFatura);
            cmd.Parameters.AddWithValue("@irs", (int)DocType.AlisIrsaliye);
            using var r = cmd.ExecuteReader();
            r.Read();
            remainingNet = r.GetDecimal(0);
            remainingQty = r.GetDecimal(1);
        }
        decimal fallback = ReadAvgCost(c, tx, productId);
        decimal newAvg = CostMath.RecomputeAvg(remainingNet, remainingQty, fallback);
        if (newAvg != fallback)
            WriteAvgCostOnly(c, tx, productId, newAvg);
    }

    /// <summary>Ürünün maliyeti: reçetesi varsa (menü) bileşen maliyetlerinin Σ(maliyet×Quantity) rollup'ı,
    /// yoksa ürünün AvgCost'u. Özyinelemeli; döngü koruması (aynı yol üzerinde tekrar eden ürün 0 döner).</summary>
    public decimal GetProductCost(int productId)
    {
        using var c = Database.OpenCompany();
        return GetProductCost(c, productId, new HashSet<int>());
    }

    private decimal GetProductCost(SqlConnection c, int productId, HashSet<int> path)
    {
        if (!path.Add(productId)) return 0m;   // döngü: bu ürün zaten mevcut yolda → 0 (sonsuz özyineleme koruması)
        try
        {
            var comps = ReadRecipe(c, productId);
            if (comps.Count == 0)
                return ReadAvgCost(c, null, productId);
            decimal sum = 0m;
            foreach (var (componentId, qty) in comps)
                sum += qty * GetProductCost(c, componentId, path);
            return decimal.Round(sum, 4);
        }
        finally { path.Remove(productId); }
    }

    /// <summary>Maliyet + marj (tutar ve satış fiyatı üzerinden katkı marjı %) bilgisi.</summary>
    public MarginInfo GetMarginInfo(int productId, decimal salePrice)
    {
        decimal cost = GetProductCost(productId);
        return new MarginInfo
        {
            ProductId = productId,
            Cost = cost,
            SalePrice = salePrice,
            MarginAmount = CostMath.MarginAmount(cost, salePrice),
            MarginPercent = CostMath.MarginPercent(cost, salePrice)
        };
    }

    // ---- helpers ----
    private static decimal BalanceInWarehouse(SqlConnection c, SqlTransaction tx, int productId, int warehouseId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ISNULL(SUM(Qty),0) FROM dbo.StockMovements WHERE ProductId=@p AND WarehouseId=@w;";
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@w", warehouseId);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    private static decimal ReadAvgCost(SqlConnection c, SqlTransaction? tx, int productId)
    {
        using var cmd = c.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT ISNULL(AvgCost,0) FROM dbo.Products WHERE Id=@p;";
        cmd.Parameters.AddWithValue("@p", productId);
        var o = cmd.ExecuteScalar();
        return o is null || o == DBNull.Value ? 0m : Convert.ToDecimal(o);
    }

    private static List<(int ComponentId, decimal Qty)> ReadRecipe(SqlConnection c, int menuProductId)
    {
        var list = new List<(int, decimal)>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ComponentProductId, Quantity FROM dbo.ProductRecipes WHERE MenuProductId=@p AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@p", menuProductId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetDecimal(1)));
        return list;
    }

    /// <summary>Bir alış faturasının KAYNAK (onaylı) alış irsaliyelerindeki, verilen ürüne ait toplam net tutarı.
    /// 0 dönerse irsaliye fiyatsızdı (ya da fiyatlı kaynak yok).</summary>
    private static decimal SourceWaybillNet(SqlConnection c, SqlTransaction tx, int invoiceDocumentId, int productId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT ISNULL(SUM(dl.LineNet),0)
FROM dbo.DocumentLinks lk
JOIN dbo.Documents s ON s.Id = lk.SourceDocumentId
JOIN dbo.DocumentLines dl ON dl.DocumentId = s.Id AND dl.IsDeleted=0 AND dl.ProductId=@p
WHERE lk.DocumentId=@inv AND s.DocType=@irs AND s.Status=@onay;";
        cmd.Parameters.AddWithValue("@inv", invoiceDocumentId);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@irs", (int)DocType.AlisIrsaliye);
        cmd.Parameters.AddWithValue("@onay", (int)DocStatus.Onaylandi);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    private static void WriteAvgCostOnly(SqlConnection c, SqlTransaction tx, int productId, decimal avgCost)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE dbo.Products SET AvgCost=@avg, UpdatedAt=SYSUTCDATETIME() WHERE Id=@p;";
        cmd.Parameters.AddWithValue("@avg", avgCost);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.ExecuteNonQuery();
    }

    private static void WriteCost(SqlConnection c, SqlTransaction tx, int productId, decimal avgCost, decimal lastCost)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE dbo.Products SET AvgCost=@avg, LastCost=@last, UpdatedAt=SYSUTCDATETIME() WHERE Id=@p;";
        cmd.Parameters.AddWithValue("@avg", avgCost);
        cmd.Parameters.AddWithValue("@last", lastCost);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.ExecuteNonQuery();
    }
}
