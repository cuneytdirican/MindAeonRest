using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CashRegisters;

public class EditModel : PageModel
{
    private readonly CashRegisterService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public int Scope { get; set; } = (int)FinanceScope.Sube;
    [BindProperty] public int KioskId { get; set; }   // 0 = seçilmedi (yalnız KIOSK kapsamında zorunlu)
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> ScopeOptions { get; private set; } = new();
    public List<SelectListItem> KioskOptions { get; private set; } = new();

    // Core özellik (lisans kapısı yok) — yalnız SysAdmin/Patron.
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var k = _svc.GetById(i);
            if (k is null) return RedirectToPage("Index");
            Id = k.Id;
            Code = k.Code;
            Name = k.Name;
            BranchId = k.BranchId;
            CurrencyId = k.CurrencyId;
            Scope = (int)k.Scope;
            KioskId = k.KioskId ?? 0;
            IsActive = k.IsActive;
        }
        else
        {
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
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
        if (BranchId == 0) { Error = "Şube seçiniz."; return Page(); }
        if (CurrencyId == 0) { Error = "Döviz tipi seçiniz (zorunlu)."; return Page(); }
        if (!Enum.IsDefined(typeof(FinanceScope), Scope)) { Error = "Geçerli bir kapsam seçiniz."; return Page(); }

        var scope = (FinanceScope)Scope;
        int? kioskId = null;
        if (scope == FinanceScope.Kiosk)
        {
            if (KioskId == 0) { Error = "KIOSK kasası için KIOSK seçiniz."; return Page(); }
            kioskId = KioskId;
        }

        var k = new CashRegister
        {
            Id = Id,
            Code = Code,
            Name = Name,
            BranchId = BranchId,
            CurrencyId = CurrencyId,
            Scope = scope,
            KioskId = kioskId,
            IsActive = IsActive
        };

        try
        {
            _svc.Save(k, Session.CurrentUserId);   // Kod normalize + firma-benzersizlik Save içinde
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

        CurrencyOptions = new CurrencyService().GetAll()
            .Select(c => new SelectListItem(c.Code, c.Id.ToString()))
            .ToList();

        ScopeOptions = new()
        {
            new SelectListItem(FinanceScope.Sube.ToTr(), ((int)FinanceScope.Sube).ToString()),
            new SelectListItem(FinanceScope.Kiosk.ToTr(), ((int)FinanceScope.Kiosk).ToString()),
        };

        // KIOSK kapsamı için tüm aktif KIOSK'lar (şube adıyla etiketli; kaydetmede şube tutarlılığı Save/DB üstünden).
        KioskOptions = new() { new SelectListItem("(KIOSK seçiniz)", "0") };
        KioskOptions.AddRange(new KioskService().GetAll()
            .Where(k => k.IsActive)
            .Select(k => new SelectListItem($"{k.Name} — {k.BranchName}", k.Id.ToString())));
    }
}
