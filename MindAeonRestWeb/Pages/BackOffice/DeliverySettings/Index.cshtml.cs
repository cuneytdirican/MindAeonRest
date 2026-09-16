using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.DeliverySettings;

/// <summary>Paket / Yemek Sitesi Ayarları (tek form, ŞUBE BAZLI): seçili şube için
/// Trendyol / Getir / Yemeksepeti / Migros platformlarının mağaza/anahtar/aktiflik ayarları.
/// WPF DeliverySettingsWindow ile birebir alanlar (StoreId, ApiKey, SecretKey, IsEnabled).</summary>
public class IndexModel : PageModel
{
    private readonly DeliveryPlatformService _svc = new();
    private readonly BranchService _branchSvc = new();

    /// <summary>Formda düzenlenen tek platform satırı (WPF DataGrid satırının web karşılığı).</summary>
    public class PlatformRow
    {
        public int Platform { get; set; }
        public string PlatformText { get; set; } = "";
        public string? StoreId { get; set; }
        public string? ApiKey { get; set; }
        public string? SecretKey { get; set; }
        public bool IsEnabled { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }
    [BindProperty] public List<PlatformRow> Rows { get; set; } = new();

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    // WPF: eklenti modül. Web'de gate: admin/Patron + Feature.Delivery (Couriers ile aynı).
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.Delivery);

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        LoadBranches();
        // Şube seçili değilse: oturumdaki şube → yoksa ilk şube.
        if (BranchId <= 0)
            BranchId = Session.CurrentBranch?.Id ?? FirstBranchId();
        LoadRows();
        Saved = saved ? "Şube paket/yemek sitesi ayarları kaydedildi." : null;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadBranches();

        if (BranchId <= 0)
        {
            Error = "Lütfen bir şube seçiniz.";
            LoadRows();
            return Page();
        }

        try
        {
            foreach (var row in Rows)
            {
                _svc.Save(new DeliveryPlatformSetting
                {
                    BranchId = BranchId,
                    Platform = (DeliveryPlatform)row.Platform,
                    StoreId = string.IsNullOrWhiteSpace(row.StoreId) ? null : row.StoreId.Trim(),
                    ApiKey = string.IsNullOrWhiteSpace(row.ApiKey) ? null : row.ApiKey.Trim(),
                    SecretKey = string.IsNullOrWhiteSpace(row.SecretKey) ? null : row.SecretKey.Trim(),
                    IsEnabled = row.IsEnabled
                }, Session.CurrentUserId);
            }
            return RedirectToPage(new { BranchId, saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LoadRows();
            return Page();
        }
    }

    private void LoadBranches()
    {
        BranchOptions = new();
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(_branchSvc.GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }

    private int FirstBranchId()
        => int.TryParse(BranchOptions.FirstOrDefault()?.Value, out var id) ? id : 0;

    private void LoadRows()
    {
        Rows = new();
        if (BranchId <= 0) return;
        // Servis kayıt yoksa boş satır döner → tüm online platformlar her zaman listelenir.
        foreach (var s in _svc.GetByBranch(BranchId))
            Rows.Add(new PlatformRow
            {
                Platform = (int)s.Platform,
                PlatformText = s.PlatformText,
                StoreId = s.StoreId,
                ApiKey = s.ApiKey,
                SecretKey = s.SecretKey,
                IsEnabled = s.IsEnabled
            });
    }
}
