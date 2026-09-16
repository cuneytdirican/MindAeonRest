using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.ResTags;

public class EditModel : PageModel
{
    private readonly ReservationTagService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Color { get; set; } = "#E91E63";
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    /// <summary>WPF ReservationTagEditWindow paleti ile birebir.</summary>
    public static readonly (string Hex, string Name)[] Palette =
    {
        ("#E91E63", "Pembe"), ("#3F51B5", "Lacivert"), ("#FF9800", "Turuncu"),
        ("#9C27B0", "Mor"), ("#4CAF50", "Yeşil"), ("#F44336", "Kırmızı"),
        ("#00BCD4", "Turkuaz"), ("#FFC107", "Sarı"), ("#607D8B", "Gri")
    };

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.Reservation);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var t = _svc.GetById(i);
            if (t is null) return RedirectToPage("Index");
            Id = t.Id; Code = t.Code; Name = t.Name; Color = t.Color ?? "#E91E63"; IsActive = t.IsActive;
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

        var tag = new ReservationTag { Id = Id, Code = Code, Name = Name, Color = Color, IsActive = IsActive };

        try
        {
            _svc.Save(tag, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
