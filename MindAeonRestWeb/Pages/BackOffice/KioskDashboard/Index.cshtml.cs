using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.KioskDashboard;

/// <summary>
/// KIOSK Kasa Paneli — WPF <c>KioskDashboardWindow</c>'un web karşılığı.
/// Şube + KIOSK filtresi + zaman kapsamı (bugün / son Z'den beri / tarih aralığı)
/// → tipe göre tahsil edilen para (nakit, kredi kartı, yemek çeki, açık hesap) + toplam tahsilat.
/// WPF'te 1 dakikalık otomatik yenileme var → web'de sayfa 60 sn'de bir kendini yeniler.
/// Salt-okunur. Gate: SysAdmin/Patron + Feature.SelfService (KIOSK özelliği).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReportService _reports = new();
    private readonly BranchService _branchSvc = new();
    private readonly KioskService _kioskSvc = new();

    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? KioskNo { get; set; }
    // Zaman kapsamı: 0 = Bugün (anlık), 1 = Son Z'den beri, 2 = Tarih aralığı  (WPF ScopeCombo ile birebir)
    [BindProperty(SupportsGet = true)] public int Scope { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> KioskOptions { get; } = new();
    public List<(int Value, string Text)> ScopeOptions { get; } = new()
    {
        (0, "Bugün (anlık)"),
        (1, "Son Z'den beri"),
        (2, "Tarih aralığı"),
    };

    // ── Sonuç ──
    public KioskMoneySnapshot? Snap { get; private set; }
    public string ScopeInfo { get; private set; } = "";
    public string? LastZText { get; private set; }
    public string? Error { get; private set; }

    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.SelfService);

    /// <summary>Web'de Session.CurrentCompany.Id dolu değildir; Company.Id'yi koddan çözeriz (Reports/Tables deseni).</summary>
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

        // Şube listesi — WPF gibi "Tüm Şubeler" (0) seçeneğiyle.
        BranchOptions.Add((0, "Tüm Şubeler"));
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        // KIOSK listesi — WPF gibi "Tüm KIOSK'lar" (0) + benzersiz No'lar.
        KioskOptions.Add((0, "Tüm KIOSK'lar"));
        var nos = _kioskSvc.GetAll().Select(k => k.No).Distinct().OrderBy(n => n).ToList();
        foreach (var n in nos)
            KioskOptions.Add((n, $"#{n}"));

        // Varsayılanlar: oturum şubesi (yoksa "Tümü"), tüm KIOSK, bugün; tarih aralığı için dün→bugün.
        BranchId ??= Session.CurrentBranch?.Id ?? 0;
        KioskNo ??= 0;
        From ??= DateTime.Today.AddDays(-1);
        To ??= DateTime.Today;

        int? branchFilter = BranchId is int bf && bf > 0 ? bf : null;
        int? kioskFilter = KioskNo is int kf && kf > 0 ? kf : null;

        try
        {
            DateTime from = DateTime.Today, to = DateTime.Today;
            DateTime? since = null;
            string info;

            if (Scope == 1)
            {
                var lastZ = _reports.GetLastZTakenAt(branchFilter, kioskFilter);
                if (lastZ is DateTime z)
                {
                    since = z;
                    info = $"Kapsam: Son Z'den beri ({z.ToLocalTime():dd.MM.yyyy HH:mm} sonrası)";
                }
                else
                {
                    from = to = DateTime.Today;
                    info = "Kapsam: Henüz Z alınmamış → bugünün tahsilatı gösteriliyor.";
                }
            }
            else if (Scope == 2)
            {
                from = (From ?? DateTime.Today).Date;
                to = (To ?? DateTime.Today).Date;
                if (to < from) (from, to) = (to, from);
                info = $"Kapsam: {from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
            }
            else
            {
                from = to = DateTime.Today;
                info = "Kapsam: Bugün (anlık)";
            }

            string branchText = BranchOptions.FirstOrDefault(o => o.Value == (BranchId ?? 0)).Text;
            string kioskText = KioskOptions.FirstOrDefault(o => o.Value == (KioskNo ?? 0)).Text;
            ScopeInfo = $"{branchText} · {kioskText}  ·  {info}";

            Snap = _reports.GetKioskMoney(branchFilter, kioskFilter, from, to, since);
            LastZText = Snap.LastZAt is DateTime lz ? lz.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }
}
