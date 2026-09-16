using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.MealVouchers;

public class EditModel : PageModel
{
    private readonly MealVoucherService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var v = _svc.GetById(i);
            if (v is null) return RedirectToPage("Index");
            Id = v.Id; Code = v.Code; Name = v.Name; IsActive = v.IsActive;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }

        var voucher = new MealVoucher { Id = Id, Code = Code, Name = Name, IsActive = IsActive };

        try
        {
            _svc.Save(voucher, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
