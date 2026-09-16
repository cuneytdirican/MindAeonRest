using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CompanyEditWindow : Window
{
    private readonly CompanyService _companyService = new();
    private readonly Company _company;
    private byte[]? _logoColor;
    private byte[]? _logoMono;

    public CompanyEditWindow(Company company)
    {
        InitializeComponent();
        _company = company;

        TitleText.Text = $"Şirket Düzenle · {company.Code}";
        CodeBox.Text = company.Code;
        NameBox.Text = company.Name;
        PhoneBox.Text = company.Phone;
        TaxNumberBox.Text = company.TaxNumber;
        TaxOfficeBox.Text = company.TaxOffice;
        AddressBox.Text = company.Address;
        ActiveCheck.IsChecked = company.IsActive;
        CurrencyCombo.ItemsSource = new CurrencyService().GetAll();
        CurrencyCombo.SelectedValue = company.CurrencyId ?? new CurrencyService().GetDefaultId();
        Location.Initialize(company.CountryId, company.CityId, company.DistrictId);

        var (color, mono) = _companyService.GetLogos(company.Id);
        _logoColor = color; _logoMono = mono;
        ColorPreview.Source = ToImage(color);
        MonoPreview.Source = ToImage(mono);

        Loaded += (_, _) => NameBox.Focus();
    }

    private static BitmapImage? ToImage(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        var img = new BitmapImage();
        using var ms = new MemoryStream(data);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private byte[]? PickImage()
    {
        var dlg = new OpenFileDialog { Filter = "Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Logo seç" };
        return dlg.ShowDialog() == true ? File.ReadAllBytes(dlg.FileName) : null;
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var b = PickImage(); if (b is null) return;
        _logoColor = b; ColorPreview.Source = ToImage(b);
    }
    private void PickMono_Click(object sender, RoutedEventArgs e)
    {
        var b = PickImage(); if (b is null) return;
        _logoMono = b; MonoPreview.Source = ToImage(b);
    }
    private void ClearColor_Click(object sender, RoutedEventArgs e) { _logoColor = null; ColorPreview.Source = null; }
    private void ClearMono_Click(object sender, RoutedEventArgs e) { _logoMono = null; MonoPreview.Source = null; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Şirket adını giriniz.");
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            _company.Name = NameBox.Text.Trim();
            _company.Phone = PhoneBox.Text;
            _company.TaxNumber = TaxNumberBox.Text;
            _company.TaxOffice = TaxOfficeBox.Text;
            _company.Address = AddressBox.Text;
            _company.IsActive = ActiveCheck.IsChecked == true;
            _company.CountryId = Location.CountryId;
            _company.CityId = Location.CityId;
            _company.DistrictId = Location.DistrictId;
            _company.CurrencyId = CurrencyCombo.SelectedValue as int?;

            _companyService.Update(_company, Session.CurrentUserId);
            _companyService.SaveLogos(_company.Id, _logoColor, _logoMono, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
