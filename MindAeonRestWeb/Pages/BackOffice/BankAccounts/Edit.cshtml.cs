using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.BankAccounts;

public class EditModel : PageModel
{
    private readonly BankAccountService _svc = new();
    private readonly BankService _bankSvc = new();
    private readonly CurrencyService _currencySvc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int BankId { get; set; }
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Banka (yerel), döviz (cross-db) lookup listeleri — elle giriş yok, düz <select>.
    public List<SelectListItem> Banks { get; private set; } = new();
    public List<SelectListItem> Currencies { get; private set; } = new();

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    private void LoadLookups()
    {
        Banks = _bankSvc.GetAll()
            .Select(b => new SelectListItem($"{b.Name} ({b.Code})", b.Id.ToString())).ToList();
        Currencies = _currencySvc.GetAll()
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString())).ToList();
    }

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var a = _svc.GetById(i);
            if (a is null) return RedirectToPage("Index");
            Id = a.Id; Code = a.Code; Name = a.Name; BankId = a.BankId; CurrencyId = a.CurrencyId; IsActive = a.IsActive;
        }
        else
        {
            CurrencyId = _currencySvc.GetDefaultId() ?? 0;   // yeni kayıt: varsayılan döviz (TRY)
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
        if (BankId == 0) { Error = "Banka seçiniz."; return Page(); }
        if (CurrencyId == 0) { Error = "Döviz tipi seçiniz (zorunlu)."; return Page(); }

        var item = new BankAccount { Id = Id, Code = Code, Name = Name, BankId = BankId, CurrencyId = CurrencyId, IsActive = IsActive };

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
