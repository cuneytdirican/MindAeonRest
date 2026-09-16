using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Hesap fişi (account/check) içeriğini adisyondan üretir.</summary>
public class ReceiptService
{
    private readonly OrderService _orders = new();
    private readonly CompanyService _companies = new();
    private readonly BranchService _branches = new();

    public ReceiptData BuildAccountReceipt(Order order) => BuildAccountReceipt(order, includeSettled: false);

    /// <summary>Hesap fişi üretir. <paramref name="includeSettled"/>=true ise ödenmiş (settled) satırlar da
    /// dahil edilir — ödeme tamamlandıktan sonra "ödendi" fişi basabilmek için.</summary>
    public ReceiptData BuildAccountReceipt(Order order, bool includeSettled)
    {
        var company = Session.CurrentCompany;
        var (color, mono) = company is null ? (null, null) : _companies.GetLogos(company.Id);
        var branch = _branches.GetById(order.BranchId);

        var data = new ReceiptData
        {
            Logo = mono ?? color,   // termal için mono önce
            CompanyName = company?.Name ?? "",
            BranchName = branch?.Name ?? Session.CurrentBranch?.Name ?? "",
            BranchAddress = branch?.Address,
            BranchTax = branch is null ? null : string.Join("  ", new[] { branch.TaxOffice, branch.TaxNumber }.Where(s => !string.IsNullOrWhiteSpace(s))),
            BranchPhone = branch?.Phone,
            OrderCode = order.Code,
            TableCodes = order.TableCodes,
            CashierName = Session.CurrentUser?.FullName ?? "",
            Date = DateTime.Now,
            Footer = "Bizi tercih ettiğiniz için teşekkürler."
        };

        // Ödenmemiş satırlar (hesap = müşterinin borcu). includeSettled=true ise ödenmiş satırlar da girer.
        var lines = _orders.GetLines(order.Id)
            .Where(l => l.LineStatus != OrderLineStatus.Iptal && (includeSettled || l.SettledClFicheId == null)).ToList();

        foreach (var l in lines)
            data.Items.Add(new ReceiptItem { Name = l.ProductName, Qty = l.Qty, Amount = l.LineTotal, IsComponent = l.IsComponent });

        // KDV kırılımı (üst satırlar, fiyat KDV dahil)
        foreach (var g in lines.Where(l => !l.IsComponent && !l.IsTreat).GroupBy(l => l.TaxRate))
        {
            decimal gross = g.Sum(x => x.Qty * x.UnitPrice - x.DiscountAmount);
            decimal matrah = Math.Round(gross / (1 + g.Key / 100m), 2);
            data.Kdv.Add(new ReceiptKdv { Rate = g.Key, Matrah = matrah, Tax = gross - matrah });
        }
        data.Total = lines.Where(l => !l.IsComponent && !l.IsTreat).Sum(l => l.Qty * l.UnitPrice - l.DiscountAmount);
        return data;
    }
}
