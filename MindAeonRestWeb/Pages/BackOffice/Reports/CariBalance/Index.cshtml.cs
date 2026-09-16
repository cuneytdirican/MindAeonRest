using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.CariBalance;

/// <summary>
/// Cari Bakiye Listesi — WPF ReportBuilders.CariBakiye'nin web karşılığı.
/// Tüm carilerin (kod/ünvan/tip/döviz/borç/alacak/bakiye) bakiye özeti + arama + borçlu/alacaklı filtre.
/// Salt-okunur; cari başına detay için Cari Hareket ekranına link. Web'de yazdır/PDF için window.print.
/// Gate: SysAdmin/Patron + Feature.PreAccounting.
/// </summary>
public class IndexModel : PageModel
{
    private readonly CariHareketService _svc = new();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>Bakiye filtresi: "" (tümü) | "debit" (borçlu, bakiye>0) | "credit" (alacaklı, bakiye&lt;0).</summary>
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "";

    public List<MindAeonRest.Models.CariBalance> Rows { get; private set; } = new();

    public int Count => Rows.Count;
    public decimal TotalDebt => Rows.Sum(r => r.Debt);
    public decimal TotalCredit => Rows.Sum(r => r.Credit);
    public decimal TotalBalance => Rows.Sum(r => r.Balance);

    /// <summary>Döviz bazında bakiye toplamı (farklı dövizler karıştırılmaz).</summary>
    public IEnumerable<(string Cur, decimal Balance)> BalanceByCurrency =>
        Rows.GroupBy(r => r.CurrencyCode ?? "—")
            .Select(g => (g.Key, g.Sum(x => x.Balance)))
            .OrderBy(x => x.Item1);

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting (CariHareket ekranı ile aynı).
    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        var from = From;
        var to = To;
        if (from is { } a && to is { } b && b < a) (from, to) = (to, from);

        var list = _svc.GetBalances(from, to);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            list = list.Where(c =>
                (!string.IsNullOrEmpty(c.Code) && c.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Name) && c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        list = Filter switch
        {
            "debit" => list.Where(c => c.Balance > 0).ToList(),
            "credit" => list.Where(c => c.Balance < 0).ToList(),
            _ => list
        };

        Rows = list;
        return Page();
    }

    /// <summary>CSV dışa aktarım — ekrandaki cari bakiye kolonlarını aynı GetBalances sorgusu +
    /// arama + borçlu/alacaklı filtre ile üretir. UTF-8 BOM + ';' ayraç (Türkçe Excel).</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();

        var from = From;
        var to = To;
        if (from is { } a && to is { } b && b < a) (from, to) = (to, from);

        var list = _svc.GetBalances(from, to);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            list = list.Where(c =>
                (!string.IsNullOrEmpty(c.Code) && c.Code.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Name) && c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        list = Filter switch
        {
            "debit" => list.Where(c => c.Balance > 0).ToList(),
            "credit" => list.Where(c => c.Balance < 0).ToList(),
            _ => list
        };

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Kod", "Cari", "Tip", "Döviz", "Borç", "Alacak", "Bakiye");
        foreach (var c in list)
            Line(c.Code, c.Name, c.TypeText, c.CurrencyCode,
                 c.Debt.ToString("N2"), c.Credit.ToString("N2"), c.Balance.ToString("N2"));

        return CsvFile(sb.ToString(), $"cari-bakiye-{DateTime.Today:yyyyMMdd}.csv");
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
