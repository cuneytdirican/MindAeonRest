using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.Personnel.Attendance;

/// <summary>
/// FAZ 4c — Puantaj / Mesai raporu (web). Dönemsel time-clock dökümü (personel · giriş · çıkış · saat) +
/// personel bazında labor-cost (Salary'den türetilen saatlik ücret × saat) + dönem cirosuna oran (%).
/// Salt-okunur; CSV indir + tarayıcı yazdır/PDF. İş mantığı AttendanceService/LaborMath'te; burada yalnız çağrı+sunum.
/// Gate: SysAdmin/Patron + Feature.PreAccounting (Personel ekranı ile aynı).
/// </summary>
public class IndexModel : PageModel
{
    private readonly AttendanceService _svc = new();
    private readonly PersonnelService _personnel = new();
    private readonly BranchService _branchSvc = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? PersonnelId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    /// <summary>Aylık maaştan saatlik ücret türetmede kullanılan standart aylık saat (boş → varsayılan).</summary>
    [BindProperty(SupportsGet = true)] public string? StdHours { get; set; }

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> PersonnelOptions { get; } = new();

    public List<MindAeonRest.Models.Attendance> Timesheet { get; private set; } = new();
    public LaborCostReport? Report { get; private set; }

    // Ön Muhasebe kapısı — Personel ekranı ile aynı (SysAdmin/Patron + Feature.PreAccounting).
    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    private void LoadOptions()
    {
        var companyId = ResolveCompanyId();
        BranchOptions.Clear();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        PersonnelOptions.Clear();
        PersonnelOptions.Add((0, "Tüm Personel"));
        foreach (var p in _personnel.GetAll().Where(p => p.IsActive))
            PersonnelOptions.Add((p.Id, $"{p.Code} · {p.FullName}"));
    }

    /// <summary>Standart aylık saat: kültürden bağımsız parse (Num); geçersiz/0 → LaborMath varsayılanı.</summary>
    private decimal StandardMonthlyHours =>
        Num.TryParseDecimal(StdHours, out var v) && v > 0 ? v : LaborMath.DefaultMonthlyHours;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadOptions();

        BranchId ??= Session.CurrentBranch?.Id ?? (BranchOptions.Count > 0 ? BranchOptions[0].Value : 0);
        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;
        var from = From.Value; var to = To.Value;
        if (to < from) (from, to) = (to, from);

        if (BranchId is int b && b > 0)
        {
            int? pid = PersonnelId is int p && p > 0 ? p : null;
            Timesheet = _svc.GetTimesheet(b, from, to, pid);
            Report = _svc.GetLaborCost(b, from, to, StandardMonthlyHours);
        }
        return Page();
    }

    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        LoadOptions();

        var from = From ?? DateTime.Today.AddDays(-30);
        var to = To ?? DateTime.Today;
        if (to < from) (from, to) = (to, from);
        if (BranchId is not int b || b <= 0) return RedirectToPage();

        int? pid = PersonnelId is int p && p > 0 ? p : null;
        var ts = _svc.GetTimesheet(b, from, to, pid);
        var rep = _svc.GetLaborCost(b, from, to, StandardMonthlyHours);

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Puantaj (Mesai Dökümü)");
        Line("Kod", "Personel", "Giriş", "Çıkış", "Saat", "Not");
        foreach (var a in ts)
            Line(a.PersonnelCode, a.PersonnelName, a.ClockInText,
                 a.IsOpen ? "" : a.ClockOutText, a.IsOpen ? "" : a.Hours.ToString("N2"), a.Note);

        Line();
        Line("Labor-Cost (İşçilik Maliyeti)", $"Standart Aylık Saat: {rep.StandardMonthlyHours:N0}");
        Line("Kod", "Personel", "Maaş", "Mesai", "Saat", "Saatlik Ücret", "İşçilik");
        foreach (var r in rep.Rows)
            Line(r.PersonnelCode, r.PersonnelName, r.Salary.ToString("N2"), r.Shifts.ToString(),
                 r.Hours.ToString("N2"), r.HourlyRate.ToString("N2"), r.LaborCost.ToString("N2"));
        Line("TOPLAM", "", "", "", rep.TotalHours.ToString("N2"), "", rep.TotalLaborCost.ToString("N2"));
        Line("Dönem Cirosu", rep.Revenue.ToString("N2"), "Labor-Cost %", rep.LaborCostPercent.ToString("N2"));

        return CsvFile(sb.ToString(), $"puantaj-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    // BULGU 11: CSV/formül enjeksiyonu nötrleştirme + ayraç kaçışı ortak yardımcıda (CsvHelper.Field).
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
