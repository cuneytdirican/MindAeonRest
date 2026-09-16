using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.Fiche;

/// <summary>
/// Fiş / Adisyon Raporu — WPF FicheReportWindow'un web karşılığı.
/// Filtre: Şube + Başlangıç/Bitiş tarih + arama (ürün/fiş/garson). Sekmeler: Fişler / İptaller.
/// Export: WPF'e özel (Excel/PDF/CSV). Web'de window.print yeterli — cshtml'de yazdır butonu.
/// Gate: SysAdmin/Patron + Feature.AdvancedReports.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReportService _reports = new();
    private readonly BranchService _branchSvc = new();

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<FicheReportRow> Fiches { get; private set; } = new();
    public List<CancelReportRow> Cancels { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "fiches";

    // Fişler özet
    public int FicheCount => Fiches.Count;
    public decimal Ciro => Fiches.Sum(f => f.Total);
    public decimal ToplamIndirim => Fiches.Sum(f => f.Discount);
    public decimal ToplamIkram => Fiches.Sum(f => f.Treat);

    // İptaller özet
    public int CancelCount => Cancels.Count;
    public decimal ToplamIptal => Cancels.Sum(c => c.Amount);
    public int FireCount => Cancels.Count(c => c.Disposition == "Fire");
    public int IadeCount => Cancels.Count(c => c.Disposition == "Stoğa İade");

    public bool CancelsTab => string.Equals(Tab, "cancels", StringComparison.OrdinalIgnoreCase);

    private static bool CanAccess =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.AdvancedReports);

    /// <summary>Web'de Session.CurrentCompany.Id dolu değildir; Company.Id'yi koddan çözeriz (Branches deseni).</summary>
    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        // Varsayılanlar: son 7 gün, oturum şubesi / ilk şube.
        From ??= DateTime.Today.AddDays(-7);
        To ??= DateTime.Today;
        BranchId ??= Session.CurrentBranch?.Id ?? BranchOptions.FirstOrDefault().Value;

        if (BranchId is not int branchId || branchId == 0)
            return Page(); // şube yok/seçilmedi → boş liste

        var from = From.Value;
        var to = To.Value;
        if (to < from) (from, to) = (to, from);

        Fiches = _reports.GetFicheReport(branchId, from, to);
        Cancels = _reports.GetCancellationReport(branchId, from, to);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            Fiches = Fiches.Where(f => Match(q, f.FicheCode, f.OrderCode, f.WaiterName, f.CashierName, f.CustomerName, f.TableCodes)).ToList();
            Cancels = Cancels.Where(c => Match(q, c.OrderCode, c.ProductName, c.Reason, c.CancelledByName, c.TableCodes)).ToList();
        }

        return Page();
    }

    private static bool Match(string q, params string?[] fields) =>
        fields.Any(f => !string.IsNullOrEmpty(f) && f.Contains(q, StringComparison.OrdinalIgnoreCase));

    /// <summary>CSV dışa aktarım — aktif sekmenin (Fişler / İptaller) ekrandaki kolonlarını
    /// aynı GetFicheReport / GetCancellationReport sorgusu + arama ile üretir.
    /// UTF-8 BOM + ';' ayraç (Türkçe Excel). Filtreler querystring ile taşınır.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();

        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        From ??= DateTime.Today.AddDays(-7);
        To ??= DateTime.Today;
        BranchId ??= Session.CurrentBranch?.Id ?? BranchOptions.FirstOrDefault().Value;

        if (BranchId is not int branchId || branchId == 0) return NotFound();

        var from = From.Value;
        var to = To.Value;
        if (to < from) (from, to) = (to, from);

        Fiches = _reports.GetFicheReport(branchId, from, to);
        Cancels = _reports.GetCancellationReport(branchId, from, to);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            Fiches = Fiches.Where(f => Match(q, f.FicheCode, f.OrderCode, f.WaiterName, f.CashierName, f.CustomerName, f.TableCodes)).ToList();
            Cancels = Cancels.Where(c => Match(q, c.OrderCode, c.ProductName, c.Reason, c.CancelledByName, c.TableCodes)).ToList();
        }

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        string name;
        if (CancelsTab)
        {
            name = "iptaller";
            Line("Tarih", "Adisyon", "Masa", "Ürün", "Miktar", "Tutar", "Sebep", "İptal Eden", "Akıbet");
            foreach (var c in Cancels)
                Line(c.DateText, c.OrderCode, c.TableCodes, c.ProductName, c.QtyText, c.AmountText, c.Reason, c.CancelledByName, c.Disposition);
        }
        else
        {
            name = "fisler";
            Line("Fiş No", "Adisyon", "Tarih", "Şube", "Lokasyon", "Masa", "Garson", "Kasiyer", "Müşteri", "İndirim", "İkram", "Ödeme", "Tutar", "Durum");
            foreach (var f in Fiches)
                Line(f.FicheCode, f.OrderCode, f.DateText, f.BranchName, f.ZoneName, f.TableCodes, f.WaiterName, f.CashierName, f.CustomerName, f.DiscountText, f.TreatText, f.Payments, f.TotalText, f.StatusText);
        }

        return CsvFile(sb.ToString(), $"{name}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    // ── CSV yardımcıları (';' ayraç, tırnak-kaçış, UTF-8 BOM) ──
    private static string Esc(string? s) => MindAeonRest.Helpers.CsvHelper.Field(s);

    private FileContentResult CsvFile(string csv, string fileName)
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes(csv);
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
