using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Personnel;

public class EditModel : PageModel
{
    private readonly PersonnelService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Department { get; set; }
    [BindProperty] public string? NationalId { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public int BranchId { get; set; }   // 0 = (Yok)
    [BindProperty] public string? HireDate { get; set; }
    [BindProperty] public decimal Salary { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    /// <summary>İzinler alt-tablosu — form satırlarından indeksli POST (Leaves[i].X). WPF PersonnelLeaveEditWindow alanları.</summary>
    [BindProperty] public List<LeaveInput> Leaves { get; set; } = new();

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();

    /// <summary>İzin türü seçenekleri (LeaveType enum): (value=int, text).</summary>
    public static IReadOnlyList<(int Value, string Text)> LeaveTypeOptions { get; } =
        Enum.GetValues<LeaveType>().Select(v => ((int)v, v.ToTr())).ToList();

    // Personel = Ön Muhasebe kapsamı — yalnız SysAdmin/Patron + PreAccounting lisansı.
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var p = _svc.GetById(i);
            if (p is null) return RedirectToPage("Index");
            Id = p.Id; Code = p.Code; Name = p.FullName; Title = p.Title; Department = p.Department;
            NationalId = p.NationalId; Phone = p.Phone; BranchId = p.BranchId ?? 0;
            HireDate = p.HireDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Salary = p.Salary; IsActive = p.IsActive;
            Leaves = _svc.GetLeaves(p.Id).Select(l => new LeaveInput
            {
                Id = l.Id,
                LeaveType = (int)l.LeaveType,
                StartDate = l.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = l.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Note = l.Note
            }).ToList();
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        // Boş satırları at (tümüyle boş kalan şablon satırlar).
        Leaves = (Leaves ?? new()).Where(l => !l.IsEmpty).ToList();

        if (CodeHelper.Normalize(Code ?? "").Length == 0) { Error = "Geçerli bir personel kodu giriniz."; return Page(); }
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Personel adı boş olamaz."; return Page(); }

        DateTime? hire = null;
        if (!string.IsNullOrWhiteSpace(HireDate))
        {
            if (!TryParseDate(HireDate, out var h)) { Error = "Geçerli bir işe giriş tarihi giriniz."; return Page(); }
            hire = h;
        }

        // İzin satırlarını doğrula (SaveLeave içinde Days hesaplanır; burada tarih/tür kontrolü).
        var leaves = new List<PersonnelLeave>();
        for (int i = 0; i < Leaves.Count; i++)
        {
            var l = Leaves[i];
            if (!Enum.IsDefined(typeof(LeaveType), l.LeaveType)) { Error = $"{i + 1}. izin satırı: geçersiz izin türü."; return Page(); }
            if (!TryParseDate(l.StartDate, out var s)) { Error = $"{i + 1}. izin satırı: geçerli bir başlangıç tarihi giriniz."; return Page(); }
            if (!TryParseDate(l.EndDate, out var e)) { Error = $"{i + 1}. izin satırı: geçerli bir bitiş tarihi giriniz."; return Page(); }
            if (e < s) { Error = $"{i + 1}. izin satırı: bitiş tarihi başlangıçtan önce olamaz."; return Page(); }
            leaves.Add(new PersonnelLeave { Id = l.Id, LeaveType = (LeaveType)l.LeaveType, StartDate = s, EndDate = e, Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim() });
        }

        var person = new MindAeonRest.Models.Personnel
        {
            Id = Id,
            Code = Code!,
            FullName = Name!.Trim(),
            Title = Empty(Title),
            Department = Empty(Department),
            NationalId = Empty(NationalId),
            Phone = Empty(Phone),
            BranchId = BranchId > 0 ? BranchId : null,
            HireDate = hire,
            Salary = Salary,
            IsActive = IsActive
        };

        try
        {
            _svc.Save(person, Session.CurrentUserId);   // Kod normalize + firma-benzersizlik Save içinde

            // İzinleri uzlaştır: mevcutlarla karşılaştır — eklenen/güncellenen SaveLeave, kaldırılan DeleteLeave.
            var existingIds = _svc.GetLeaves(person.Id).Select(x => x.Id).ToHashSet();
            var keptIds = new HashSet<int>();
            foreach (var l in leaves)
            {
                l.PersonnelId = person.Id;
                if (l.Id > 0) keptIds.Add(l.Id);
                _svc.SaveLeave(l, Session.CurrentUserId);
            }
            foreach (var gone in existingIds.Where(x => !keptIds.Contains(x)))
                _svc.DeleteLeave(gone, Session.CurrentUserId);

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
        BranchOptions = new() { new SelectListItem("(Yok)", "0") };
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(new BranchService().GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool TryParseDate(string? s, out DateTime value)
        => DateTime.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>Formdan gelen tek izin satırı (indeksli bind). Id>0 = mevcut kayıt.</summary>
    public class LeaveInput
    {
        public int Id { get; set; }
        public int LeaveType { get; set; } = (int)MindAeonRest.Models.LeaveType.Yillik;
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string? Note { get; set; }

        public bool IsEmpty => Id == 0 && string.IsNullOrWhiteSpace(StartDate) && string.IsNullOrWhiteSpace(EndDate) && string.IsNullOrWhiteSpace(Note);
    }
}
