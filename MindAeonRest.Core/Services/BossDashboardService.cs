using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Patron ekranı veri motoru: tarih aralığı + şube (0=tüm şubeler) için zengin işletme metrikleri.
/// Hepsi salt-okuma. Tarihler UTC saklanır, yerele çevrilip filtrelenir.</summary>
public class BossDashboardService
{
    private static readonly string[] Palette = { "#E8505B", "#F0B86E", "#7FB069", "#37475A", "#6C7A89", "#9AA7B5", "#C9CDD6" };

    // Yerel tarihe çevrim (UTC->yerel) — kolon adı verilir
    private static string L(string col) => $"DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), {col})";

    public BossDashboard Get(DateTime from, DateTime to, int branchId)
    {
        var f = from.Date; var t = to.Date;
        var db = AppConfig.DatabaseName;
        var d = new BossDashboard();
        using var c = Database.OpenCompany();

        void P(SqlCommand cmd) { cmd.Parameters.AddWithValue("@f", f); cmd.Parameters.AddWithValue("@t", t); cmd.Parameters.AddWithValue("@b", branchId); }

        // Fiş bazlı: ciro / fiş / iade / ödeme — ClFiches
        string cfDate = $"CAST({L("cf.FicheDate")} AS DATE)";
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0),
       COUNT(DISTINCT CASE WHEN cf.Total>0 THEN cf.OrderId END),
       ISNULL(SUM(CASE WHEN cf.Total<0 THEN -cf.Total ELSE 0 END),0)
FROM dbo.ClFiches cf
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t;";
            P(cmd);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { d.Revenue = r.GetDecimal(0); d.FisCount = r.GetInt32(1); d.RefundTotal = r.GetDecimal(2); }
        }
        d.AvgTicket = d.FisCount > 0 ? Math.Round(d.Revenue / d.FisCount, 2) : 0;

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT pl.Kind, SUM(pl.Amount) FROM dbo.PaymentLines pl JOIN dbo.ClFiches cf ON cf.Id=pl.ClFicheId
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t AND pl.Amount>0 GROUP BY pl.Kind;";
            P(cmd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var k = (PaymentKind)r.GetInt32(0); decimal a = r.GetDecimal(1);
                if (k == PaymentKind.Nakit) d.Nakit += a; else if (k.IsCardType()) d.Kart += a; else d.Diger += a;
            }
        }

        // Satır bazlı ortak: kapanan siparişlerin geçerli satırları (parent, ikram değil, iptal değil)
        string oDate = $"CAST({L("o.ClosedAt")} AS DATE)";
        string lineNet = "((l.Qty - l.RefundedQty)*l.UnitPrice - l.DiscountAmount)";
        string lineFrom = $@"FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId
