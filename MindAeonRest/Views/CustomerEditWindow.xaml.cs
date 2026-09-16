using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CustomerEditWindow : Window
{
    private readonly CustomerService _service = new();
    private readonly CustomerGroupService _groupService = new();
    private readonly CodeSeriesService _codeSeries = new();
    private readonly Customer _customer;
    private readonly List<CustomerAddress> _addresses = new();

    public int? SavedId => _customer.Id == 0 ? null : _customer.Id;

    public CustomerEditWindow(Customer? existing = null)
    {
        InitializeComponent();
        _customer = existing ?? new Customer();

        GroupPicker.Configure(() => _groupService.GetAll(), AddGroup, existing?.CustomerGroupId);
        EInvoiceCombo.ItemsSource = Enum.GetValues<EInvoiceType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();
        EInvoiceCombo.SelectedValue = (int)(_customer.EInvoiceType);
        CustomerTypeCombo.ItemsSource = Enum.GetValues<CustomerType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();
        CustomerTypeCombo.SelectedValue = (int)(_customer.CustomerType);
        var lists = new List<ComboItem> { new(0, "(Yok)") };
        lists.AddRange(new PriceListService().GetAll().Where(p => p.IsActive).Select(p => new ComboItem(p.Id, p.Display)));
        PriceListCombo.ItemsSource = lists;
        PriceListCombo.SelectedValue = _customer.PriceListId ?? 0;

        if (existing is not null)
        {
            TitleText.Text = $"Müşteri · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            TaxNumberBox.Text = existing.TaxNumber;
            TaxOfficeBox.Text = existing.TaxOffice;
            AddressBox.Text = existing.Address;
            ActiveCheck.IsChecked = existing.IsActive;
            Location.Initialize(existing.CountryId, existing.CityId, existing.DistrictId);
            _addresses.AddRange(_service.GetAddresses(existing.Id));
        }
        else
        {
            Location.Initialize();
            var nextCode = _codeSeries.PeekNext(CodeSeriesType.Customer);
            if (nextCode is not null) CodeBox.Text = nextCode;
        }

        RefreshAddresses();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private int? AddGroup()
    {
        var w = new QuickAddWindow("Yeni Müşteri Grubu", "Kod", "Grup Adı",
            (code, name) => _groupService.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    // ===== Adresler =====
    private void RefreshAddresses() { AddressGrid.ItemsSource = null; AddressGrid.ItemsSource = _addresses; }

    private void AddAddress_Click(object sender, RoutedEventArgs e)
    {
        var w = new CustomerAddressEditWindow { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _addresses.Add(w.Result); EnforceDefault(w.Result); RefreshAddresses(); }
    }

    private void EditAddress_Click(object sender, RoutedEventArgs e)
    {
        if (AddressGrid.SelectedItem is not CustomerAddress sel) { ShowError("Önce bir adres seçin."); return; }
        int idx = _addresses.IndexOf(sel);
        var w = new CustomerAddressEditWindow(sel) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _addresses[idx] = w.Result; EnforceDefault(w.Result); RefreshAddresses(); }
    }

    private void RemoveAddress_Click(object sender, RoutedEventArgs e)
    {
        if (AddressGrid.SelectedItem is CustomerAddress sel) { _addresses.Remove(sel); RefreshAddresses(); }
    }

    private void EnforceDefault(CustomerAddress def)
    {
        if (!def.IsDefault) return;
        foreach (var a in _addresses) a.IsDefault = ReferenceEquals(a, def);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir cari kodu giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Cari adını giriniz."); return; }

        _customer.Code = CodeBox.Text;
        _customer.Name = NameBox.Text.Trim();
        _customer.CustomerGroupId = GroupPicker.SelectedValue;
        _customer.Phone = PhoneBox.Text;
        _customer.TaxNumber = TaxNumberBox.Text;
        _customer.TaxOffice = TaxOfficeBox.Text;
        _customer.Address = AddressBox.Text;
        _customer.IsActive = ActiveCheck.IsChecked == true;
        _customer.EInvoiceType = EInvoiceCombo.SelectedValue is int ei ? (EInvoiceType)ei : EInvoiceType.Yok;
        _customer.CustomerType = CustomerTypeCombo.SelectedValue is int ct ? (CustomerType)ct : CustomerType.Musteri;
        _customer.PriceListId = PriceListCombo.SelectedValue is int pl && pl > 0 ? pl : null;
        _customer.CountryId = Location.CountryId;
        _customer.CityId = Location.CityId;
        _customer.DistrictId = Location.DistrictId;

        bool wasNew = _customer.Id == 0;
        string? seriesCode = wasNew ? _codeSeries.PeekNext(CodeSeriesType.Customer) : null;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_customer, _addresses, Session.CurrentUserId);
            if (wasNew && seriesCode is not null && CodeHelper.Normalize(seriesCode) == _customer.Code)
                _codeSeries.Advance(CodeSeriesType.Customer);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
