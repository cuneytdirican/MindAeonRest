using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.Expenses;

/// <summary>Gider fişi ekleme — WPF GiderEditWindow birebir karşılığı
/// (gider tipi, tutar, tarih, ödeme kaynağı kasa/banka + araç, proje, açıklama → ExpenseService.Save).
/// Ödeme kaynağına göre kasa/banka çıkış hareketi serviste (atomik) yapılır.</summary>
public class EditModel : PageModel
{
    private readonly ExpenseService _svc = new();

    [BindProperty] public int ExpenseTypeId { get; set; }
    [BindProperty] public int PersonnelId { get; set; }
    [BindProperty] public string Amount { get; set; } = "0";
    [BindProperty] public DateTime ExpenseDate { get; set; } = DateTime.Today;
    [BindProperty] public int PayKind { get; set; } = (int)ExpensePayKind.Kasa;
    [BindProperty] public int InstrumentId { get; set; }
    [BindProperty] public int ProjectId { get; set; }
    [BindProperty] public string? Description { get; set; }

    public string? Error { get; private set; }
    public List<SelectListItem> TypeOptions { get; private set; } = new();
    public List<SelectListItem> PersonnelOptions { get; private set; } = new();
    public List<SelectListItem> PayKindOptions { get; private set; } = new();
    public List<SelectListItem> CashOptions { get; private set; } = new();
    public List<SelectListItem> BankOptions { get; private set; } = new();
    public List<SelectListItem> ProjectOptions { get; private set; } = new();
    // Maaş olan gider tipleri (JS ile personel alanını göstermek için).
    public List<int> SalaryTypeIds { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    // WPF create-only; id rotası nav uyumu için var, mevcut kayıt yüklenmez (ExpenseService güncellemez).
    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (ExpenseTypeId == 0) { Error = "Gider tipi seçiniz."; return Page(); }
        if (!Num.TryParseDecimal(Amount, out var amt) || amt <= 0)
        { Error = "Geçerli tutar girin."; return Page(); }
        if (InstrumentId == 0) { Error = "Ödeme aracı (kasa/banka) seçiniz."; return Page(); }

        var isSalary = SalaryTypeIds.Contains(ExpenseTypeId);

        var x = new Expense
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            ExpenseTypeId = ExpenseTypeId,
            PersonnelId = isSalary && PersonnelId > 0 ? PersonnelId : null,
            ProjectId = ProjectId > 0 ? ProjectId : null,
            Amount = amt,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            PayKind = (ExpensePayKind)PayKind,
            InstrumentId = InstrumentId,
            ExpenseDate = ExpenseDate.Date.Add(DateTime.Now.TimeOfDay),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
        };

        try
        {
            _svc.Save(x, Session.CurrentUserId);
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
        var types = new ExpenseTypeService().GetAll().Where(t => t.IsActive).ToList();
        TypeOptions = types.Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToList();
        SalaryTypeIds = types.Where(t => t.IsSalary).Select(t => t.Id).ToList();

        PersonnelOptions = new() { new SelectListItem("(Personel seçiniz)", "0") };
        PersonnelOptions.AddRange(new PersonnelService().GetAll()
            .Where(p => p.IsActive)
            .Select(p => new SelectListItem(p.FullName, p.Id.ToString())));

        PayKindOptions = new()
        {
            new SelectListItem("Kasa", ((int)ExpensePayKind.Kasa).ToString()),
            new SelectListItem("Banka", ((int)ExpensePayKind.Banka).ToString()),
        };

        int br = Session.CurrentBranch?.Id ?? 0;
        CashOptions = new CashRegisterService().GetAll()
            .Where(k => k.IsActive && k.BranchId == br)
            .Select(k => new SelectListItem(k.Name, k.Id.ToString())).ToList();
        BankOptions = new BankAccountService().GetAll()
            .Where(a => a.IsActive)
            .Select(a => new SelectListItem(a.Name, a.Id.ToString())).ToList();

        ProjectOptions = new() { new SelectListItem("(Yok)", "0") };
        ProjectOptions.AddRange(new ProjectService().GetAll()
            .Where(p => p.IsActive)
            .Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString())));
    }
}
