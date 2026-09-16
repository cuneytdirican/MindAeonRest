using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Company;

/// <summary>
/// Şirket Bilgileri (back-office) — tek AKTİF şirketi düzenler. WPF CompanyEditWindow karşılığı.
/// Kod ve veritabanı DEĞİŞTİRİLEMEZ (readonly). Kayıt sistem DB'sine yazılır (CompanyService).
/// </summary>
public class IndexModel : PageModel
{
    private readonly CompanyService _svc = new();
    private readonly CurrencyService _currencySvc = new();
    private readonly LocationService _locationSvc = new();

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? TaxNumber { get; set; }
    [BindProperty] public string? TaxOffice { get; set; }
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public int? CurrencyId { get; set; }
    [BindProperty] public int? CountryId { get; set; }
    [BindProperty] public int? CityId { get; set; }
    [BindProperty] public int? DistrictId { get; set; }
    [BindProperty] public bool RemoveLogoColor { get; set; }
    [BindProperty] public bool RemoveLogoMono { get; set; }

    // Salt-okunur gösterim
    public int Id { get; private set; }
    public string Code { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";

    public bool HasLogoColor { get; private set; }
    public bool HasLogoMono { get; private set; }

    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    public List<SelectListItem> Currencies { get; private set; } = new();
    public List<SelectListItem> Countries { get; private set; } = new();
    public List<SelectListItem> Cities { get; private set; } = new();
    public List<SelectListItem> Districts { get; private set; } = new();

    // Kiracı yöneticisi (SysAdmin/Patron/Yönetici) KENDİ aktif şirketini düzenler — kayıt daima Session.CurrentCompany.Code'dan
    // çözülür (POST'tan id alınmaz → başka şirkete yazılamaz). Feature (lisans) kapısı YOK.
    private static bool CanAccess => Session.IsTenantAdmin;
    /// <summary>Şirketi pasifleştirmek girişi kilitler (pasif şirkete yalnız SysAdmin girer) → aktiflik yalnız SysAdmin.</summary>
    public bool CanChangeActive => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        var company = LoadActiveCompany();
        if (company is null) { Error = "Aktif şirket bulunamadı."; LoadLookups(); return Page(); }

        Id = company.Id;
        Code = company.Code;
        DatabaseName = company.DatabaseName;
        Name = company.Name;
        Phone = company.Phone;
        TaxNumber = company.TaxNumber;
        TaxOffice = company.TaxOffice;
        Address = company.Address;
        IsActive = company.IsActive;
        CurrencyId = company.CurrencyId ?? _currencySvc.GetDefaultId();
        CountryId = company.CountryId;
        CityId = company.CityId;
        DistrictId = company.DistrictId;

        var (color, mono) = _svc.GetLogos(company.Id);
        HasLogoColor = color is { Length: > 0 };
        HasLogoMono = mono is { Length: > 0 };

        LoadLookups();
        return Page();
    }

    public IActionResult OnPost(IFormFile? logoColorFile, IFormFile? logoMonoFile)
    {
        if (!CanAccess) return Forbid();

        var company = LoadActiveCompany();
        if (company is null) { Error = "Aktif şirket bulunamadı."; LoadLookups(); return Page(); }

        // Kod/DatabaseName readonly — daima mevcut kayıttan gelir, POST'tan değil.
        Id = company.Id;
        Code = company.Code;
        DatabaseName = company.DatabaseName;

        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Şirket adını giriniz.";
            RefreshLogoState(company.Id);
            LoadLookups();
            return Page();
        }

        company.Name = Name.Trim();
        company.Phone = Phone;
        company.TaxNumber = TaxNumber;
        company.TaxOffice = TaxOffice;
        company.Address = Address;
        if (CanChangeActive) company.IsActive = IsActive;   // kiracı yöneticisi için mevcut değer korunur
        IsActive = company.IsActive;
        company.CurrencyId = CurrencyId;
        company.CountryId = CountryId;
        company.CityId = CityId;
        company.DistrictId = DistrictId;

        try
        {
            _svc.Update(company, Session.CurrentUserId);

            // Logolar: yeni yüklendiyse onu al; "kaldır" işaretliyse null; aksi halde mevcut logoyu koru.
            var (curColor, curMono) = _svc.GetLogos(company.Id);
            byte[]? color = ResolveLogo(logoColorFile, RemoveLogoColor, curColor);
            byte[]? mono = ResolveLogo(logoMonoFile, RemoveLogoMono, curMono);
            _svc.SaveLogos(company.Id, color, mono, Session.CurrentUserId);

            // Oturumdaki aktif şirketin adı/durumu güncel kalsın.
            if (Session.CurrentCompany is { } cur && cur.Code == company.Code)
            {
                cur.Name = company.Name;
                cur.IsActive = company.IsActive;
            }

            Saved = "Şirket bilgileri kaydedildi.";
            RefreshLogoState(company.Id);
            LoadLookups();
            return Page();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            RefreshLogoState(company.Id);
            LoadLookups();
            return Page();
        }
    }

    /// <summary>Şirket logosu önizleme (color|mono).</summary>
    public IActionResult OnGetLogo(string kind)
    {
        if (!CanAccess) return Forbid();
        var company = LoadActiveCompany();
        if (company is null) return NotFound();
        var (color, mono) = _svc.GetLogos(company.Id);
        var bytes = kind == "mono" ? mono : color;
        return bytes is { Length: > 0 } ? File(bytes, "image/png") : NotFound();
    }

    private MindAeonRest.Models.Company? LoadActiveCompany()
    {
        var code = Session.CurrentCompany?.Code;
        return string.IsNullOrWhiteSpace(code) ? null : _svc.GetByCode(code);
    }

    private void RefreshLogoState(int companyId)
    {
        var (color, mono) = _svc.GetLogos(companyId);
        HasLogoColor = color is { Length: > 0 };
        HasLogoMono = mono is { Length: > 0 };
    }

    private static byte[]? ResolveLogo(IFormFile? file, bool remove, byte[]? current)
    {
        if (file is { Length: > 0 })
        {
            using var ms = new MemoryStream();
            file.CopyTo(ms);
            return ms.ToArray();
        }
        return remove ? null : current;
    }

    private void LoadLookups()
    {
        Currencies = _currencySvc.GetAll()
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString(), c.Id == CurrencyId))
            .ToList();

        Countries = _locationSvc.GetCountries()
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CountryId))
            .ToList();

        if (CountryId is int cid)
            Cities = _locationSvc.GetCities(cid)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CityId))
                .ToList();

        if (CityId is int ci)
            Districts = _locationSvc.GetDistricts(ci)
                .Select(d => new SelectListItem(d.Name, d.Id.ToString(), d.Id == DistrictId))
                .ToList();
    }
}
