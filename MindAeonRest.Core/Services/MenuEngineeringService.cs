using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// FAZ 3a Menü Mühendisliği + gelişmiş analitik motoru. Satış verisini (kapanan adisyon satırları) maliyetle
/// (CostService) birleştirir: ürün başına adet, ciro, maliyet, katkı marjı, marj%, popülerlik + menü matrisi
/// sınıflandırması (Yıldız/İnek/Bilmece/Köpek). Ayrıca gün×saat ısı haritası ve basit talep tahmini.
/// Hepsi SALT-OKUMA. Tarihler UTC saklanır, iş-günü kesimine göre (@cutoff) yerele çevrilip filtrelenir.
/// Saf hesap mantığı MenuMath/ForecastMath'te (DB'siz test edilebilir).
/// </summary>
public class MenuEngineeringService
{
    private readonly CostService _cost = new();
    private readonly BranchService _branch = new();

    // Yerel + iş-günü kesimli ifadeler (BossDashboardService/ReportService ile aynı desen)
    private static string Local(string col) => $"DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), {col})";
    private static string BizDay(string col) => $"DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, {col})";

    /// <summary>branchId>0 ise o şubenin iş-günü kesimi; 0 (tüm şubeler) ise kesim yok (0).</summary>
    private int ResolveCutoff(int branchId) => branchId > 0 ? _branch.GetDayStartMinutes(branchId) : 0;

    /// <summary>Menü mühendisliği matrisi: tarih aralığı + şube (0=tüm) için ürün satış+maliyet+marj+sınıf.</summary>
    public MenuEngineeringReport GetMenuEngineering(DateTime from, DateTime to, int branchId)
    {
        var f = from.Date; var t = to.Date;
        int cutoff = ResolveCutoff(branchId);
        var report = new MenuEngineeringReport { From = f, To = t, BranchId = branchId };

        // Kapanan adisyonların geçerli ana satırları (ikram/iptal/silinmiş hariç) — BossDashboard lineFrom deseni.
        string oDate = $"CAST({BizDay("o.ClosedAt")} AS DATE)";
        string lineNet = "((l.Qty - l.RefundedQty)*l.UnitPrice - l.DiscountAmount)";
        string sql = $@"
SELECT l.ProductId, ISNULL(pr.Name,'-') AS Name,
       ISNULL(SUM(l.Qty - l.RefundedQty),0) AS Qty,
       ISNULL(SUM({lineNet}),0) AS Revenue
FROM dbo.OrderLines l
JOIN dbo.Orders o ON o.Id=l.OrderId
LEFT JOIN dbo.Products pr ON pr.Id=l.ProductId
WHERE (@b=0 OR o.BranchId=@b) AND o.Status=1 AND {oDate} BETWEEN @f AND @t
  AND l.IsDeleted=0 AND l.LineStatus<>4 AND l.ParentLineId IS NULL AND l.IsTreat=0
  AND l.ProductId IS NOT NULL
GROUP BY l.ProductId, pr.Name
HAVING SUM(l.Qty - l.RefundedQty) > 0;";

        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@f", f);
            cmd.Parameters.AddWithValue("@t", t);
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                report.Items.Add(new MenuItemStat
                {
                    ProductId = r.GetInt32(0),
                    Name = r.IsDBNull(1) ? "-" : r.GetString(1),
                    Qty = r.GetDecimal(2),
                    Revenue = r.GetDecimal(3)
                });
            }
        }

        // Maliyet (reçete rollup dahil) — ürün başına CostService.
        foreach (var it in report.Items)
            it.UnitCost = _cost.GetProductCost(it.ProductId);

        // Popülerlik payı
        decimal totalQty = report.Items.Sum(i => i.Qty);
        foreach (var it in report.Items)
            it.PopularityShare = totalQty > 0 ? decimal.Round(it.Qty / totalQty * 100m, 2) : 0m;

        // Eşikler (ortalama) + matris sınıflandırma (saf MenuMath)
        if (report.Items.Count > 0)
        {
            report.AvgQty = decimal.Round(report.Items.Average(i => i.Qty), 4);
            report.AvgUnitMargin = decimal.Round(report.Items.Average(i => i.UnitMargin), 4);
            foreach (var it in report.Items)
                it.MenuClass = MenuMath.Classify(it.Qty, report.AvgQty, it.UnitMargin, report.AvgUnitMargin);
        }

        // Katkı marjı (tutar) azalan sırada
        report.Items = report.Items.OrderByDescending(i => i.ContributionMargin).ToList();
        return report;
    }

    /// <summary>Gün×saat ısı haritası: hafta günü (0=Pzt) × saat (0-23), hücre = ciro + adisyon. İş-günü kesimli.</summary>
    public SalesHeatmap GetHeatmap(DateTime from, DateTime to, int branchId)
    {
        var f = from.Date; var t = to.Date;
        int cutoff = ResolveCutoff(branchId);
        var heat = new SalesHeatmap();

        // İş günü = tarih ataması (weekday bunun üzerinden); saat = gerçek yerel saat.
        string bizDate = $"CAST({BizDay("cf.FicheDate")} AS DATE)";
        string hourExpr = $"DATEPART(HOUR, {Local("cf.FicheDate")})";
        string sql = $@"
SELECT {bizDate} AS Bd, {hourExpr} AS Hh,
       ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0) AS Rev,
       COUNT(DISTINCT cf.OrderId) AS Cnt
