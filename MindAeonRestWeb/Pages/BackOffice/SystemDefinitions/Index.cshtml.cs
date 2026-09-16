using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SystemDefinitions;

/// <summary>Sistem Tanımları — tüm şirketlerce ortak kullanılan jenerik tanım listeleri:
/// Ülkeler / Şehirler / İlçeler / Ana Birimler. WPF SystemDefinitionsWindow ile birebir kapsam.
/// Kademeli filtre (Ülke→Şehir, Şehir→İlçe), + Yeni ekleme ve soft-delete Sil/Geri Al.
/// Yalnız yöneticiler (Session.IsSysAdmin). Feature gate yok — tanımlar sistem DB'sinde.</summary>
public class IndexModel : PageModel
{
    private readonly LocationService _loc = new();
    private readonly MainUnitService _units = new();

    // Aktif sekme: countries | cities | districts | units
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "countries";
    // Kademeli filtreler
    [BindProperty(SupportsGet = true)] public int? CountryId { get; set; }
    [BindProperty(SupportsGet = true)] public int? CityId { get; set; }

    public List<Country> Countries { get; private set; } = new();
    public List<City> Cities { get; private set; } = new();
    public List<District> Districts { get; private set; } = new();
    public List<MainUnit> Units { get; private set; } = new();

    // Ülke/Şehir seçicileri için lookup
    public List<Country> CountryOptions { get; private set; } = new();
    public List<City> CityOptions { get; private set; } = new();

    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    // WPF: if (!Session.IsSysAdmin) { ... Close(); } — yalnız yönetici.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet(string? saved = null)
    {
        if (!CanAccess) return Forbid();
        Normalize();
        Load();
        Saved = saved;
        return Page();
    }

    // ---- Ekleme (+ Yeni) ----

    public IActionResult OnPostAddCountry(string? code, string name)
    {
        if (!CanAccess) return Forbid();
        Kind = "countries";
        try { _loc.CreateCountry(code, name ?? "", Session.CurrentUserId); return Ok("Ülke eklendi."); }
        catch (Exception ex) { return Fail(ex); }
    }

    public IActionResult OnPostAddCity(int? countryId, string? plate, string name)
    {
        if (!CanAccess) return Forbid();
        Kind = "cities"; CountryId = countryId;
        if (countryId is not int cid) { Error = "Önce ülke seçiniz."; return Reload(); }
        try { _loc.CreateCity(cid, name ?? "", plate, Session.CurrentUserId); return Ok("Şehir eklendi."); }
        catch (Exception ex) { return Fail(ex); }
    }

    public IActionResult OnPostAddDistrict(int? countryId, int? cityId, string name)
    {
        if (!CanAccess) return Forbid();
        Kind = "districts"; CountryId = countryId; CityId = cityId;
        if (cityId is not int ci) { Error = "Önce şehir seçiniz."; return Reload(); }
        try { _loc.CreateDistrict(ci, name ?? "", Session.CurrentUserId); return Ok("İlçe eklendi."); }
        catch (Exception ex) { return Fail(ex); }
    }

    public IActionResult OnPostAddUnit(string? code, string name)
    {
        if (!CanAccess) return Forbid();
        Kind = "units";
        try { _units.Create(code ?? "", name ?? "", Session.CurrentUserId); return Ok("Birim eklendi."); }
        catch (Exception ex) { return Fail(ex); }
    }

    // ---- Sil / Geri Al (soft-delete, sistem DB, jenerik Schema.SetDeleted) ----

    public IActionResult OnPostToggleDelete(string kind, int id, bool deleted, int? countryId, int? cityId)
    {
        if (!CanAccess) return Forbid();
        Kind = kind; CountryId = countryId; CityId = cityId;
        string table = kind switch
        {
            "countries" => "Countries",
            "cities" => "Cities",
            "districts" => "Districts",
            "units" => "MainUnits",
            _ => ""
        };
        if (table.Length == 0) return RedirectBack(null);
        try
        {
            using var c = Database.OpenConnection();   // sistem DB (lokasyon/birim ortak tablolar)
            Schema.SetDeleted(c, table, id, deleted, Session.CurrentUserId);
            return RedirectBack(deleted ? "Kayıt silindi." : "Kayıt geri alındı.");
        }
        catch (Exception ex) { return Fail(ex); }
    }

    // ---- Yardımcılar ----

    private void Normalize()
    {
        Kind = (Kind ?? "").Trim().ToLowerInvariant();
        if (Kind is not ("countries" or "cities" or "districts" or "units")) Kind = "countries";
    }

    private void Load()
    {
        CountryOptions = _loc.GetCountries();
        // Şehir seçici yalnız ülke seçiliyken dolar (WPF EntityPicker davranışı).
        CityOptions = CountryId is int cid ? _loc.GetCities(cid) : new List<City>();

        switch (Kind)
        {
            case "countries":
                Countries = _loc.GetCountries(includeDeleted: true);
                break;
            case "cities":
                Cities = CountryId is int c ? _loc.GetCities(c, includeDeleted: true) : new List<City>();
                break;
            case "districts":
                Districts = CityId is int ci ? _loc.GetDistricts(ci, includeDeleted: true) : new List<District>();
                break;
            case "units":
                Units = _units.GetAll(includeDeleted: true);
                break;
        }
    }

    private IActionResult Ok(string msg) => RedirectToPage(new { Kind, CountryId, CityId, saved = msg });
    private IActionResult RedirectBack(string? msg) => RedirectToPage(new { Kind, CountryId, CityId, saved = msg });

    private IActionResult Fail(Exception ex)
    {
        Error = ex.Message;
        return Reload();
    }

    private IActionResult Reload()
    {
        Normalize();
        Load();
        return Page();
    }
}
