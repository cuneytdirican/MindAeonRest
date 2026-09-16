using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.ExpenseTypes;

public class EditModel : PageModel
{
    private readonly ExpenseTypeService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsSalary { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Gider tipleri şirket DB'sinde tutulur, lisans-feature yok — sadece rol denetimi.
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var e = _svc.GetById(i);
            if (e is null) return RedirectToPage("Index");
            Id = e.Id; Code = e.Code; Name = e.Name; IsSalary = e.IsSalary; IsActive = e.IsActive;
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

        var item = new ExpenseType { Id = Id, Code = Code, Name = Name, IsSalary = IsSalary, IsActive = IsActive };

        try
        {
            _svc.Save(item, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
