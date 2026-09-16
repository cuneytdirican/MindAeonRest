using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Zones;

public class EditModel : PageModel
{
    private readonly ZoneService _svc = new();

    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.LocationTables);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var z = _svc.GetById(i);
            if (z is null) return RedirectToPage("Index");
            Id = z.Id;
            Code = z.Code;
            Name = z.Name;
            BranchId = z.BranchId;
            IsActive = z.IsActive;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        // Fiş yazıcısı artık KIOSK tanımından; düzenlemede eski değer korunur (yeni kayıtta null).
        var existing = Id > 0 ? _svc.GetById(Id) : null;

        var z = new Zone
        {
            Id = Id,
            Code = Code ?? string.Empty,
            Name = (Name ?? string.Empty).Trim(),
            BranchId = BranchId,
            ReceiptPrinterName = existing?.ReceiptPrinterName,
            IsActive = IsActive
        };

        try
        {
            _svc.Save(z, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// "+" hızlı ekleme (proje kuralı; WPF ZoneEditWindow "+ Yeni"): satır-içi yeni şube oluşturur ve JSON döner.
    /// Sayfa yenilenmeden JS &lt;select&gt;'e ekleyip seçer (antiforgery token header'la gönderilir).
    /// </summary>
    public IActionResult OnPostAddBranch(string? code, string? name)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        if (Session.CurrentCompany is not { } co) return new JsonResult(new { ok = false, error = "Firma bulunamadı." });
        try
        {
            var created = new BranchService().Create(new CompanyBranch
            {
                CompanyId = co.Id,
                Code = code ?? string.Empty,
                Name = (name ?? string.Empty).Trim(),
                IsActive = true
            }, Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = created.Id, text = created.Name });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    private void LoadLookups()
    {
        BranchOptions = new() { new SelectListItem("(Şube seçiniz)", "0") };
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(new BranchService().GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }
}
