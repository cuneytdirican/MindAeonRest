using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Units;

public class EditModel : PageModel
{
    private readonly MainUnitService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Birimler sistem DB'sinde (ortak) tutulur, lisans-feature yok — sadece rol denetimi.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var u = _svc.GetById(i);
            if (u is null) return RedirectToPage("Index");
            Id = u.Id; Code = u.Code; Name = u.Name; IsActive = u.IsActive;
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
            if (IsNew)
                _svc.Create(Code, Name, Session.CurrentUserId);
            else
                _svc.Update(Id, Name, IsActive, Session.CurrentUserId);  // Update kodu değiştirmez.
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
