using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice;

/// <summary>Yönetim açılış ekranı = WPF BossDashboardView birebir karşılığı (KPI + trend + top listeler + aktif kampanyalar),
/// altında hızlı erişim modül kutucukları (artık gerçek linkler).</summary>
public class IndexModel : PageModel
{
    public string Company => Session.CurrentCompany?.Code ?? "";
    // POS'a Git: doğrudan /app'e DEĞİL, SSO devir sayfasına gider → mevcut web oturumundan taze JWT üretilir,
    // PWA eski (başka kullanıcıya ait olabilecek) oturumu atıp bu kimlikle açılır. Çift login + yanlış-kimlik biter.
    // mode = role bazlı açılış ekranı (patron/kasiyer/mutfak/garson) — yönetici yine 🔀 ile değiştirebilir.
    public string PosUrl => "/PosHandoff?mode=" + PosMode;
    private string PosMode =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) ? "patron"
        : Session.HasRole("Mutfak") ? "kitchen"
        : Session.HasRole("Kasiyer") ? "cashier"
        : "waiter";
    public bool Has(Feature f) => Session.LicenseHas(f);

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }

    // Sadece patron/yönetici zengin özeti görür; diğer kullanıcılar yalnız hızlı erişim kutucuklarını görür.
    public bool ShowDashboard { get; private set; }
    public BossDashboard? Data { get; private set; }
    public decimal GiftLiability { get; private set; }
    public decimal LoyaltyLiability { get; private set; }
    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public string FromText => From ?? DateTime.Today.ToString("yyyy-MM-dd");
    public string ToText => To ?? DateTime.Today.ToString("yyyy-MM-dd");

    public void OnGet()
    {
        ShowDashboard = Session.IsSysAdmin || Session.HasRole("Patron");
        if (!ShowDashboard) return;

        int companyId = ResolveCompanyId();
        BranchOptions = new() { new SelectListItem("Tüm Şubeler", "0") };
        if (companyId > 0)
            BranchOptions.AddRange(new BranchService().GetByCompany(companyId)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));

        var from = TryDate(From) ?? DateTime.Today;
        var to = TryDate(To) ?? DateTime.Today;
        if (to < from) (from, to) = (to, from);
        // Gün sonuna kadar dahil et.
        var toInclusive = to.Date.AddDays(1).AddSeconds(-1);

        try { Data = new BossDashboardService().Get(from.Date, toInclusive, BranchId); }
        catch { Data = new BossDashboard(); }
        try { GiftLiability = new GiftCardService().GetTotalLiability(); } catch { }
        try { LoyaltyLiability = new LoyaltyService().GetTotalLiability(); } catch { }
    }

    private static int ResolveCompanyId()
    {
        var co = Session.CurrentCompany;
        if (co is null) return 0;
        if (co.Id > 0) return co.Id;
        try { return new CompanyService().GetByCode(co.Code)?.Id ?? 0; } catch { return 0; }
    }

    private static DateTime? TryDate(string? s)
        => DateTime.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
