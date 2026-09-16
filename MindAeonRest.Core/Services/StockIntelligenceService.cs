using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Teorik/gerçek sapma satırı (ham bileşen bazlı). Miktar + maliyet (CostService).</summary>
public class StockVarianceRow
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal TheoreticalQty { get; set; }   // satılan ürünlerin reçeteye göre öngörülen tüketimi
    public decimal ActualQty { get; set; }         // gerçek stok çıkışı (satış + fire + sayım farkı)
    public decimal UnitCost { get; set; }          // reçete rollup dahil birim maliyet

    /// <summary>Sapma miktarı = gerçek − teorik. Pozitif = fazla tüketim (fire/kayıp).</summary>
    public decimal VarianceQty => decimal.Round(ActualQty - TheoreticalQty, 3);
    public decimal VariancePercent => StockIntelMath.VariancePercent(TheoreticalQty, ActualQty);
    public decimal TheoreticalCost => decimal.Round(TheoreticalQty * UnitCost, 2);
    public decimal ActualCost => decimal.Round(ActualQty * UnitCost, 2);
    /// <summary>Sapmanın maliyeti (fire/kayıp tutarı). Pozitif = kayıp.</summary>
    public decimal VarianceCost => decimal.Round(VarianceQty * UnitCost, 2);

    public string TheoreticalQtyText => $"{TheoreticalQty:0.###}";
    public string ActualQtyText => $"{ActualQty:0.###}";
    public string VarianceQtyText => $"{VarianceQty:0.###}";
    public string VarianceCostText => $"{VarianceCost:N2}";
    public bool IsLoss => VarianceQty > 0.0005m;
}

/// <summary>Teorik/gerçek sapma raporu (tarih aralığı + şube/depo).</summary>
public class StockVarianceReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public List<StockVarianceRow> Rows { get; set; } = new();

    public decimal TotalTheoreticalCost => Math.Round(Rows.Sum(r => r.TheoreticalCost), 2);
    public decimal TotalActualCost => Math.Round(Rows.Sum(r => r.ActualCost), 2);
    /// <summary>Toplam sapma maliyeti = öngörülenin üzerindeki gerçek tüketim (fire/kayıp) tutarı.</summary>
    public decimal TotalVarianceCost => Math.Round(Rows.Sum(r => r.VarianceCost), 2);
}

/// <summary>Düşük-stok satırı: bakiye MinStock altında olan ürün + önerilen sipariş miktarı.</summary>
public class LowStockItem
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Balance { get; set; }
    public decimal MinStock { get; set; }
    public decimal AvgCost { get; set; }

    /// <summary>Eşiğe tamamlamak için gereken miktar (MinStock − bakiye), en az 0.</summary>
    public decimal SuggestedQty => Math.Max(0m, decimal.Round(MinStock - Balance, 3));
    public string BalanceText => $"{Balance:0.###}";
    public string MinStockText => $"{MinStock:0.###}";
    public string SuggestedQtyText => $"{SuggestedQty:0.###}";
}

/// <summary>
/// FAZ 4b Stok Zekâsı: (1) satılan ürünlerin reçeteye göre TEORİK bileşen tüketimi vs GERÇEK stok
/// hareketi (satış + fire + sayım farkı) → sapma/fire-kayıp raporu, CostService ile maliyetlendirilir;
/// (2) düşük-stok (bakiye &lt; MinStock) tespiti; (3) düşük-stok ürünlerden AlisSiparis TASLAĞI üretimi
/// (mevcut DocumentService evrak motoru). SALT-OKUMA hesaplar; taslak üretimi yalnız yeni Taslak evrak yazar.
/// Saf hesap mantığı StockIntelMath'te (DB'siz test edilebilir).
/// </summary>
public class StockIntelligenceService
{
    private readonly CostService _cost = new();
    private readonly BranchService _branch = new();

    // GERÇEK tüketim sayılan stok hareket tipleri: Satış(0) + Satış İptal(1, geri giriş) + Sayım(4, ± fark) + Fire(7).
    // Bu küme reçete-tabanlı POS tüketimi + fire + sayım farkını kapsar (belge alış/satış çıkışları hariç).
    private static readonly int[] ConsumptionTypes = { 0, 1, 4, 7 };

