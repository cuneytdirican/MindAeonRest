using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Şube özet (dashboard) verisi.</summary>
public class DashboardService
{
    private static readonly string[] Palette = { "#37475A", "#E8505B", "#C9CDD6", "#9AA7B5", "#6C7A89", "#F0B86E", "#7FB069" };

    public DashboardData Get(int branchId, DateTime date)
    {
        var d = date.Date;
        var data = new DashboardData();
        using var c = Database.OpenCompany();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT ISNULL(SUM(Total),0), COUNT(DISTINCT OrderId) FROM dbo.ClFiches WHERE BranchId=@b AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), FicheDate) AS DATE)=@d;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { data.SalesTotal = r.GetDecimal(0); data.AdisyonCount = r.GetInt32(1); }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT pl.Kind, SUM(pl.Amount) FROM dbo.PaymentLines pl JOIN dbo.ClFiches cf ON cf.Id=pl.ClFicheId
WHERE cf.BranchId=@b AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), cf.FicheDate) AS DATE)=@d GROUP BY pl.Kind;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kind = (PaymentKind)r.GetInt32(0); decimal amt = r.GetDecimal(1);
                if (kind == PaymentKind.Nakit) data.Nakit += amt;
                else if (kind.IsCardType()) data.Kart += amt;
                else data.Diger += amt;
            }
        }
        data.Average = data.AdisyonCount > 0 ? Math.Round(data.SalesTotal / data.AdisyonCount, 2) : 0;
        data.KasaTotal = new CashMovementService().GetBalances(branchId).Sum(x => x.Balance);

        // ERP KPI'ları (varsayılan döviz — farklı dövizler karıştırılmaz)
        try
        {
            int defCur = new CurrencyService().GetDefaultId() ?? 0;
            var bals = new CariHareketService().GetBalances().Where(x => x.CurrencyId == defCur).ToList();
            data.Receivable = bals.Where(x => x.Balance > 0).Sum(x => x.Balance);
            data.Payable = Math.Abs(bals.Where(x => x.Balance < 0).Sum(x => x.Balance));
            data.BankTotal = new BankMovementService().GetBalances().Where(x => x.CurrencyId == defCur).Sum(x => x.Balance);
            data.TodayExpense = new ExpenseService().GetList(d, d).Sum(x => x.Amount);
        }
        catch { /* ERP tabloları yoksa atla */ }

        // Yükümlülükler (parasal açık takibi): dolaşımdaki hediye çeki + puan
        try { data.GiftCardLiability = new GiftCardService().GetTotalLiability(); } catch { }
        try { data.LoyaltyLiability = new LoyaltyService().GetTotalLiability(); } catch { }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT TOP 6 p.Name, SUM((l.Qty - l.RefundedQty)*l.UnitPrice - l.DiscountAmount) AS Tutar
FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId LEFT JOIN dbo.Products p ON p.Id=l.ProductId
WHERE o.BranchId=@b AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), o.ClosedAt) AS DATE)=@d AND l.IsDeleted=0 AND l.LineStatus<>4
  AND l.ParentLineId IS NULL AND l.IsTreat=0
GROUP BY p.Name ORDER BY Tutar DESC;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d);
            using var r = cmd.ExecuteReader();
            int i = 0;
            while (r.Read())
                data.TopProducts.Add(new TopProduct { Name = r.IsDBNull(0) ? "-" : r.GetString(0), Amount = r.GetDecimal(1), Color = Palette[i % Palette.Length] });
            // i increment handled below after read loop
        }
        decimal topTotal = data.TopProducts.Sum(t => t.Amount);
        for (int i = 0; i < data.TopProducts.Count; i++)
        {
            data.TopProducts[i].Color = Palette[i % Palette.Length];
            data.TopProducts[i].Percent = topTotal > 0 ? (double)(data.TopProducts[i].Amount / topTotal) * 100 : 0;
        }
        return data;
    }
}
