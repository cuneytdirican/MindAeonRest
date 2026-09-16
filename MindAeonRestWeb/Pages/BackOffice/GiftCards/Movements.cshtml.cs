using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.GiftCards;

public class MovementsModel : PageModel
{
    private readonly GiftCardService _svc = new();

    public GiftCard? Card { get; private set; }
    public List<GiftCardMovement> Movements { get; private set; } = new();

    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet(int id)
    {
        if (!CanAccess) return Forbid();
        Card = _svc.GetById(id);
        if (Card is null) return RedirectToPage("Index");
        Movements = _svc.GetMovements(id);
        return Page();
    }
}
