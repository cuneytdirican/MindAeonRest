using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.PriceTransfer;

/// <summary>
/// Şubeler arası fiyat aktarımı (WPF PriceTransferWindow ile birebir): kaynak şubenin şube-özel
/// (müşteri/grup/liste kapsamsız) fiyatları hedef şubeye kopyalanır. Hedefin mevcut şube-özel fiyatları
/// önce temizlenir → PriceTransferService.Transfer(sourceBranchId, targetBranchId, userId) etkilenen satır
/// sayısını döner. Menü/reçeteler firma geneli olduğundan şubeye özel taşınmaz.
/// </summary>
public class IndexModel : PageModel
{
    private readonly PriceTransferService _svc = new();

    [BindProperty] public int SourceBranchId { get; set; }
    [BindProperty] public int TargetBranchId { get; set; }

    public string? Error { get; private set; }
    public List<SelectListItem> BranchOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadBranches();
        var branches = BranchOptions;
        if (branches.Count > 0) SourceBranchId = int.Parse(branches[0].Value);
        if (branches.Count > 1) TargetBranchId = int.Parse(branches[1].Value);
        return Page();
    }

    /// <summary>WPF Ok_Click ile aynı akış: kaynak≠hedef doğrula → Transfer → "{n} fiyat aktarıldı" → PRG.</summary>
    public IActionResult OnPostTransfer()
    {
        if (!CanAccess) return Forbid();
        LoadBranches();

        if (SourceBranchId <= 0 || TargetBranchId <= 0)
        {
            Error = "Kaynak ve hedef şube seçin.";
            return Page();
        }
        if (SourceBranchId == TargetBranchId)
        {
            Error = "Kaynak ve hedef şube aynı olamaz.";
            return Page();
        }

        try
        {
            int n = _svc.Transfer(SourceBranchId, TargetBranchId, Session.CurrentUserId);
            TempData["PriceTransferResult"] = $"{n} fiyat aktarıldı.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadBranches()
    {
        BranchOptions = Session.CurrentCompany is { } co
            ? new BranchService().GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString()))
                .ToList()
            : new List<SelectListItem>();
    }
}