    private static string BizDay(string col) => $"DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, {col})";
    private int ResolveCutoff(int branchId) => branchId > 0 ? _branch.GetDayStartMinutes(branchId) : 0;

    /// <summary>Teorik (satış×reçete) vs gerçek (stok çıkışı) bileşen tüketim sapması. warehouseId gerçek
    /// hareketleri daraltır (teorik şube geneli satışa dayanır). branchId=0 → tüm şubeler.</summary>
    public StockVarianceReport GetVarianceReport(DateTime from, DateTime to, int branchId, int? warehouseId = null)
    {
        var f = from.Date; var t = to.Date;
        if (t < f) (f, t) = (t, f);
        int cutoff = ResolveCutoff(branchId);
        var report = new StockVarianceReport { From = f, To = t, BranchId = branchId, WarehouseId = warehouseId };

        // 1) Satılan ana ürünler (kapanan adisyon, ikram/iptal/silinmiş/iade hariç) — MenuEngineering deseni.
        var sales = new List<(int ProductId, decimal Qty)>();
        string oDate = $"CAST({BizDay("o.ClosedAt")} AS DATE)";
        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT l.ProductId, SUM(l.Qty - l.RefundedQty) AS Qty
FROM dbo.OrderLines l
JOIN dbo.Orders o ON o.Id=l.OrderId
WHERE (@b=0 OR o.BranchId=@b) AND o.Status=1 AND {oDate} BETWEEN @f AND @t
  AND l.IsDeleted=0 AND l.LineStatus<>4 AND l.ParentLineId IS NULL AND l.IsTreat=0 AND l.ProductId IS NOT NULL
GROUP BY l.ProductId
HAVING SUM(l.Qty - l.RefundedQty) > 0;";
            cmd.Parameters.AddWithValue("@f", f);
            cmd.Parameters.AddWithValue("@t", t);
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read()) sales.Add((r.GetInt32(0), r.GetDecimal(1)));
        }

        // 2) Reçete haritası + TEORİK ham bileşen tüketimi (saf StockIntelMath rollup).
        var recipes = LoadRecipeMap();
        IReadOnlyList<(int, decimal)> RecipeOf(int pid) =>
            recipes.TryGetValue(pid, out var list) ? list : System.Array.Empty<(int, decimal)>();
        var theoretical = StockIntelMath.ExpandConsumption(sales, RecipeOf);

        // 3) GERÇEK tüketim: stok hareketlerinin (Satış/İptal/Sayım/Fire) toplamının negatifi.
        var actual = LoadActualConsumption(f, t, branchId, warehouseId);

        // 4) Birleştir + maliyetlendir (CostService reçete rollup dahil birim maliyet).
        var names = LoadProductNames();
        var keys = new HashSet<int>(theoretical.Keys);
        keys.UnionWith(actual.Keys);
        foreach (var pid in keys)
        {
            theoretical.TryGetValue(pid, out var th);
            actual.TryGetValue(pid, out var ac);
            names.TryGetValue(pid, out var nm);
            report.Rows.Add(new StockVarianceRow
            {
                ProductId = pid,
                Code = nm.Code ?? "",
                Name = nm.Name ?? "-",
                TheoreticalQty = decimal.Round(th, 3),
                ActualQty = decimal.Round(ac, 3),
                UnitCost = _cost.GetProductCost(pid)
            });
        }
        // En yüksek kayıp (sapma maliyeti) üstte.
        report.Rows = report.Rows.OrderByDescending(x => x.VarianceCost).ThenBy(x => x.Name).ToList();
        return report;
    }

    /// <summary>Bakiye &lt; MinStock olan ürünler (MinStock&gt;0). warehouseId verilirse o depo bakiyesi,
    /// yoksa şube (branchId) bakiyesi; branchId=0 → tüm hareketler.</summary>
    public List<LowStockItem> GetLowStock(int? branchId = null, int? warehouseId = null)
    {
        var list = new List<LowStockItem>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT p.Id, p.Code, p.Name, p.MinStock, p.AvgCost, ISNULL(bal.Qty,0) AS Bal
FROM dbo.Products p
LEFT JOIN (
    SELECT ProductId, SUM(Qty) AS Qty FROM dbo.StockMovements
    WHERE (@br IS NULL OR BranchId=@br) AND (@wh IS NULL OR WarehouseId=@wh)
    GROUP BY ProductId
) bal ON bal.ProductId = p.Id
WHERE p.IsDeleted=0 AND p.MinStock > 0 AND ISNULL(bal.Qty,0) < p.MinStock
ORDER BY (p.MinStock - ISNULL(bal.Qty,0)) DESC, p.Code;";
        cmd.Parameters.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wh", (object?)warehouseId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LowStockItem
            {
                ProductId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                MinStock = r.GetDecimal(3), AvgCost = r.GetDecimal(4), Balance = r.GetDecimal(5)
            });
        return list;
    }

    /// <summary>Düşük-stok ürünlerden bir AlisSiparis TASLAĞI üretir (mevcut DocumentService motoru):
    /// her ürün için önerilen miktar (MinStock−bakiye), birim fiyat = son/ortalama maliyet tahmini.
    /// Tedarikçi cari boş bırakılır (kullanıcı taslakta doldurur). Düşük-stok yoksa 0 döner.</summary>
    public int GenerateReorderDraft(int branchId, int? warehouseId, int? userId)
    {
        if (branchId <= 0) throw new ArgumentException("Sipariş taslağı için şube seçilmeli.");
        var low = GetLowStock(branchId, warehouseId).Where(x => x.SuggestedQty > 0).ToList();
        if (low.Count == 0) return 0;

        var lines = low.Select(x => new DocumentLine
        {
            ProductId = x.ProductId,
            ProductName = x.Name,
            ProductCode = x.Code,
            Qty = x.SuggestedQty,
            UnitPrice = x.AvgCost,   // tahmini alış fiyatı (kullanıcı revize eder)
            TaxRate = 0
        }).ToList();

        var doc = new Document
        {
            DocType = DocType.AlisSiparis,
            BranchId = branchId,
            WarehouseId = warehouseId,
            DocDate = DateTime.Now,
            Status = DocStatus.Taslak,
            Note = $"Düşük-stok oto-sipariş taslağı ({low.Count} kalem) — Stok Zekâsı"
        };
        new DocumentService().Save(doc, lines, userId);
        return doc.Id;
    }

    // ---- helpers ----
    private static Dictionary<int, List<(int ComponentId, decimal Quantity)>> LoadRecipeMap()
    {
        var map = new Dictionary<int, List<(int, decimal)>>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MenuProductId, ComponentProductId, Quantity FROM dbo.ProductRecipes WHERE IsDeleted=0;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int menu = r.GetInt32(0);
            if (!map.TryGetValue(menu, out var list)) map[menu] = list = new List<(int, decimal)>();
            list.Add((r.GetInt32(1), r.GetDecimal(2)));
        }
        return map;
    }

    private Dictionary<int, decimal> LoadActualConsumption(DateTime f, DateTime t, int branchId, int? warehouseId)
    {
        var map = new Dictionary<int, decimal>();
        int cutoff = ResolveCutoff(branchId);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        // MoveDate UTC saklanır. TEORİK (satış) tarafı BizDay(UTC→yerel + DayStartMinutes kesimi) kullandığından,
        // GERÇEK taraf da BİREBİR aynı pencereyi kullanmalı — aksi halde sınırda (tek-günlük rapor / gece-yarısı
        // geçen mekan) teorik-var/gerçek-yok "hayali fire" oluşur. Aynı @cutoff + aynı CAST-AS-DATE BETWEEN @f..@t.
        string mDate = $"CAST({BizDay("MoveDate")} AS DATE)";
        cmd.CommandText = $@"
SELECT ProductId, -SUM(Qty) AS Consumed
FROM dbo.StockMovements
WHERE MovementType IN ({string.Join(",", ConsumptionTypes)})
  AND {mDate} BETWEEN @f AND @t
  AND (@b=0 OR BranchId=@b) AND (@wh IS NULL OR WarehouseId=@wh)
GROUP BY ProductId;";
        cmd.Parameters.AddWithValue("@f", f);
        cmd.Parameters.AddWithValue("@t", t);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@wh", (object?)warehouseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetInt32(0)] = r.GetDecimal(1);
        return map;
    }

    private static Dictionary<int, (string Code, string Name)> LoadProductNames()
    {
        var map = new Dictionary<int, (string, string)>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name FROM dbo.Products;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetInt32(0)] = (r.GetString(1), r.GetString(2));
        return map;
    }
}