FROM dbo.ClFiches cf
WHERE (@b=0 OR cf.BranchId=@b) AND {bizDate} BETWEEN @f AND @t
GROUP BY {bizDate}, {hourExpr};";

        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@f", f);
        cmd.Parameters.AddWithValue("@t", t);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var bd = r.GetDateTime(0);
            int hour = r.GetInt32(1);
            int weekday = ((int)bd.DayOfWeek + 6) % 7;   // .NET Pazar=0 → Pzt=0..Paz=6
            heat.Add(weekday, hour, r.GetDecimal(2), r.GetInt32(3));
        }
        return heat;
    }

    /// <summary>Basit talep tahmini: son <paramref name="lookbackDays"/> günün günlük cirosundan (iş-günü kesimli)
    /// hareketli ortalama + haftalık mevsimsellik ile ertesi gün/hafta ciro tahmini (ForecastMath).</summary>
    public SalesForecast GetForecast(int branchId, int lookbackDays = 28, int window = 7)
    {
        int cutoff = ResolveCutoff(branchId);
        var today = DateTime.Today;
        var start = today.AddDays(-(lookbackDays - 1));

        // Günlük ciro sözlüğü (iş günü tarihine göre)
        var byDay = new Dictionary<DateTime, decimal>();
        string bizDate = $"CAST({BizDay("cf.FicheDate")} AS DATE)";
        string sql = $@"
SELECT {bizDate} AS Bd, ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0) AS Rev
FROM dbo.ClFiches cf
WHERE (@b=0 OR cf.BranchId=@b) AND {bizDate} BETWEEN @f AND @t
GROUP BY {bizDate};";

        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@f", start.Date);
            cmd.Parameters.AddWithValue("@t", today.Date);
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read()) byDay[r.GetDateTime(0)] = r.GetDecimal(1);
        }

        // Sürekli seri: her gün 0 dahil (bugün hariç — kısmî gün tahminini bozmasın)
        var series = new List<double>();
        for (var day = start.Date; day < today.Date; day = day.AddDays(1))
            series.Add(byDay.TryGetValue(day, out var v) ? (double)v : 0.0);

        int startWeekday = ((int)start.Date.DayOfWeek + 6) % 7;
        var next7 = ForecastMath.ForecastNextDays(series, startWeekday, window, 7);

        var fc = new SalesForecast
        {
            LookbackDays = lookbackDays,
            Window = window,
            AvgDailyRevenue = series.Count > 0 ? decimal.Round((decimal)series.Average(), 2) : 0m,
            NextDayRevenue = next7.Length > 0 ? decimal.Round((decimal)next7[0], 2) : 0m
        };

        // series son elemanı (today-1)'in ertesi günü = today; tahmin noktaları bugünden itibaren.
        var firstForecastDay = today.Date;
        decimal total = 0;
        for (int k = 0; k < next7.Length; k++)
        {
            var d = firstForecastDay.AddDays(k);
            int wd = ((int)d.DayOfWeek + 6) % 7;
            var rev = decimal.Round((decimal)next7[k], 2);
            total += rev;
            fc.Next7Days.Add(new ForecastPoint { Date = d, DayName = SalesHeatmap.DayNames[wd], Revenue = rev });
        }
        fc.Next7DaysTotal = total;
        return fc;
    }
}
