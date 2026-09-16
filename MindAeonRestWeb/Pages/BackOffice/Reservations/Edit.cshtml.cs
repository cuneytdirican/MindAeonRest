using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reservations;

public class EditModel : PageModel
{
    private readonly ReservationService _svc = new();
    private readonly CustomerService _customerSvc = new();
    private readonly ReservationTagService _tagSvc = new();
    private readonly ZoneService _zoneSvc = new();
    private readonly TableService _tableSvc = new();

    [BindProperty] public int CustomerId { get; set; }             // 0 = seçilmedi
    [BindProperty] public int ZoneId { get; set; }                 // lokasyon (masalar buna bağlı)
    [BindProperty] public int TagId { get; set; }                  // 0 = yok
    [BindProperty] public DateTime? StartTime { get; set; }
    [BindProperty] public DateTime? EndTime { get; set; }
    [BindProperty] public int PartySize { get; set; }
    [BindProperty] public int Status { get; set; } = (int)ReservationStatus.Beklemede;
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public List<int> TableIds { get; set; } = new();

    public int Id { get; private set; }
    public string? Code { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> ZoneOptions { get; private set; } = new();
    public List<SelectListItem> TagOptions { get; private set; } = new();
    public List<SelectListItem> StatusOptions { get; private set; } = new();

    /// <summary>Lokasyon -> masaları (zone bazlı masa seçimi için JS'e verilir).</summary>
    public Dictionary<int, List<TableOption>> ZoneTables { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.Reservation);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var r = _svc.GetById(i);
            if (r is null) return RedirectToPage("Index");
            Id = r.Id;
            Code = r.Code;
            CustomerId = r.CustomerId;
            ZoneId = r.ZoneId;
            TagId = r.TagId ?? 0;
            StartTime = r.StartTime;
            EndTime = r.EndTime;
            PartySize = r.PartySize;
            Status = (int)r.Status;
            Note = r.Note;
            TableIds = r.TableIds ?? new();
        }
        else
        {
            var now = DateTime.Now;
            StartTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            EndTime = StartTime.Value.AddHours(2);
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        if (Id > 0) Code = _svc.GetById(Id)?.Code;

        TableIds = (TableIds ?? new()).Where(t => t > 0).Distinct().ToList();

        if (CustomerId == 0) { Error = "Müşteri seçiniz."; return Page(); }
        if (ZoneId == 0) { Error = "Lokasyon seçiniz."; return Page(); }
        if (StartTime is null) { Error = "Başlangıç tarih/saati seçiniz."; return Page(); }
        if (EndTime is null) { Error = "Bitiş tarih/saati seçiniz."; return Page(); }
        if (EndTime <= StartTime) { Error = "Bitiş zamanı başlangıçtan sonra olmalı."; return Page(); }
        if (TableIds.Count == 0) { Error = "En az bir masa seçiniz."; return Page(); }
        if (!Enum.IsDefined(typeof(ReservationStatus), Status)) { Error = "Geçerli bir durum seçiniz."; return Page(); }

        // Şube, seçilen lokasyondan türetilir (WPF'de zone -> branch).
        var zone = _zoneSvc.GetById(ZoneId);
        if (zone is null) { Error = "Lokasyon bulunamadı."; return Page(); }

        var reservation = new Reservation
        {
            Id = Id,
            Code = Code ?? string.Empty,
            BranchId = zone.BranchId,
            ZoneId = ZoneId,
            CustomerId = CustomerId,
            TagId = TagId > 0 ? TagId : null,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            StartTime = StartTime.Value,
            EndTime = EndTime.Value,
            PartySize = PartySize,
            Status = (ReservationStatus)Status
        };

        try
        {
            _svc.Save(reservation, TableIds, Session.CurrentUserId);   // masa çakışma kontrolü + kod üretimi Save içinde
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// "+" hızlı müşteri ekleme (proje kuralı): satır-içi yeni müşteri oluşturur ve JSON döner.
    /// WPF ReservationEditWindow.AddCustomer karşılığı (WPF'te tam CustomerEditWindow açar; web'de kod+ünvan yeterli).
    /// CustomerService.Save kodu normalize/benzersizlik doğrular. Antiforgery token header'la gelir.
    /// </summary>
    public IActionResult OnPostAddCustomer(string? code, string? name, string? phone)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        try
        {
            var customer = new Customer
            {
                Code = code ?? "",
                Name = name ?? "",
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                CustomerType = MindAeonRest.Models.CustomerType.Musteri,
                EInvoiceType = MindAeonRest.Models.EInvoiceType.Yok,
                IsActive = true
            };
            var saved = _customerSvc.Save(customer, new List<CustomerAddress>(), Session.CurrentUserId);
            string text = saved.Name + (string.IsNullOrWhiteSpace(saved.Phone) ? "" : $" · {saved.Phone}");
            return new JsonResult(new { ok = true, id = saved.Id, text });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>
    /// "+" hızlı etiket ekleme (proje kuralı): satır-içi yeni rezervasyon etiketi oluşturur ve JSON döner.
    /// WPF ReservationEditWindow.AddTag karşılığı. ReservationTagService.Save kod normalize/benzersizlik doğrular.
    /// </summary>
    public IActionResult OnPostAddTag(string? code, string? name, string? color)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        try
        {
            var tag = new ReservationTag
            {
                Code = code ?? "",
                Name = name ?? "",
                Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
                IsActive = true
            };
            var saved = _tagSvc.Save(tag, Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = saved.Id, text = saved.Name });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    private void LoadLookups()
    {
        CustomerOptions = new() { new SelectListItem("(Müşteri seçiniz)", "0") };
        CustomerOptions.AddRange(_customerSvc.GetAll()
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem($"{c.Name}" + (string.IsNullOrWhiteSpace(c.Phone) ? "" : $" · {c.Phone}"), c.Id.ToString())));

        ZoneOptions = new() { new SelectListItem("(Lokasyon seçiniz)", "0") };
        ZoneOptions.AddRange(_zoneSvc.GetAll()
            .Where(z => z.IsActive)
            .Select(z => new SelectListItem(z.Display, z.Id.ToString())));

        TagOptions = new() { new SelectListItem("(Etiket yok)", "0") };
        TagOptions.AddRange(_tagSvc.GetAll()
            .Where(t => t.IsActive)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString())));

        StatusOptions = Enum.GetValues<ReservationStatus>()
            .Select(s => new SelectListItem(s.ToTr(), ((int)s).ToString()))
            .ToList();

        // Her aktif lokasyonun masaları (zone bazlı checkbox listesi için).
        ZoneTables = new();
        foreach (var z in _zoneSvc.GetAll().Where(z => z.IsActive))
        {
            ZoneTables[z.Id] = _tableSvc.GetByZone(z.Id)
                .Where(t => t.IsActive)
                .Select(t => new TableOption { Id = t.Id, Label = $"{t.Code} ({t.Seats}k)" })
                .ToList();
        }
    }

    public class TableOption
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }
}
