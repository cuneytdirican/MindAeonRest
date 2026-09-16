using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Customers;

public class EditModel : PageModel
{
    private readonly CustomerService _svc = new();
    private readonly CustomerGroupService _groupSvc = new();
    private readonly PriceListService _priceSvc = new();
    private readonly LocationService _locSvc = new();
    private readonly CodeSeriesService _codeSeries = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int CustomerGroupId { get; set; }        // 0 = seçilmedi
    [BindProperty] public int CustomerType { get; set; } = (int)MindAeonRest.Models.CustomerType.Musteri;
    [BindProperty] public int EInvoiceType { get; set; } = (int)MindAeonRest.Models.EInvoiceType.Yok;
    [BindProperty] public int PriceListId { get; set; }            // 0 = yok
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? TaxNumber { get; set; }
    [BindProperty] public string? TaxOffice { get; set; }
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    /// <summary>Adresler alt-tablosu — form satırlarından indeksli POST (Addresses[i].X).</summary>
    [BindProperty] public List<AddressInput> Addresses { get; set; } = new();

    // Lokasyon (ülke/şehir/ilçe): WPF LocationSelector cascade karşılığı web'de yok.
    // Kayıtlı cari düzenlenirken mevcut lokasyon id'leri gizli alanlarda saklanıp geri yazılır (kayıp önlenir).
    [BindProperty] public int? CountryId { get; set; }
    [BindProperty] public int? CityId { get; set; }
    [BindProperty] public int? DistrictId { get; set; }

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> GroupOptions { get; private set; } = new();
    public List<SelectListItem> PriceListOptions { get; private set; } = new();
    public List<SelectListItem> CustomerTypeOptions { get; private set; } = new();
    public List<SelectListItem> EInvoiceOptions { get; private set; } = new();

