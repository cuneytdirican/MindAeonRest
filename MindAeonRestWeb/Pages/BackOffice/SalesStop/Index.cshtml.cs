using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SalesStop;

/// <summary>
/// Şube bazlı ürün satış durdurma/açma ekranı. WPF MainWindow Page.SalesStop ile birebir:
/// okuma ProductBranchSettingsService.GetSalesStatus(branchId), yazma
/// ProductBranchSettingsService.SetSalesStopped(ids, branchId, stopped, userId).
/// WPF aktif şubeyi (Session.CurrentBranch) kullanır; web tek şube oturumuna bağlı olmadığından
/// şirketin şubeleri arasından bir şube seçtirir (BranchService.GetByCompany).
/// </summary>
public class IndexModel : PageModel
{
    private readonly BranchService _branchSvc = new();
    private readonly ProductBranchSettingsService _settingsSvc = new();

    public List<CompanyBranch> Branches { get; private set; } = new();
    public List<ProductSalesStatus> Items { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Load();
        return Page();
    }

    /// <summary>Tek ürünün satış durumunu bu şube için değiştirir (WPF SetSalesStopped ile aynı servis/metot).</summary>
    public IActionResult OnPostToggle(int productId, bool stopped)
    {
        if (!CanAccess) return Forbid();
        if (BranchId <= 0) return RedirectToPage(new { BranchId, Q });
        _settingsSvc.SetSalesStopped(new[] { productId }, BranchId, stopped, Session.CurrentUserId);
        return RedirectToPage(new { BranchId, Q });
    }

    private void Load()
    {
        int companyId = Session.CurrentCompany?.Id ?? 0;
        Branches = companyId == 0 ? new() : _branchSvc.GetByCompany(companyId);

        // Seçili şube yoksa ilk şubeye düş.
        if (BranchId <= 0 && Branches.Count > 0) BranchId = Branches[0].Id;

        if (BranchId <= 0) { Items = new(); return; }

        var all = _settingsSvc.GetSalesStatus(BranchId);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(p => (p.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (p.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        Items = all;
    }
}
