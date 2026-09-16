using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.PriceLists;

public class EditModel : PageModel
{
    private readonly PriceListService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var p = _svc.GetById(i);
            if (p is null) return RedirectToPage("Index");
            Id = p.Id; Code = p.Code; Name = p.Name; IsActive = p.IsActive;
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

        try
        {
            if (Id == 0)
                _svc.Create(Code, Name, Session.CurrentUserId);
            else
                _svc.Update(Id, Code, Name, IsActive, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
