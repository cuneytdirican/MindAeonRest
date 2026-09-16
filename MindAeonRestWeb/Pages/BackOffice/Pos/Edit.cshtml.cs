using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Pos;

public class EditModel : PageModel
{
    private readonly PosService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public int BankId { get; set; }
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public int Scope { get; set; } = (int)FinanceScope.Sube;
    [BindProperty] public int KioskId { get; set; }   // 0 = seçilmedi
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> BankOptions { get; private set; } = new();
    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> ScopeOptions { get; private set; } = new();
    public List<SelectListItem> KioskOptions { get; private set; } = new();

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var p = _svc.GetById(i);
            if (p is null) return RedirectToPage("Index");
            Id = p.Id;
            Code = p.Code; Name = p.Name;
            BranchId = p.BranchId; BankId = p.BankId; CurrencyId = p.CurrencyId;
            Scope = (int)p.Scope; KioskId = p.KioskId ?? 0; IsActive = p.IsActive;
        }
        else
        {
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
        }
        LoadLookups();
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

        var scope = Enum.IsDefined(typeof(FinanceScope), Scope) ? (FinanceScope)Scope : FinanceScope.Sube;

        var pos = new CreditCardPos
        {
            Id = Id,
            Code = Code,
            Name = Name,
            BranchId = BranchId,
            BankId = BankId,
            CurrencyId = CurrencyId,
            Scope = scope,
            KioskId = scope == FinanceScope.Kiosk ? (KioskId > 0 ? KioskId : null) : null,
            IsActive = IsActive
        };

        if (scope == FinanceScope.Kiosk && pos.KioskId is null)
        {
            Error = "KIOSK POS için KIOSK seçiniz.";
            return Page();
        }

        try
        {
            _svc.Save(pos, Session.CurrentUserId);   // Save şube/banka/döviz zorunluluğunu ayrıca doğrular
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

        BankOptions = new() { new SelectListItem("(Banka seçiniz)", "0") };
        BankOptions.AddRange(new BankService().GetAll()
            .Select(b => new SelectListItem(b.Display, b.Id.ToString())));

        CurrencyOptions = new() { new SelectListItem("(Döviz seçiniz)", "0") };
        CurrencyOptions.AddRange(new CurrencyService().GetAll()
            .Select(c => new SelectListItem(c.Code, c.Id.ToString())));

        ScopeOptions = new()
        {
            new SelectListItem("Şube POS", ((int)FinanceScope.Sube).ToString()),
            new SelectListItem("KIOSK POS", ((int)FinanceScope.Kiosk).ToString())
        };

        // KIOSK listesi seçili şubeye göre (kapsam KIOSK olduğunda kullanılır).
        KioskOptions = new() { new SelectListItem("(KIOSK seçiniz)", "0") };
        if (BranchId > 0)
            KioskOptions.AddRange(new KioskService().GetByBranch(BranchId)
                .Select(k => new SelectListItem(k.Name, k.Id.ToString())));
    }
}
