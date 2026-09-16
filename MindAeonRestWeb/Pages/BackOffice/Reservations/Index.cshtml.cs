using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reservations;

public class IndexModel : PageModel
{
    private readonly ReservationService _svc = new();
    private readonly OrderService _orderSvc = new();
    private readonly WaitlistService _waitlistSvc = new();

    public List<Reservation> Items { get; private set; } = new();
    // FAZ 5c — Bekleme listesi görünümü (online rezervasyonda uygun masa yoksa buraya düşenler).
    public List<WaitlistEntry> Waitlist { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "reservations";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    // WPF ReservationWindow filtre paneliyle birebir: Başlangıç/Bitiş saati (HH:mm). Boşsa süzme yapılmaz.
    [BindProperty(SupportsGet = true)] public string? StartTime { get; set; }
    [BindProperty(SupportsGet = true)] public string? EndTime { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    public string? StatusMessage { get; private set; }

    // Gate: yalnız SysAdmin/Patron + Rezervasyon lisansı.
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.Reservation);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        StatusMessage = TempData["Status"] as string;
        var all = _svc.GetForList(Date, includeDeleted: ShowDeleted);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all.Where(r => (r.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (r.CustomerName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (r.TableCodes ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                              || (r.ZoneName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Saat aralığı süzmesi: ReservationService liste sorgusunda saat parametresi yok → sonuç kümesini
        // rezervasyonun başlangıç saatine (StartTime.TimeOfDay) göre burada süz (WPF ParseWindow karşılığı).
        if (TimeSpan.TryParse(StartTime, out var st))
            all = all.Where(r => r.StartTime.TimeOfDay >= st).ToList();
        if (TimeSpan.TryParse(EndTime, out var et))
            all = all.Where(r => r.StartTime.TimeOfDay <= et).ToList();

        Items = all;

        // Bekleme listesi (şube kapsamlı). Silinenler hariç; tarih süzgeci rezervasyonlarla aynı gün filtresine uyar.
        int branchId = Session.CurrentBranch?.Id ?? 0;
        if (branchId > 0)
            Waitlist = _waitlistSvc.GetForList(branchId, Date, includeDeleted: ShowDeleted);

        return Page();
    }

    /// <summary>Bekleme listesi durumu değiştir: Çağrıldı / Geldi / İptal. (WPF/back-office paylaşımlı Core.)</summary>
    public IActionResult OnPostWaitlistStatus(int id, int status)
    {
        if (!CanAccess) return Forbid();
        try { _waitlistSvc.SetStatus(id, (WaitlistStatus)status, Session.CurrentUserId); }
        catch (Exception ex) { TempData["Status"] = ex.Message; }
        return RedirectToPage(new { Q, Date, StartTime, EndTime, ShowDeleted, Tab = "waitlist" });
    }

    /// <summary>
    /// "Adisyon Aç" (masaya otur) — WPF ReservationWindow.OpenOrder_Click karşılığı.
    /// Rezervasyonun masaları + müşterisiyle tek atomik Core çağrısı (OrderService.CreateOrder) ile açık adisyon oluşturur,
    /// rezervasyonu "Geldi" işaretler. WPF'te ardından AdisyonWindow açılır; web'de yalnız adisyon oluşturulup mesaj döner.
    /// </summary>
    public IActionResult OnPostOpenOrder(int id)
    {
        if (!CanAccess) return Forbid();
        try
        {
            var res = _svc.GetById(id);
            if (res is null) { TempData["Status"] = "Rezervasyon bulunamadı."; return RedirectToPage(new { Q, Date, StartTime, EndTime, ShowDeleted }); }

            var tableIds = _svc.GetTableIds(res.Id);
            // Müşteri rezervasyon kaydında zorunlu (Save doğrular); yine de güvenli tarafta 0 kontrolü.
            if (res.CustomerId == 0) { TempData["Status"] = "Rezervasyonda müşteri tanımlı değil; adisyon açılamadı."; return RedirectToPage(new { Q, Date, StartTime, EndTime, ShowDeleted }); }

            var order = _orderSvc.CreateOrder(res.BranchId, res.ZoneId, res.CustomerId, tableIds, Session.CurrentUserId, res.Id);
            _svc.SetStatus(res.Id, ReservationStatus.Geldi, Session.CurrentUserId);
            TempData["Status"] = $"{res.Code} için adisyon açıldı ({order.Code}). Masa(lar): {res.TableCodes}.";
        }
        catch (Exception ex)
        {
            TempData["Status"] = ex.Message;
        }
        return RedirectToPage(new { Q, Date, StartTime, EndTime, ShowDeleted });
    }

    /// <summary>Soft-delete / geri al — WPF ile aynı jenerik mekanizma (Schema.SetDeleted).</summary>
    public IActionResult OnPostToggleDelete(int id, bool deleted)
    {
        if (!CanAccess) return Forbid();
        using var c = Database.OpenCompany();
        Schema.SetDeleted(c, "Reservations", id, deleted, Session.CurrentUserId);
        return RedirectToPage(new { Q, Date, StartTime, EndTime, ShowDeleted });
    }
}
