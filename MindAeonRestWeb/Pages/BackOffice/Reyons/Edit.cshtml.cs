using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reyons;

public class EditModel : PageModel
{
    private readonly ReyonService _svc = new();
    private readonly ZoneService _zoneSvc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int ZoneId { get; set; }
    [BindProperty] public int StationType { get; set; } = (int)MindAeonRest.Models.StationType.Mutfak;
    [BindProperty] public string? PrinterName { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> ZoneOptions { get; private set; } = new();
    public List<SelectListItem> StationOptions { get; private set; } = new();
    // "+" hızlı zone ekleme mini-formu için şube listesi (ZoneService.Save BranchId zorunlu kılar).
    public List<SelectListItem> BranchOptions { get; private set; } = new();

    // Mutfak/reyon özelliği — yalnız SysAdmin/Patron + Feature.Kitchen.
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.Kitchen);

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
            Name = r.Name;
            ZoneId = r.ZoneId;
            StationType = (int)r.StationType;
            PrinterName = r.PrinterName;
            IsActive = r.IsActive;
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
        if (ZoneId == 0) { Error = "Lokasyon (zone) seçiniz."; return Page(); }
        if (!Enum.IsDefined(typeof(StationType), StationType)) { Error = "Geçerli bir istasyon tipi seçiniz."; return Page(); }

        var r = new Reyon
        {
            Id = Id,
            Code = Code,
            Name = Name,
            ZoneId = ZoneId,
            StationType = (StationType)StationType,
            PrinterName = string.IsNullOrWhiteSpace(PrinterName) ? null : PrinterName.Trim(),
            IsActive = IsActive
        };

        try
        {
            _svc.Save(r, Session.CurrentUserId);   // Kod normalize + firma-benzersizlik Save içinde
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// "+" hızlı ekleme (proje kuralı; WPF ReyonEditWindow "+ Yeni"): satır-içi yeni lokasyon (zone) oluşturur ve JSON döner.
    /// Sayfa yenilenmeden JS &lt;select&gt;'e ekleyip seçer (antiforgery token header'la gönderilir).
    /// </summary>
    public IActionResult OnPostAddZone(string? code, string? name, int branchId)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        try
        {
            var created = _zoneSvc.Save(new Zone
            {
                Id = 0,
                Code = code ?? string.Empty,
                Name = (name ?? string.Empty).Trim(),
                BranchId = branchId,
                IsActive = true
            }, Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = created.Id, text = $"{created.Name} ({created.Code})" });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    private void LoadLookups()
    {
        ZoneOptions = new() { new SelectListItem("(Lokasyon seçiniz)", "0") };
        ZoneOptions.AddRange(_zoneSvc.GetAll()
            .Select(z => new SelectListItem($"{z.Name} ({z.Code})", z.Id.ToString())));

        StationOptions = new()
        {
            new SelectListItem("Mutfak (yemek — KDS / Sipariş Teslim)", ((int)MindAeonRest.Models.StationType.Mutfak).ToString()),
            new SelectListItem("Bar / İçecek (Meşrubatlar)", ((int)MindAeonRest.Models.StationType.Bar).ToString()),
        };

        BranchOptions = new() { new SelectListItem("(Şube seçiniz)", "0") };
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(new BranchService().GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }
}
