using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.GiftCards;

public class IssueModel : PageModel
{
    private readonly GiftCardService _svc = new();

    [BindProperty] public string Amount { get; set; } = "";
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public int CustomerId { get; set; }         // 0 = seçilmedi (opsiyonel)
    [BindProperty] public string? Code { get; set; }           // boş = otomatik
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public DateTime? Expires { get; set; }
    [BindProperty] public int SaleMethod { get; set; }         // 0 = Nakit (kasa), 1 = Kart (POS)
    [BindProperty] public int SaleInstrumentId { get; set; }   // kasa Id veya POS Id

    public string? Error { get; private set; }

    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> CashOptions { get; private set; } = new();
    public List<SelectListItem> PosOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (!Num.TryParseDecimal(Amount, out var amount) || amount <= 0)
        {
            Error = "Geçerli bir tutar girin (0'dan büyük).";
            return Page();
        }

        // Satış tahsilatı: Nakit→kasa, Kart→POS (elle giriş yok, ikisi de fail-closed).
        bool cash = SaleMethod == 0;
        PaymentKind saleKind = cash ? PaymentKind.Nakit : PaymentKind.KrediKarti;
        if (SaleInstrumentId == 0)
        {
            Error = cash
                ? "Satış için bir kasa seçin (aktif şube kasası yoksa Kasalar'dan ekleyin)."
                : "Satış için bir POS cihazı seçin (aktif şube POS'u yoksa POS'lardan ekleyin).";
            return Page();
        }

        int? customerId = CustomerId > 0 ? CustomerId : null;
        int? currencyId = CurrencyId > 0 ? CurrencyId : null;
        string? code = string.IsNullOrWhiteSpace(Code) ? null : Code.Trim();
        string? note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        DateTime? expires = Expires?.Date.AddDays(1).AddSeconds(-1);   // gün sonu (WPF ile aynı)

        try
        {
            _svc.Issue(amount, currencyId, customerId, expires, code, note, saleKind, SaleInstrumentId, Session.CurrentUserId);
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
        int branch = Session.CurrentBranch?.Id ?? 0;

        CurrencyOptions = new CurrencyService().GetAll()
            .Select(c => new SelectListItem(c.Code, c.Id.ToString()))
            .ToList();

        CustomerOptions = new() { new SelectListItem("(Müşteri seçiniz — opsiyonel)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Where(c => c.Code != "PA")
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));

        CashOptions = new CashRegisterService().GetAll()
            .Where(k => k.IsActive && k.BranchId == branch)
            .Select(k => new SelectListItem($"{k.Code} · {k.Name}", k.Id.ToString()))
            .ToList();

        PosOptions = new PosService().GetAll()
            .Where(p => p.IsActive && p.BranchId == branch)
            .Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString()))
            .ToList();
    }
}
