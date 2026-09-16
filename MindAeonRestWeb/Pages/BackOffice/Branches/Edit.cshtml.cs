using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Branches;

public class EditModel : PageModel
{
    private readonly BranchService _svc = new();
    private readonly LocationService _loc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    /// <summary>Gün başlangıç saati HH:mm (BranchEditWindow deseni → DayStartMinutes = saat*60+dakika).</summary>
    [BindProperty] public string DayStart { get; set; } = "00:00";
    [BindProperty] public bool IsActive { get; set; } = true;

    // WPF BranchEditWindow'da olup web'de eksik olan alanlar.
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? TaxNumber { get; set; }
    [BindProperty] public string? TaxOffice { get; set; }
    [BindProperty] public string? Address { get; set; }

    // Lokasyon (WPF LocationSelector cascade karşılığı): Ülke → Şehir → İlçe.
    [BindProperty] public int? CountryId { get; set; }
    [BindProperty] public int? CityId { get; set; }
    [BindProperty] public int? DistrictId { get; set; }

    /// <summary>
    /// Kademeli combo için tüm lokasyon verisi tek JSON'da:
    /// { countries:[{id,name}], cities:{countryId:[{id,name}]}, districts:{cityId:[{id,name}]} }.
    /// (Customers ekranındaki JSON+JS deseninin Branches'e bağımsız uyarlaması.)
    /// </summary>
    public string LocationJson { get; private set; } = "{}";

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    private static bool CanAccess => Session.IsTenantAdmin;

    /// <summary>
    /// Web'de Session.CurrentCompany.Id dolu değildir; koddan çözülür (AuthController.MyBranches deseni).
    /// </summary>
    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        BuildLocationJson();
        if (id is int i && i > 0)
        {
            var b = _svc.GetById(i);
            // KİRACI İZOLASYONU: başka şirketin şubesi id ile açılamaz (bilgi sızıntısı/IDOR).
            if (b is null || b.CompanyId != ResolveCompanyId()) return RedirectToPage("Index");
            Id = b.Id;
            Code = b.Code;
            Name = b.Name;
            IsActive = b.IsActive;
            DayStart = b.DayStartText;
            Phone = b.Phone;
            TaxNumber = b.TaxNumber;
            TaxOffice = b.TaxOffice;
            Address = b.Address;
            CountryId = b.CountryId;
            CityId = b.CityId;
            DistrictId = b.DistrictId;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        BuildLocationJson();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }

        var companyId = ResolveCompanyId();
        if (companyId is null)
        {
            Error = "Aktif şirket çözümlenemedi.";
            return Page();
        }

        // KİRACI İZOLASYONU: Update "WHERE Id=@Id" ile çalışır (CompanyId süzmez) → güncellenecek şube AKTİF şirkete ait olmalı.
        if (!IsNew && _svc.GetById(Id)?.CompanyId != companyId.Value) return Forbid();

        var branch = new CompanyBranch
        {
            Id = Id,
            CompanyId = companyId.Value,
            Code = Code,
            Name = Name,
            IsActive = IsActive,
            DayStartMinutes = ParseDayStart(DayStart),
            Phone = Phone,
            TaxNumber = TaxNumber,
            TaxOffice = TaxOffice,
            Address = Address,
            CountryId = CountryId > 0 ? CountryId : null,
            CityId = CityId > 0 ? CityId : null,
            DistrictId = DistrictId > 0 ? DistrictId : null
        };

        try
        {
            if (IsNew) _svc.Create(branch, Session.CurrentUserId);
            else _svc.Update(branch, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>Ülke/şehir/ilçe verisini kademeli combo için tek JSON'a serialize eder.</summary>
    private void BuildLocationJson()
    {
        var countries = _loc.GetCountries()
            .Select(c => new { id = c.Id, name = c.Name })
            .ToList();

        var cities = new Dictionary<string, object>();
        var districts = new Dictionary<string, object>();
        foreach (var c in countries)
        {
            var cityList = _loc.GetCities(c.id)
                .Select(ci => new { id = ci.Id, name = ci.Name })
                .ToList();
            cities[c.id.ToString()] = cityList;
            foreach (var ci in cityList)
            {
                districts[ci.id.ToString()] = _loc.GetDistricts(ci.id)
                    .Select(d => new { id = d.Id, name = d.Name })
                    .ToList();
            }
        }

        LocationJson = System.Text.Json.JsonSerializer.Serialize(new { countries, cities, districts });
    }

    /// <summary>HH:mm → toplam dakika. Geçersiz/boş girişte 0 (gece yarısı).</summary>
    private static int ParseDayStart(string? text)
    {
        if (TimeSpan.TryParseExact((text ?? "").Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
            return (int)ts.TotalMinutes;
        return 0;
    }
}