WHERE (@b=0 OR o.BranchId=@b) AND o.Status=1 AND {oDate} BETWEEN @f AND @t
  AND l.IsDeleted=0 AND l.LineStatus<>4 AND l.ParentLineId IS NULL AND l.IsTreat=0";

        // İskonto toplamı
        using (var cmd = c.CreateCommand())
        { cmd.CommandText = $"SELECT ISNULL(SUM(l.DiscountAmount),0) {lineFrom};"; P(cmd); d.Discount = (decimal)cmd.ExecuteScalar()!; }

        // İptal satır adedi (sipariş açılış tarihine göre)
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"SELECT COUNT(1) FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId
WHERE (@b=0 OR o.BranchId=@b) AND l.LineStatus=4 AND CAST({L("o.OpenedAt")} AS DATE) BETWEEN @f AND @t AND l.ParentLineId IS NULL;";
            P(cmd); d.CancelCount = (int)cmd.ExecuteScalar()!;
        }

        // Anlık açık masa (doluluk) — tarihten bağımsız
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT ot.TableId) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId WHERE o.Status=0 AND o.IsDeleted=0 AND (@b=0 OR o.BranchId=@b);";
            cmd.Parameters.AddWithValue("@b", branchId); d.OpenTables = (int)cmd.ExecuteScalar()!;
        }

        // Şube kırılımı (çok şubeli görünüm)
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT cf.BranchId, br.Name, ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0), COUNT(DISTINCT CASE WHEN cf.Total>0 THEN cf.OrderId END)
FROM dbo.ClFiches cf LEFT JOIN [{db}].dbo.CompanyBranches br ON br.Id=cf.BranchId
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY cf.BranchId, br.Name ORDER BY 3 DESC;";
            P(cmd);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                d.Branches.Add(new BranchStat { BranchId = r.GetInt32(0), Name = r.IsDBNull(1) ? $"Şube {r.GetInt32(0)}" : r.GetString(1), Revenue = r.GetDecimal(2), FisCount = r.GetInt32(3) });
        }
        // Şube anlık doluluk
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT o.BranchId, COUNT(DISTINCT ot.TableId) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId WHERE o.Status=0 AND o.IsDeleted=0 GROUP BY o.BranchId;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var bs = d.Branches.FirstOrDefault(x => x.BranchId == r.GetInt32(0)); if (bs != null) bs.OpenTables = r.GetInt32(1); }
        }

        // Saatlik yoğunluk (0-23)
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT DATEPART(HOUR, {L("cf.FicheDate")}), ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0), COUNT(DISTINCT cf.OrderId)
FROM dbo.ClFiches cf WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY DATEPART(HOUR, {L("cf.FicheDate")});";
            P(cmd);
            var map = new Dictionary<int, (decimal v, int c)>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) map[r.GetInt32(0)] = (r.GetDecimal(1), r.GetInt32(2));
            for (int h = 0; h < 24; h++)
            {
                map.TryGetValue(h, out var x);
                d.Hourly.Add(new TrendPoint { Label = $"{h:00}", Value = x.v, Count = x.c });
            }
            Bars(d.Hourly);
        }

        // Günlük ciro trendi
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT {cfDate}, ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0), COUNT(DISTINCT cf.OrderId)
FROM dbo.ClFiches cf WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY {cfDate} ORDER BY 1;";
            P(cmd);
            using var r = cmd.ExecuteReader();
            while (r.Read()) d.Daily.Add(new TrendPoint { Label = r.GetDateTime(0).ToString("dd.MM"), Value = r.GetDecimal(1), Count = r.GetInt32(2) });
            Bars(d.Daily);
        }

        // En çok satan ürünler
        d.TopProducts = Rank(c, P, $@"
SELECT TOP 10 pr.Name, ISNULL(SUM({lineNet}),0) amt, ISNULL(SUM(l.Qty - l.RefundedQty),0) qty
{lineFrom} GROUP BY pr.Name", joinProduct: true, byAmount: true);

        // En çok ciro yapan masalar
        d.TopTables = Rank(c, P, $@"
SELECT TOP 10 ISNULL(tb.Name, tb.Code) nm, ISNULL(SUM({lineNet}),0) amt, COUNT(DISTINCT o.Id) qty
{lineFrom} AND EXISTS (SELECT 1 FROM dbo.OrderTables ot WHERE ot.OrderId=o.Id)
  AND tb.Code IS NOT NULL GROUP BY ISNULL(tb.Name, tb.Code)", joinTable: true, byAmount: true);

        // En çok ciro yapan lokasyonlar
        d.TopZones = Rank(c, P, $@"
SELECT TOP 10 z.Name, ISNULL(SUM({lineNet}),0) amt, COUNT(DISTINCT o.Id) qty
{lineFrom} GROUP BY z.Name", joinZone: true, byAmount: true);

        // En çok iptal edilen ürünler
        d.Cancelled = Rank(c, P, $@"
SELECT TOP 10 pr.Name, 0 amt, COUNT(1) qty
FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId LEFT JOIN dbo.Products pr ON pr.Id=l.ProductId
WHERE (@b=0 OR o.BranchId=@b) AND l.LineStatus=4 AND l.ParentLineId IS NULL
  AND CAST({L("o.OpenedAt")} AS DATE) BETWEEN @f AND @t
GROUP BY pr.Name", byAmount: false);

        // En çok iade edilen ürünler
        d.Refunded = Rank(c, P, $@"
SELECT TOP 10 pr.Name, ISNULL(SUM(l.RefundedQty*l.UnitPrice),0) amt, ISNULL(SUM(l.RefundedQty),0) qty
FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId LEFT JOIN dbo.Products pr ON pr.Id=l.ProductId
WHERE (@b=0 OR o.BranchId=@b) AND l.RefundedQty>0 AND l.ParentLineId IS NULL
  AND CAST({L("o.ClosedAt")} AS DATE) BETWEEN @f AND @t
GROUP BY pr.Name", byAmount: true);

        // Garson performansı (sipariş açan)
        d.Waiters = Rank(c, P, $@"
SELECT TOP 10 ISNULL(u.FullName, 'Bilinmeyen'), ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0) amt, COUNT(DISTINCT cf.OrderId) qty
FROM dbo.ClFiches cf JOIN dbo.Orders o ON o.Id=cf.OrderId LEFT JOIN [{db}].dbo.Users u ON u.Id=o.CreatedBy
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY u.FullName", byAmount: true);

        // Kasiyer performansı (fiş kesen)
        d.Cashiers = Rank(c, P, $@"
SELECT TOP 10 ISNULL(u.FullName, 'Bilinmeyen'), ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0) amt, COUNT(DISTINCT cf.Id) qty
FROM dbo.ClFiches cf LEFT JOIN [{db}].dbo.Users u ON u.Id=cf.CreatedBy
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY u.FullName", byAmount: true);

        // Kampanya bazlı satış: kampanya uygulanmış adisyonların cirosu + adedi
        d.CampaignSales = Rank(c, P, $@"
SELECT TOP 20 cmp.Name, ISNULL(SUM(CASE WHEN cf.Total>0 THEN cf.Total ELSE 0 END),0) amt, COUNT(DISTINCT cf.OrderId) qty
FROM dbo.OrderCampaigns oc
JOIN dbo.Campaigns cmp ON cmp.Id=oc.CampaignId
JOIN dbo.ClFiches cf ON cf.OrderId=oc.OrderId
WHERE (@b=0 OR cf.BranchId=@b) AND {cfDate} BETWEEN @f AND @t
GROUP BY cmp.Name", byAmount: true);

        // Şu an geçerli (aktif) kampanyalar — şube seçiliyse o şubede geçerli olanlar
        var now = DateTime.Now;
        var allCamp = new CampaignService().GetAll(includeDeleted: false);
        d.ActiveCampaigns = branchId > 0
            ? allCamp.Where(x => x.IsValidNow(branchId, now)).ToList()
            : allCamp.Where(x => x.IsActive && now >= x.StartDateTime && now <= x.EndDateTime).ToList();

        return d;
    }

    private static List<RankRow> Rank(SqlConnection c, Action<SqlCommand> p, string sql,
        bool joinProduct = false, bool joinTable = false, bool joinZone = false, bool byAmount = true)
    {
        // ürün/masa/zone join'leri lineFrom içine eklenir
        if (joinProduct) sql = sql.Replace("WHERE (@b=0", "LEFT JOIN dbo.Products pr ON pr.Id=l.ProductId WHERE (@b=0");
        // Sipariş birden çok masaya bağlıysa LEFT JOIN satırları çoğaltıp ciroyu şişirir → OUTER APPLY ile sipariş başına TEK masa
        if (joinTable) sql = sql.Replace("WHERE (@b=0", "OUTER APPLY (SELECT TOP 1 t2.Name, t2.Code FROM dbo.OrderTables ot2 JOIN dbo.Tables t2 ON t2.Id=ot2.TableId WHERE ot2.OrderId=o.Id ORDER BY t2.Id) tb WHERE (@b=0");
        if (joinZone) sql = sql.Replace("WHERE (@b=0", "LEFT JOIN dbo.Zones z ON z.Id=o.ZoneId WHERE (@b=0");
        sql += byAmount ? " ORDER BY amt DESC;" : " ORDER BY qty DESC;";

        var list = new List<RankRow>();
        using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; p(cmd); using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new RankRow {
                Name = r.IsDBNull(0) ? "-" : r.GetString(0),
                Amount = r.IsDBNull(1) ? 0 : Convert.ToDecimal(r.GetValue(1)),
                Count = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)) }); }

        decimal maxA = list.Count > 0 ? list.Max(x => x.Amount) : 0;
        int maxC = list.Count > 0 ? list.Max(x => x.Count) : 0;
        for (int i = 0; i < list.Count; i++)
        {
            list[i].Color = Palette[i % Palette.Length];
            list[i].BarWidth = byAmount ? (maxA > 0 ? (double)(list[i].Amount / maxA) * 100 : 0)
                                        : (maxC > 0 ? (double)list[i].Count / maxC * 100 : 0);
        }
        return list;
    }

    private static void Bars(List<TrendPoint> pts)
    {
        decimal max = pts.Count > 0 ? pts.Max(x => x.Value) : 0;
        foreach (var p in pts) p.BarWidth = max > 0 ? (double)(p.Value / max) * 100 : 0;
    }
}
