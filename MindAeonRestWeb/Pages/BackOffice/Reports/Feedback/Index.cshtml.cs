using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.Feedback;

/// <summary>FAZ 5d — Geri Bildirim raporu (RAPOR deseni: filtre → sonuç). Şube + tarih aralığı seç → puan/yorum
/// listesi + ortalama puan. SALT-OKUNUR. Yüksek puanlar (huni) Google'a yönlendirildiğinde işaretlenir.</summary>
public class IndexModel : PageModel
{
    private readonly FeedbackService _feedback = new();
    private readonly BranchService _branch = new();

    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }   // 0 = tüm şubeler
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();

    public FeedbackStats Stats { get; private set; } = new();
    public string? SubTitle { get; private set; }
    public string? Error { get; private set; }

    // Core rapor (lisans kapısı yok) — ZList deseni: Patron/SysAdmin ya da Reports izni.
    private static bool CanAccess => Session.IsTenantAdmin || Permissions.Reports;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        return Page();
    }

    private void Prepare()
    {
        var co = Session.CurrentCompany;
        var branches = co is null ? new List<CompanyBranch>() : _branch.GetByCompany(co.Id).Where(b => b.IsActive).ToList();
        BranchOptions = new List<SelectListItem> { new("Tüm Şubeler", "0") };
        BranchOptions.AddRange(branches.Select(b => new SelectListItem(b.Name, b.Id.ToString())));

        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;
    }

    private void Build()
    {
        try
        {
            var from = (From ?? DateTime.Today).Date;
            var to = (To ?? DateTime.Today).Date;
            if (to < from) (from, to) = (to, from);
            int? branchId = BranchId > 0 ? BranchId : null;

            // CreatedAt UTC saklanır → yerel gün aralığını UTC'ye çevirerek süz.
            var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
            var toUtc = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            Stats = _feedback.GetStats(branchId, fromUtc, toUtc);

            var branchText = branchId is int b
                ? BranchOptions.FirstOrDefault(o => o.Value == b.ToString())?.Text ?? $"Şube {b}"
                : "Tüm Şubeler";
            SubTitle = $"{branchText} · {from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public string BranchName(int id) => BranchOptions.FirstOrDefault(o => o.Value == id.ToString())?.Text ?? $"Şube {id}";

    /// <summary>CSV dışa aktarım — ekrandaki geri bildirim listesi (UTF-8 BOM + ';' ayraç, Türkçe Excel).</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Tarih", "Şube", "Puan", "Yorum", "Telefon", "Adisyon", "Google");
        foreach (var f in Stats.Items)
            Line(f.CreatedAtText, BranchName(f.BranchId), f.Rating.ToString(), f.Comment,
                 f.CustomerPhone, f.OrderId?.ToString(), f.GoogleRedirected ? "Evet" : "Hayır");

        var from = (From ?? DateTime.Today).Date;
        var to = (To ?? DateTime.Today).Date;
        return CsvFile(sb.ToString(), $"geri-bildirim-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

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
