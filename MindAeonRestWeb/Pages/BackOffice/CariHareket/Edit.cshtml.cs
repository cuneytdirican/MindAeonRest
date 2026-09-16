using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.CariHareket;

/// <summary>Manuel cari hareket ekleme — WPF CariHareketEditWindow birebir karşılığı
/// (cari, işlem tipi, tarih, borç, alacak, proje, açıklama → CariHareketService.AddManual).</summary>
public class EditModel : PageModel
{
    private readonly CariHareketService _svc = new();

    [BindProperty] public int CustomerId { get; set; }
    [BindProperty] public int DocType { get; set; } = (int)CariDocType.Manuel;
    [BindProperty] public DateTime TxnDate { get; set; } = DateTime.Today;
    [BindProperty] public string Debt { get; set; } = "0";
    [BindProperty] public string Credit { get; set; } = "0";
    [BindProperty] public int ProjectId { get; set; }
    [BindProperty] public string? Description { get; set; }

    public string? Error { get; private set; }
    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> TypeOptions { get; private set; } = new();
    public List<SelectListItem> ProjectOptions { get; private set; } = new();

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet(int? customerId)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (customerId is int cid && cid > 0) CustomerId = cid;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (CustomerId == 0) { Error = "Cari seçiniz."; return Page(); }
        if (!Num.TryParseDecimal(Debt, out var debt) || debt < 0)
        { Error = "Borç geçersiz."; return Page(); }
        if (!Num.TryParseDecimal(Credit, out var credit) || credit < 0)
        { Error = "Alacak geçersiz."; return Page(); }
        if (debt == 0 && credit == 0) { Error = "Borç veya Alacak giriniz."; return Page(); }

        var h = new MindAeonRest.Models.CariHareket
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            CustomerId = CustomerId,
            TxnDate = TxnDate.Date.Add(DateTime.Now.TimeOfDay),
            DocType = (CariDocType)DocType,
            Debt = debt,
            Credit = credit,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            ProjectId = ProjectId > 0 ? ProjectId : null,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
        };

        try
        {
            _svc.AddManual(h, Session.CurrentUserId);
            return RedirectToPage("Index", new { CustomerId });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// "+" hızlı cari ekleme (proje kuralı): satır-içi yeni cari (kod+ünvan+telefon) oluşturur ve JSON döner.
    /// Sayfa yenilenmeden JS listeye ekleyip seçer; antiforgery token header'la gönderilir (Customers/Edit AddGroup deseni).
    /// </summary>
    public IActionResult OnPostAddCustomer(string? code, string? name, string? phone)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        try
        {
            var svc = new CustomerService();
            var saved = svc.Save(new Customer
            {
                Code = code ?? "",
                Name = name ?? "",
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                CustomerType = MindAeonRest.Models.CustomerType.Musteri,
                EInvoiceType = MindAeonRest.Models.EInvoiceType.Yok,
                IsActive = true
            }, Array.Empty<CustomerAddress>(), Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = saved.Id, text = $"{saved.Code} · {saved.Name}" });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    private void LoadLookups()
    {
        CustomerOptions = new() { new SelectListItem("(Cari seçiniz)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));

        // WPF ile birebir işlem tipi listesi (Açılış / Manuel / Tahsilat / Ödeme).
        TypeOptions = new()
        {
            new SelectListItem(CariDocType.Acilis.ToTr(), ((int)CariDocType.Acilis).ToString()),
            new SelectListItem(CariDocType.Manuel.ToTr(), ((int)CariDocType.Manuel).ToString()),
            new SelectListItem(CariDocType.Tahsilat.ToTr(), ((int)CariDocType.Tahsilat).ToString()),
            new SelectListItem(CariDocType.Odeme.ToTr(), ((int)CariDocType.Odeme).ToString()),
        };

        ProjectOptions = new() { new SelectListItem("(Yok)", "0") };
        ProjectOptions.AddRange(new ProjectService().GetAll()
            .Where(p => p.IsActive)
            .Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString())));
    }
}
