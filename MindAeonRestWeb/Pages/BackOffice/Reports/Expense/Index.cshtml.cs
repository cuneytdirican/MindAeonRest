using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.Expense;

/// <summary>
/// Gider Raporu — WPF GiderListWindow / ExpenseReport'un web karşılığı.
/// Dönemsel gider dökümü (tarih/şube/gider tipi/açıklama/ödeme/tutar) + dönem toplamı +
/// gider tipine göre kırılım (grup toplamları). Filtre: Şube + Başlangıç/Bitiş + Gider Tipi.
/// Salt-okunur; CSV indir + tarayıcı yazdır/PDF. İş mantığı ExpenseService'te; burada yalnız çağrı+sunum.
/// Gate: SysAdmin/Patron + Feature.PreAccounting (Giderler ekranı ile aynı).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ExpenseService _svc = new();
    private readonly ExpenseTypeService _typeSvc = new();
    private readonly BranchService _branchSvc = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ExpenseTypeId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> TypeOptions { get; } = new();
    public List<MindAeonRest.Models.Expense> Rows { get; private set; } = new();

    /// <summary>BranchId → şube adı (Expense modelinde şube adı yok; koddan çözeriz).</summary>
    public Dictionary<int, string> BranchNames { get; } = new();

    public int Count => Rows.Count;
    public decimal Total => Rows.Sum(x => x.Amount);

    /// <summary>Gider tipine göre kırılım (grup toplamı + adet), tutara göre azalan.</summary>
    public IEnumerable<(string Type, int Count, decimal Total)> ByType =>
        Rows.GroupBy(r => string.IsNullOrEmpty(r.ExpenseTypeName) ? "—" : r.ExpenseTypeName!)
            .Select(g => (g.Key, g.Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Item3);

    public string BranchName(int id) => BranchNames.TryGetValue(id, out var n) ? n : "—";

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting (Giderler ekranı ile aynı).
    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    /// <summary>Web'de Session.CurrentCompany.Id dolu değildir; Company.Id'yi koddan çözeriz (Branches deseni).</summary>
    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    private void LoadOptions()
    {
        var companyId = ResolveCompanyId();

        BranchOptions.Add((0, "Tüm Şubeler"));
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
            {
                BranchOptions.Add((b.Id, b.Name));
                BranchNames[b.Id] = b.Name;
            }

        TypeOptions.Add((0, "Tüm Gider Tipleri"));
        foreach (var t in _typeSvc.GetAll())
            TypeOptions.Add((t.Id, t.Name));
    }

    /// <summary>Filtreleri uygular (şube + tarih aralığı ExpenseService.GetList; gider tipi + şube bellekte).</summary>
    private List<MindAeonRest.Models.Expense> Query()
    {
        From ??= DateTime.Today.AddMonths(-1);
        To ??= DateTime.Today;
        var from = From.Value;
        var to = To.Value;
        if (to < from) (from, to) = (to, from);

        // İş mantığı ExpenseService'te — tarih aralığı orada süzülür.
        IEnumerable<MindAeonRest.Models.Expense> list = _svc.GetList(from, to);

        if (BranchId is int bf && bf > 0)
            list = list.Where(e => e.BranchId == bf);
        if (ExpenseTypeId is int tf && tf > 0)
            list = list.Where(e => e.ExpenseTypeId == tf);

        return list.ToList();
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadOptions();
        BranchId ??= Session.CurrentBranch?.Id ?? 0;
        Rows = Query();
        return Page();
    }

    /// <summary>CSV dışa aktarım — ekrandaki kolonlar + aynı filtreler. UTF-8 BOM + ';' ayraç (Türkçe Excel).</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        LoadOptions();
        var rows = Query();

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Tarih", "Şube", "Gider Tipi", "Personel", "Açıklama", "Ödeme", "Tutar");
        foreach (var e in rows)
            Line(e.ExpenseDate.ToString("dd.MM.yyyy HH:mm"), BranchName(e.BranchId), e.ExpenseTypeName,
                 e.PersonnelName, e.Description, e.PayText, e.Amount.ToString("N2"));

        // Kırılım (gider tipine göre) + dönem toplamı
        Line();
        Line("Gider Tipi", "Adet", "Toplam");
        foreach (var g in ByType)
            Line(g.Type, g.Count.ToString(), g.Total.ToString("N2"));
        Line("TOPLAM", Count.ToString(), Total.ToString("N2"));

        var from = From ?? DateTime.Today.AddMonths(-1);
        var to = To ?? DateTime.Today;
        return CsvFile(sb.ToString(), $"gider-raporu-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
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
