using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class SystemDefinitionsWindow : Window
{
    private enum Kind { None, Countries, Cities, Districts, Units }

    private readonly LocationService _loc = new();
    private readonly MainUnitService _units = new();
    private Kind _kind = Kind.None;

    public SystemDefinitionsWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("Sistem tanımları yalnızca yöneticiler içindir.", "Yetki"); Loaded += (_, __) => Close(); return; }

        CountryFilter.Configure(() => _loc.GetCountries(), AddCountry);
        CityFilter.Configure(
            () => CountryFilter.SelectedValue is int cid ? _loc.GetCities(cid) : (System.Collections.IEnumerable)Array.Empty<City>(),
            AddCity);

        CountryFilter.SelectionChanged += (_, _) =>
        {
            if (_kind == Kind.Cities) LoadCities();
            else if (_kind == Kind.Districts) { CityFilter.Reload(); LoadDistricts(); }
        };
        CityFilter.SelectionChanged += (_, _) => { if (_kind == Kind.Districts) LoadDistricts(); };
    }

    private void Menu_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && sender is not RadioButton) return;

        _kind =
            sender == MenuCountries ? Kind.Countries :
            sender == MenuCities ? Kind.Cities :
            sender == MenuDistricts ? Kind.Districts :
            sender == MenuUnits ? Kind.Units : Kind.None;

        HintText.Visibility = Visibility.Collapsed;
        CountriesGrid.Visibility = Visibility.Collapsed;
        CitiesGrid.Visibility = Visibility.Collapsed;
        DistrictsGrid.Visibility = Visibility.Collapsed;
        UnitsGrid.Visibility = Visibility.Collapsed;
        FilterPanel.Visibility = Visibility.Collapsed;
        CityFilter.Visibility = Visibility.Collapsed;
        CityFilterLabel.Visibility = Visibility.Collapsed;
        AddButton.Visibility = Visibility.Visible;
        DeleteToggleButton.Visibility = Session.IsSysAdmin ? Visibility.Visible : Visibility.Collapsed;

        switch (_kind)
        {
            case Kind.Countries:
                ContentTitle.Text = "Ülkeler";
                CountriesGrid.Visibility = Visibility.Visible;
                LoadCountries();
                break;
            case Kind.Cities:
                ContentTitle.Text = "Şehirler";
                FilterPanel.Visibility = Visibility.Visible;
                CitiesGrid.Visibility = Visibility.Visible;
                LoadCities();
                break;
            case Kind.Districts:
                ContentTitle.Text = "İlçeler";
                FilterPanel.Visibility = Visibility.Visible;
                CityFilter.Visibility = Visibility.Visible;
                CityFilterLabel.Visibility = Visibility.Visible;
                DistrictsGrid.Visibility = Visibility.Visible;
                LoadDistricts();
                break;
            case Kind.Units:
                ContentTitle.Text = "Ana Birimler";
                UnitsGrid.Visibility = Visibility.Visible;
                LoadUnits();
                break;
        }
    }

    private void LoadCountries() => CountriesGrid.ItemsSource = _loc.GetCountries(includeDeleted: true);
    private void LoadCities() => CitiesGrid.ItemsSource = CountryFilter.SelectedValue is int cid ? _loc.GetCities(cid, includeDeleted: true) : null;
    private void LoadDistricts() => DistrictsGrid.ItemsSource = CityFilter.SelectedValue is int ci ? _loc.GetDistricts(ci, includeDeleted: true) : null;
    private void LoadUnits() => UnitsGrid.ItemsSource = _units.GetAll(includeDeleted: true);

    private void DeleteToggle_Click(object sender, RoutedEventArgs e)
    {
        (DataGrid? grid, string table) = _kind switch
        {
            Kind.Countries => (CountriesGrid, "Countries"),
            Kind.Cities => (CitiesGrid, "Cities"),
            Kind.Districts => (DistrictsGrid, "Districts"),
            Kind.Units => (UnitsGrid, "MainUnits"),
            _ => (null, "")
        };
        if (grid?.SelectedItem is not AuditableEntity ent) { Dialog.Info("Önce bir kayıt seçin.", "Bilgi"); return; }
        bool del = !ent.IsDeleted;
        if (!Dialog.Confirm($"Kayıt {(del ? "silinsin" : "geri alınsın")} mı?", del ? "Sil" : "Geri Al")) return;
        try
        {
            using var c = Database.OpenConnection();   // sistem DB (lokasyon/birim sistem tablolarında)
            Schema.SetDeleted(c, table, ent.Id, del, Session.CurrentUserId);
            ReloadCurrent();
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void ReloadCurrent()
    {
        switch (_kind)
        {
            case Kind.Countries: LoadCountries(); break;
            case Kind.Cities: LoadCities(); break;
            case Kind.Districts: LoadDistricts(); break;
            case Kind.Units: LoadUnits(); break;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        switch (_kind)
        {
            case Kind.Countries: if (AddCountry().HasValue) { LoadCountries(); CountryFilter.Reload(); } break;
            case Kind.Cities: if (AddCity().HasValue) LoadCities(); break;
            case Kind.Districts: if (AddDistrict().HasValue) LoadDistricts(); break;
            case Kind.Units: if (AddUnit().HasValue) LoadUnits(); break;
        }
    }

    private int? AddCountry()
    {
        var w = new QuickAddWindow("Yeni Ülke", "Kod", "Ülke Adı",
            (code, name) => _loc.CreateCountry(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddCity()
    {
        if (CountryFilter.SelectedValue is not int countryId)
        {
            Dialog.Info("Önce ülke seçiniz.", "Uyarı");
            return null;
        }
        var w = new QuickAddWindow("Yeni Şehir", "Plaka", "Şehir Adı",
            (plate, name) => _loc.CreateCity(countryId, name, plate, Session.CurrentUserId)) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddDistrict()
    {
        if (CityFilter.SelectedValue is not int cityId)
        {
            Dialog.Info("Önce şehir seçiniz.", "Uyarı");
            return null;
        }
        var w = new QuickAddWindow("Yeni İlçe", null, "İlçe Adı",
            (_, name) => _loc.CreateDistrict(cityId, name, Session.CurrentUserId)) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddUnit()
    {
        var w = new QuickAddWindow("Yeni Birim", "Kod", "Birim Adı",
            (code, name) => _units.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
