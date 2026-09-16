using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Orders;

/// <summary>Sipariş Detayı — WPF OrderDetailWindow birebir karşılığı (salt-okunur izleme).
/// GetById + GetAllLines (silinmiş/iptal dahil) + GetOrderFiches + her fiş için GetPaymentLines.
/// IsZLocked ise "Z Kilitli" rozeti. Gate: SysAdmin/Patron.</summary>
public class DetailModel : PageModel
{
    private readonly OrderService _orders = new();

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public Order? Order { get; private set; }
    public List<OrderLine> Lines { get; private set; } = new();
    public List<ClFiche> Fiches { get; private set; } = new();
    // Fiş Id → o fişin ödeme hareketleri
    public Dictionary<int, List<PaymentLine>> Payments { get; private set; } = new();
    public bool ZLocked { get; private set; }

    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        Order = _orders.GetById(Id);
        if (Order is null) return NotFound();

        ZLocked = _orders.IsZLocked(Id);
        Lines = _orders.GetAllLines(Id);
        Fiches = _orders.GetOrderFiches(Id);
        foreach (var f in Fiches)
            Payments[f.Id] = _orders.GetPaymentLines(f.Id);

        return Page();
    }
}
