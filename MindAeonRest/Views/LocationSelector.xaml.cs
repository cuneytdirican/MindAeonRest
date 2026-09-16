using System.Windows;
using System.Windows.Controls;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Kademeli Ülke→�?ehir→İlçe seçici. Her seviyede "+" ile ekrandan çıkmadan tanımlama yapılır.</summary>
public partial class LocationSelector : UserControl
{
    private readonly LocationService _loc = new();

    public LocationSelector()
    {
        InitializeComponent();
        CountryPicker.PlaceholderText = "Ülke seçin";
        CityPicker.PlaceholderText = "�?ehir seçin";
        DistrictPicker.PlaceholderText = "İlçe seçin";

        CountryPicker.SelectionChanged += (_, _) => { CityPicker.Reload(); DistrictPicker.Clear(); };
        CityPicker.SelectionChanged += (_, _) => DistrictPicker.Reload();
    }

    public int? CountryId => CountryPicker.SelectedValue;
    public int? CityId => CityPicker.SelectedValue;
    public int? DistrictId => DistrictPicker.SelectedValue;

    public void Initialize(int? countryId = null, int? cityId = null, int? districtId = null)
    {
        CountryPicker.Configure(() => _loc.GetCountries(), AddCountry, countryId);
        CityPicker.Configure(
            () => CountryPicker.SelectedValue is int cid ? _loc.GetCities(cid) : (System.Collections.IEnumerable)Array.Empty<City>(),
            AddCity, cityId);
        DistrictPicker.Configure(
            () => CityPicker.SelectedValue is int ci ? _loc.GetDistricts(ci) : (System.Collections.IEnumerable)Array.Empty<District>(),
            AddDistrict, districtId);
    }

    private int? AddCountry()
    {
        var w = new QuickAddWindow("Yeni Ülke", "Kod", "Ülke Adı",
            (code, name) => _loc.CreateCountry(code, name, Session.CurrentUserId), field1Upper: true)
        { Owner = Window.GetWindow(this) };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddCity()
    {
        if (CountryPicker.SelectedValue is not int countryId)
        {
            Dialog.Info("Önce ülke seçiniz.", "Uyarı");
            return null;
        }
        var w = new QuickAddWindow("Yeni �?ehir", "Plaka", "�?ehir Adı",
            (plate, name) => _loc.CreateCity(countryId, name, plate, Session.CurrentUserId))
        { Owner = Window.GetWindow(this) };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddDistrict()
    {
        if (CityPicker.SelectedValue is not int cityId)
        {
            Dialog.Info("Önce şehir seçiniz.", "Uyarı");
            return null;
        }
        var w = new QuickAddWindow("Yeni İlçe", null, "İlçe Adı",
            (_, name) => _loc.CreateDistrict(cityId, name, Session.CurrentUserId))
        { Owner = Window.GetWindow(this) };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }
}