    // Lokasyon lookup (WPF LocationSelector cascade karşılığı): tüm veri sayfaya JSON gömülür,
    // JS ile Ülke→Şehir→İlçe kademeli doldurulur (Documents/Edit WarehousesByBranchJson deseni).
    public List<SelectListItem> CountryOptions { get; private set; } = new();
    public string CitiesByCountryJson { get; private set; } = "{}";   // { "countryId": [ {v,t} ] }
    public string DistrictsByCityJson { get; private set; } = "{}";   // { "cityId": [ {v,t} ] }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.BasicCustomer);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var c = _svc.GetById(i);
            if (c is null) return RedirectToPage("Index");
            Id = c.Id;
            Code = c.Code;
            Name = c.Name;
            CustomerGroupId = c.CustomerGroupId ?? 0;
            CustomerType = (int)c.CustomerType;
            EInvoiceType = (int)c.EInvoiceType;
            PriceListId = c.PriceListId ?? 0;
            Phone = c.Phone;
            TaxNumber = c.TaxNumber;
            TaxOffice = c.TaxOffice;
            Address = c.Address;
            IsActive = c.IsActive;
            CountryId = c.CountryId;
            CityId = c.CityId;
            DistrictId = c.DistrictId;
            Addresses = _svc.GetAddresses(c.Id).Select(a => new AddressInput
            {
                Title = a.Title,
                Address = a.Address,
                Phone = a.Phone,
                IsDefault = a.IsDefault,
                CountryId = a.CountryId,
                CityId = a.CityId,
                DistrictId = a.DistrictId
            }).ToList();
        }
        else if (string.IsNullOrWhiteSpace(Code))
        {
            // Yeni kayıt (Id==0): WPF CustomerEditWindow gibi sıradaki otomatik kodu öner.
            // Kullanıcı isterse değiştirebilir; OnPost'ta eşleşirse seri ilerletilir.
            var nextCode = _codeSeries.PeekNext(CodeSeriesType.Customer);
            if (nextCode is not null) Code = nextCode;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        // Boş satırları at (tümüyle boş şablon satırlar).
        Addresses = (Addresses ?? new()).Where(a => !a.IsEmpty).ToList();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ünvan zorunludur.";
            return Page();
        }
        if (!Enum.IsDefined(typeof(CustomerType), CustomerType)) { Error = "Geçerli bir cari tipi seçiniz."; return Page(); }
        if (!Enum.IsDefined(typeof(EInvoiceType), EInvoiceType)) { Error = "Geçerli bir e-belge tipi seçiniz."; return Page(); }

        var customer = new Customer
        {
            Id = Id,
            Code = Code,
            Name = Name,
            CustomerGroupId = CustomerGroupId > 0 ? CustomerGroupId : null,
            CustomerType = (CustomerType)CustomerType,
            EInvoiceType = (EInvoiceType)EInvoiceType,
            PriceListId = PriceListId > 0 ? PriceListId : null,
            Phone = Phone,
            TaxNumber = TaxNumber,
            TaxOffice = TaxOffice,
            Address = Address,
            IsActive = IsActive,
            CountryId = CountryId,
            CityId = CityId,
            DistrictId = DistrictId
        };

        var addresses = Addresses.Select(a => new CustomerAddress
        {
            Title = a.Title,
            Address = a.Address,
            Phone = a.Phone,
            IsDefault = a.IsDefault,
            CountryId = a.CountryId,
            CityId = a.CityId,
            DistrictId = a.DistrictId
        }).ToList();

        // WPF Save_Click paritesi: yeni kayıtta kaydedilecek kod, sıradaki seri koduyla eşleşiyorsa
        // başarılı kayıttan sonra seriyi ilerlet (Advance). Kullanıcı önerilen kodu değiştirdiyse ilerletilmez.
        bool wasNew = Id == 0;
        string? seriesCode = wasNew ? _codeSeries.PeekNext(CodeSeriesType.Customer) : null;

        try
        {
            _svc.Save(customer, addresses, Session.CurrentUserId);   // atomik: cari + adresler tek çağrı
            if (wasNew && seriesCode is not null
                && CodeHelper.Normalize(seriesCode) == CodeHelper.Normalize(customer.Code))
                _codeSeries.Advance(CodeSeriesType.Customer);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// "+" hızlı ekleme (proje kuralı): satır-içi yeni müşteri grubu oluşturur ve JSON döner.
    /// Sayfa yenilenmeden JS listeye ekleyip seçer; form durumu korunur (antiforgery token header'la gönderilir).
    /// </summary>
    public IActionResult OnPostAddGroup(string? code, string? name)
    {
        if (!CanAccess) return new JsonResult(new { ok = false, error = "Yetkiniz yok." });
        try
        {
            int newId = _groupSvc.Create(code ?? "", name ?? "", Session.CurrentUserId);
            var g = _groupSvc.GetById(newId);
            return new JsonResult(new { ok = true, id = newId, text = g?.Display ?? (name ?? "") });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { ok = false, error = ex.Message });
        }
    }

    private void LoadLookups()
    {
        GroupOptions = new() { new SelectListItem("(Grup seçiniz)", "0") };
        GroupOptions.AddRange(_groupSvc.GetAll()
            .Where(g => g.IsActive)
            .Select(g => new SelectListItem(g.Display, g.Id.ToString())));

        PriceListOptions = new() { new SelectListItem("(Yok)", "0") };
        PriceListOptions.AddRange(_priceSvc.GetAll()
            .Where(p => p.IsActive)
            .Select(p => new SelectListItem(p.Display, p.Id.ToString())));

        CustomerTypeOptions = Enum.GetValues<CustomerType>()
            .Select(t => new SelectListItem(t.ToTr(), ((int)t).ToString()))
            .ToList();

        EInvoiceOptions = Enum.GetValues<EInvoiceType>()
            .Select(t => new SelectListItem(t.ToTr(), ((int)t).ToString()))
            .ToList();

        // --- Lokasyon: ülkeler <select>, tüm şehir/ilçe verisi JSON (kademeli JS için) ---
        var countries = _locSvc.GetCountries();
        CountryOptions = new() { new SelectListItem("(Ülke seçiniz)", "") };
        CountryOptions.AddRange(countries.Select(c => new SelectListItem(c.Name, c.Id.ToString())));

        // Her ülkenin şehirleri ve her şehrin ilçeleri (yalnız var olan ülke/şehir için).
        var citiesByCountry = new Dictionary<string, List<object>>();
        var districtsByCity = new Dictionary<string, List<object>>();
        foreach (var country in countries)
        {
            var cities = _locSvc.GetCities(country.Id);
            if (cities.Count > 0)
                citiesByCountry[country.Id.ToString()] = cities
                    .Select(ci => (object)new { v = ci.Id, t = ci.Name }).ToList();
            foreach (var city in cities)
            {
                var districts = _locSvc.GetDistricts(city.Id);
                if (districts.Count > 0)
                    districtsByCity[city.Id.ToString()] = districts
                        .Select(d => (object)new { v = d.Id, t = d.Name }).ToList();
            }
        }
        CitiesByCountryJson = JsonSerializer.Serialize(citiesByCountry);
        DistrictsByCityJson = JsonSerializer.Serialize(districtsByCity);
    }

    /// <summary>Formdan gelen tek adres satırı (indeksli bind).</summary>
    public class AddressInput
    {
        public string? Title { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public bool IsDefault { get; set; }

        // Lokasyon web'de düzenlenmez; mevcut kayıtlar için gizli alanlarla korunur.
        public int? CountryId { get; set; }
        public int? CityId { get; set; }
        public int? DistrictId { get; set; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Title)
                            && string.IsNullOrWhiteSpace(Address)
                            && string.IsNullOrWhiteSpace(Phone);
    }
}
