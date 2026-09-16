using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CekSenet;

/// <summary>WPF CekSenetEditWindow karşılığı. Yeni kayıt → tam form (CheckNoteService.Add, cari hareket dahil).
/// Mevcut kayıt → alanlar salt-okunur + durum değişimi (ChangeStatus; karşılıksızda cari ters). WPF de mevcut kaydı alan-alan düzenletmez.</summary>
public class EditModel : PageModel
{
    private readonly CheckNoteService _svc = new();

    [BindProperty] public int Kind { get; set; } = (int)CheckNoteKind.Cek;
    [BindProperty] public int Direction { get; set; } = (int)CheckDirection.Alinan;
    [BindProperty] public int CustomerId { get; set; }
    [BindProperty] public string? Serial { get; set; }
    [BindProperty] public string? BankName { get; set; }
    [BindProperty] public decimal Amount { get; set; }
    [BindProperty] public DateTime IssueDate { get; set; } = DateTime.Today;
    [BindProperty] public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public int NewStatus { get; set; }   // mevcut kayıtta durum uygula

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Mevcut kayıt görüntüleme
    public CheckNote? Existing { get; private set; }

    public List<SelectListItem> KindOptions { get; private set; } = new();
    public List<SelectListItem> DirectionOptions { get; private set; } = new();
    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> BankOptions { get; private set; } = new();
    public List<SelectListItem> StatusOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (id is int i && i > 0)
        {
            var n = _svc.GetById(i);
            if (n is null) return RedirectToPage("Index");
            Id = n.Id;
            Existing = n;
            Kind = (int)n.Kind;
            Direction = (int)n.Direction;
            CustomerId = n.CustomerId;
            Serial = n.Serial;
            BankName = n.BankName;
            Amount = n.Amount;
            IssueDate = n.IssueDate;
            DueDate = n.DueDate;
            Note = n.Note;
            NewStatus = (int)n.Status;
        }
        return Page();
    }

    /// <summary>Yeni çek/senet ekle (cari hareket dahil).</summary>
    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (!Enum.IsDefined(typeof(CheckNoteKind), Kind)) { Error = "Tür seçiniz."; return Page(); }
        if (!Enum.IsDefined(typeof(CheckDirection), Direction)) { Error = "Yön seçiniz."; return Page(); }
        if (CustomerId == 0) { Error = "Cari seçiniz."; return Page(); }
        if (Amount <= 0) { Error = "Tutar 0'dan büyük olmalı."; return Page(); }
        if (DueDate.Date < IssueDate.Date) { Error = "Vade, düzenleme tarihinden önce olamaz."; return Page(); }

        var n = new CheckNote
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            Kind = (CheckNoteKind)Kind,
            Direction = (CheckDirection)Direction,
            CustomerId = CustomerId,
            Serial = Empty(Serial),
            BankName = Empty(BankName),
            Amount = Amount,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            IssueDate = IssueDate,
            DueDate = DueDate,
            Status = CheckStatus.Portfoyde,
            Note = Empty(Note)
        };

        try
        {
            _svc.Add(n, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>Mevcut kayıtta durum uygula (WPF "Durumu Uygula"). Karşılıksızda cari ters çevrilir.</summary>
    public IActionResult OnPostStatus(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();
        Existing = Id > 0 ? _svc.GetById(Id) : null;
        if (Existing is null) return RedirectToPage("Index");

        if (!Enum.IsDefined(typeof(CheckStatus), NewStatus)) { Error = "Geçerli bir durum seçiniz."; return Page(); }

        try
        {
            _svc.ChangeStatus(Id, (CheckStatus)NewStatus, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Existing = _svc.GetById(Id);
            return Page();
        }
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void LoadLookups()
    {
        KindOptions = new()
        {
            new SelectListItem("Çek", ((int)CheckNoteKind.Cek).ToString()),
            new SelectListItem("Senet", ((int)CheckNoteKind.Senet).ToString()),
        };
        DirectionOptions = new()
        {
            new SelectListItem("Alınan (müşteriden)", ((int)CheckDirection.Alinan).ToString()),
            new SelectListItem("Verilen (tedarikçiye)", ((int)CheckDirection.Verilen).ToString()),
        };

        CustomerOptions = new() { new SelectListItem("(Cari seçiniz)", "0") };
        CustomerOptions.AddRange(new CustomerService().GetAll()
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));

        // WPF banka serbest metin girer; web tarafında BankService lookup'tan seçilir, seçilen bankanın adı BankName alanına yazılır.
        BankOptions = new() { new SelectListItem("(Banka seçiniz / senette boş)", "") };
        BankOptions.AddRange(new BankService().GetAll()
            .Where(b => b.IsActive)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Name)));

        StatusOptions = Enum.GetValues<CheckStatus>()
            .Select(s => new SelectListItem(s.ToTr(), ((int)s).ToString()))
            .ToList();
    }
}
