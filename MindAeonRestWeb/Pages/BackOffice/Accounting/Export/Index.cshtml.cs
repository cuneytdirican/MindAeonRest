using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Accounting.Export;

/// <summary>
/// FAZ 4a Muhasebe Aktarım — tarih aralığı + biçim (Logo / Mikro / Genel) seç → cari hareketleri
/// (+ fatura KDV'si) harici muhasebe programına içe-aktarılabilir CSV olarak indir (UTF-8 BOM, ';' ayraç).
/// Salt-okunur. Gate: SysAdmin/Patron + Feature.PreAccounting (Ön Muhasebe ile aynı kapı).
/// </summary>
public class IndexModel : PageModel
{
    private readonly AccountingExportService _svc = new();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int Format { get; set; }
    [BindProperty(SupportsGet = true)] public bool Summary { get; set; }

    public List<AccountingExportRow> Rows { get; private set; } = new();
    public List<SelectListItem> FormatOptions { get; private set; } = new();
    public bool Searched { get; private set; }

    public int Count => Rows.Count;
    public decimal TotalDebt => Rows.Sum(r => r.Debt);
    public decimal TotalCredit => Rows.Sum(r => r.Credit);
    public decimal TotalKdv => Rows.Sum(r => r.Kdv);

    private static bool CanAccess =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadFormats();
        if (From is { } && To is { })
        {
            var (from, to) = Normalize();
            Rows = _svc.GetRows(from, to);
            Searched = true;
        }
        return Page();
    }

    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        if (From is null || To is null) return RedirectToPage();

        var (from, to) = Normalize();
        var rows = _svc.GetRows(from, to);
        var fmt = (AccountingExportFormat)Format;

        string csv = Summary
            ? AccountingExportService.BuildSummaryCsv(rows)
            : AccountingExportService.BuildCsv(rows, fmt);

        string tag = Summary ? "ozet" : fmt.ToString().ToLowerInvariant();
        return CsvFile(csv, $"muhasebe-{tag}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    private (DateTime from, DateTime to) Normalize()
    {
        var from = From!.Value;
        var to = To!.Value;
        if (to < from) (from, to) = (to, from);
        return (from, to);
    }

    private void LoadFormats()
        => FormatOptions = Enum.GetValues<AccountingExportFormat>()
            .Select(f => new SelectListItem(f.ToTr(), ((int)f).ToString()))
            .ToList();

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
