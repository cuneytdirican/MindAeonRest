using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.ZList;

/// <summary>Z (Gün Sonu) Raporları listesi — RAPOR deseni (filtre → sonuç tablo).
/// WPF <c>ZReportListWindow</c> birebir karşılığı: şube + tarih aralığı seç → GetZReportList;
/// alınmış Z raporlarını (tarih/şube/KIOSK/fiş/ciro/alınma/alan) SALT-OKUNUR listeler.
/// Z burada ALINMAZ (o iş X/Z sayfasında); bu ekran yalnız geçmiş Z'leri gösterir.</summary>
public class IndexModel : PageModel
{
    private readonly ReportService _reports = new();
    private readonly BranchService _branch = new();
    private readonly KioskService _kiosk = new();

    // ── Filtre ── (WPF: 0 = "Tüm Şubeler", From = bugün-30, To = bugün)
    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int KioskNo { get; set; }   // 0 = tüm KIOSK'lar
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> KioskOptions { get; private set; } = new();

    // ── Sonuç ──
    public List<ZReportRow> Rows { get; private set; } = new();
    public decimal TotalSales => Rows.Sum(z => z.SalesTotal);
    public int TotalFis => Rows.Sum(z => z.FisCount);
    public string? SubTitle { get; private set; }

    public string? Error { get; private set; }

    // Core özellik (lisans kapısı yok) — WPF ZReportListWindow Permissions.Reports.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron") || Permissions.Reports;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        return Page();
    }

    /// <summary>Filtre varsayılanları + şube seçenekleri (0 = Tüm Şubeler; WPF ile aynı).</summary>
    private void Prepare()
    {
        var co = Session.CurrentCompany;
        var branches = co is null ? new List<CompanyBranch>() : _branch.GetByCompany(co.Id).Where(b => b.IsActive).ToList();
        BranchOptions = new List<SelectListItem> { new("Tüm Şubeler", "0") };
        BranchOptions.AddRange(branches.Select(b => new SelectListItem(b.Name, b.Id.ToString())));

        // KIOSK seçenekleri: seçili şube varsa o şubenin KIOSK'ları, yoksa tüm KIOSK No'lar (No'ya göre tekilleştir).
        var kiosks = _kiosk.GetAll(false);
        if (BranchId > 0) kiosks = kiosks.Where(k => k.BranchId == BranchId).ToList();
        KioskOptions = new List<SelectListItem> { new("Tüm KIOSK'lar", "0") };
        KioskOptions.AddRange(kiosks
            .GroupBy(k => k.No).OrderBy(g => g.Key)
            .Select(g => new SelectListItem($"#{g.Key} · {g.First().Name}", g.Key.ToString())));

        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;
    }

    /// <summary>Z listesini üret (tarih aralığı normalize + tüm-KIOSK). Şube 0 = tümü.</summary>
    private void Build()
    {
        try
        {
            var from = (From ?? DateTime.Today).Date;
            var to = (To ?? DateTime.Today).Date;
            if (to < from) (from, to) = (to, from);
            int? branchId = BranchId > 0 ? BranchId : null;
            int? kioskNo = KioskNo > 0 ? KioskNo : null;

            Rows = _reports.GetZReportList(branchId, kioskNo, from, to);

            var branchText = branchId is int b
                ? BranchOptions.FirstOrDefault(o => o.Value == b.ToString())?.Text ?? $"Şube {b}"
                : "Tüm Şubeler";
            var kioskText = kioskNo is int k ? $"KIOSK #{k}" : "Tüm KIOSK'lar";
            SubTitle = $"{branchText} · {kioskText} · {from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    /// <summary>CSV dışa aktarım — ekrandaki Z listesi kolonlarını aynı GetZReportList sorgusuyla üretir.
    /// UTF-8 BOM + ';' ayraç (Türkçe Excel). Filtreler querystring ile taşınır.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Tarih", "Şube", "KIOSK", "Fiş Adedi", "Satış Toplamı", "Alınma", "Alan");
        foreach (var z in Rows)
            Line(z.DateText, z.BranchName, z.KioskText, z.FisCount.ToString(),
                 z.SalesTotal.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")), z.TakenAtText, z.TakenByName);

        var from = (From ?? DateTime.Today).Date;
        var to = (To ?? DateTime.Today).Date;
        return CsvFile(sb.ToString(), $"z-raporlari-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
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

    public static string Money(decimal v) => "₺" + v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
