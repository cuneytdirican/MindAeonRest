using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.MealVoucherOut;

/// <summary>Yemek Çeki Çıkışı — WPF YemekCekiCikisWindow karşılığı.
/// Toplanan yemek çekleri, Yemek Çeki Şirketi'ne (cari) Satış Faturası olarak çıkılır → şirket bize borçlanır (cari borç).
/// WPF ile birebir: CariHareketService.AddManual(SatisFaturasi, DocRefType="YemekÇekiÇıkış", Debt=tutar).</summary>
public class IndexModel : PageModel
{
    private readonly CariHareketService _svc = new();
    private readonly MealVoucherAccountService _accSvc = new();

    [BindProperty] public int AccountId { get; set; }          // Yemek Çeki Hesabı (bilgi amaçlı; bakiye ipucu)
    [BindProperty] public int CustomerId { get; set; }         // Yemek Çeki Şirketi (cari) — zorunlu
    [BindProperty] public string Amount { get; set; } = "";
    [BindProperty] public DateTime? Date { get; set; }
    [BindProperty] public string? DocNo { get; set; }
    [BindProperty] public string? Note { get; set; }

    public string? Error { get; private set; }

    public List<SelectListItem> AccountOptions { get; private set; } = new();
    public List<SelectListItem> CustomerOptions { get; private set; } = new();

    // Yemek Çeki = Core özellik (lisans kapısı yok, PreAccounting DEĞİL) — yalnız SysAdmin/Patron.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        Date = DateTime.Today;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (CustomerId <= 0)
        {
            Error = "Yemek Çeki Şirketi (cari) seçiniz.";
            return Page();
        }

        if (!Num.TryParseDecimal(Amount, out var amount) || amount <= 0)
        {
            Error = "Geçerli bir tutar giriniz (0'dan büyük).";
            return Page();
        }

        int branchId = Session.CurrentBranch?.Id ?? 0;
        int currencyId = new CurrencyService().GetDefaultId() ?? 0;
        string? note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        string? docNo = string.IsNullOrWhiteSpace(DocNo) ? null : DocNo.Trim();

        try
        {
            // WPF YemekCekiCikisWindow.Save_Click ile birebir aynı hareket.
            _svc.AddManual(new MindAeonRest.Models.CariHareket
            {
                BranchId = branchId,
                CustomerId = CustomerId,
                TxnDate = Date ?? DateTime.Now,
                DocType = CariDocType.SatisFaturasi,
                DocRefType = "YemekÇekiÇıkış",
                DocNo = docNo,
                Description = "Yemek çeki satış faturası" + (note is null ? "" : $" · {note}"),
                CurrencyId = currencyId,
                Debt = amount,    // şirket bize borçlanır
                Credit = 0
            }, Session.CurrentUserId);

            TempData["Ok"] = $"Yemek çeki satış faturası oluşturuldu: {amount:N2} ₺. Yemek Çeki Şirketi cari hesabına BORÇ yazıldı. Tahsilatı banka girişiyle yapabilirsiniz.";
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

        // WPF: aktif + bu şube yemek çeki hesapları.
        AccountOptions = new() { new SelectListItem("(Yemek çeki hesabı seçiniz)", "0") };
        AccountOptions.AddRange(_accSvc.GetAll()
            .Where(a => a.IsActive && a.BranchId == branch)
            .Select(a => new SelectListItem($"{a.Code} · {a.Name}", a.Id.ToString())));

        // WPF EntityPicker: tüm cariler (PA dahil filtre yok — kullanıcı yemek çeki şirketini seçer).
        CustomerOptions = new() { new SelectListItem("(Yemek Çeki Şirketi seçiniz)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));
    }
}
