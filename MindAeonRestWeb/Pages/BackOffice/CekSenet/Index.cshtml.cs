using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CekSenet;

/// <summary>Çek / Senet portföyü — WPF CekSenetWindow + CekVadeWindow karşılığı.
/// Liste (tür/yön/tutar/vade/durum/cari) + arama + yön/durum + "vadesi yaklaşan" filtresi + soft-delete (dbo.CheckNotes).</summary>
public class IndexModel : PageModel
{
    private readonly CheckNoteService _svc = new();
    private readonly BankAccountService _bankSvc = new();

    public List<CheckNote> Items { get; private set; } = new();
    public List<BankAccount> BankAccounts { get; private set; } = new();  // "Bankaya Tahsil/Ödeme" için aktif banka hesapları

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int Dir { get; set; } = -1;      // -1 = tümü, 0 = Alınan, 1 = Verilen
    [BindProperty(SupportsGet = true)] public int Status { get; set; } = -1;   // -1 = tümü, aksi CheckStatus
    [BindProperty(SupportsGet = true)] public bool Upcoming { get; set; }      // "vadesi yaklaşan": açık + ≤7 gün

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting.
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        Message = TempData["CekMessage"] as string;
        Error = TempData["CekError"] as string;
        BankAccounts = _bankSvc.GetAll().Where(a => a.IsActive).ToList();

        CheckDirection? dir = Dir >= 0 && Enum.IsDefined(typeof(CheckDirection), Dir) ? (CheckDirection)Dir : null;
        var all = _svc.GetAll(dir);

        if (Status >= 0 && Enum.IsDefined(typeof(CheckStatus), Status))
            all = all.Where(n => (int)n.Status == Status).ToList();

        if (Upcoming)
            all = all.Where(n => !n.IsClosed && n.DaysToDue <= 7).ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(n => (n.Serial ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (n.BankName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (n.CustomerName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Items = all.OrderBy(n => n.IsClosed).ThenBy(n => n.DueDate).ToList();
        return Page();
    }

    /// <summary>Soft-delete / geri al — jenerik mekanizma (Schema.SetDeleted, şirket DB "CheckNotes").</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "CheckNotes", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, Dir, Status, Upcoming });
    }

    /// <summary>Hızlı durum uygula — WPF CekSenetWindow ApplyStatus karşılığı (CheckNoteService.ChangeStatus).</summary>
    public IActionResult OnPostChangeStatus(int id, int status)
    {
        if (!CanAccess) return Forbid();
        if (Enum.IsDefined(typeof(CheckStatus), status))
        {
            try { _svc.ChangeStatus(id, (CheckStatus)status, Session.CurrentUserId); }
            catch { /* geçersiz geçiş / cari hata: sessizce listeye dön */ }
        }
        return RedirectToPage(new { Q, Dir, Status, Upcoming });
    }

    /// <summary>Bankaya Tahsil/Ödeme — WPF CekVadeWindow ToBank_Click karşılığı (CheckNoteService.CollectToBank).
    /// Alınan → banka GİRİŞ + Tahsil; Verilen → banka ÇIKIŞ + Ödendi. Yalnız açık (kapanmamış) kayıtlar için görünür.</summary>
    public IActionResult OnPostCollectToBank(int id, int bankAccountId)
    {
        if (!CanAccess) return Forbid();
        if (bankAccountId <= 0)
            TempData["CekError"] = "Banka hesabı seçiniz.";
        else
        {
            try { _svc.CollectToBank(id, bankAccountId, Session.CurrentUserId); TempData["CekMessage"] = "Çek/senet bankaya işlendi."; }
            catch (Exception ex) { TempData["CekError"] = ex.Message; }
        }
        return RedirectToPage(new { Q, Dir, Status, Upcoming });
    }
}
