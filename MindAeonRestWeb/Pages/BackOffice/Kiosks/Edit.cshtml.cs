using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Kiosks;

public class EditModel : PageModel
{
    private readonly KioskService _svc = new();

    [BindProperty] public int No { get; set; } = 1;
    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public string? ReceiptPrinterName { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.SelfService);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var k = _svc.GetById(i);
            if (k is null) return RedirectToPage("Index");
            Id = k.Id; No = k.No; Code = k.Code; Name = k.Name;
            BranchId = k.BranchId; ReceiptPrinterName = k.ReceiptPrinterName; IsActive = k.IsActive;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }
        if (BranchId <= 0)
        {
            Error = "Şube seçiniz.";
            return Page();
        }
        if (No <= 0) No = 1;

        var k = new Kiosk
        {
            Id = Id,
            No = No,
            Code = Code,
            Name = Name,
            BranchId = BranchId,
            ReceiptPrinterName = string.IsNullOrWhiteSpace(ReceiptPrinterName) ? null : ReceiptPrinterName.Trim(),
            IsActive = IsActive
        };

        try
        {
            _svc.Save(k, Session.CurrentUserId);   // Code (firma) + (BranchId,No) tekilliği serviste
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
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
