using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.MenuEngineering;

/// <summary>
/// Menü Analizi (Menü Mühendisliği) — satış + maliyet birleşimi: ürün matrisi (Yıldız/İnek/Bilmece/Köpek),
/// gün×saat ısı haritası ve basit talep tahmini. Salt-okuma.
/// Gate: SysAdmin/Patron + Feature.AdvancedReports. CSV: matris satırları.
/// </summary>
public class IndexModel : PageModel
{
    private readonly MenuEngineeringService _svc = new();
    private readonly BranchService _branchSvc = new();

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public MenuEngineeringReport Report { get; private set; } = new();
    public SalesHeatmap Heatmap { get; private set; } = new();
    public SalesForecast Forecast { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.AdvancedReports);

    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    private int LoadFiltersAndResolveBranch()
    {
        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        From ??= DateTime.Today.AddDays(-29);
        To ??= DateTime.Today;
        // 0 = tüm şubeler seçeneğini destekle: BranchId null ise oturum şubesi, o da yoksa 0 (tümü).
        BranchId ??= Session.CurrentBranch?.Id ?? 0;
        return BranchId.Value;
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        int branchId = LoadFiltersAndResolveBranch();

        var from = From!.Value;
        var to = To!.Value;
        if (to < from) (from, to) = (to, from);

        Report = _svc.GetMenuEngineering(from, to, branchId);
        Heatmap = _svc.GetHeatmap(from, to, branchId);
        Forecast = _svc.GetForecast(branchId);
        return Page();
    }

    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        int branchId = LoadFiltersAndResolveBranch();

        var from = From!.Value;
        var to = To!.Value;
        if (to < from) (from, to) = (to, from);

        Report = _svc.GetMenuEngineering(from, to, branchId);

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");
        Line("Ürün", "Adet", "Ciro", "Maliyet", "Katkı Marjı", "Marj %", "Popülerlik %", "Sınıf");
        foreach (var i in Report.Items)
            Line(i.Name, i.Qty.ToString("0.##"), i.Revenue.ToString("N2"), i.TotalCost.ToString("N2"),
                 i.ContributionMargin.ToString("N2"), i.MarginPercent.ToString("N1"),
                 i.PopularityShare.ToString("N1"), i.ClassLabel);

        return CsvFile(sb.ToString(), $"menu-analizi-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
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
