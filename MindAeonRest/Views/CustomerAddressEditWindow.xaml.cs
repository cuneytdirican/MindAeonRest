using System.Windows;
using System.Windows.Input;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

public partial class CustomerAddressEditWindow : Window
{
    public CustomerAddress? Result { get; private set; }

    public CustomerAddressEditWindow(CustomerAddress? existing = null)
    {
        InitializeComponent();
        if (existing is not null)
        {
            TitleBox.Text = existing.Title;
            PhoneBox.Text = existing.Phone;
            AddressBox.Text = existing.Address;
            DefaultCheck.IsChecked = existing.IsDefault;
            Location.Initialize(existing.CountryId, existing.CityId, existing.DistrictId);
        }
        else
        {
            Location.Initialize();
        }
        Loaded += (_, _) => TitleBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new CustomerAddress
        {
            Title = TitleBox.Text.Trim(),
            Phone = PhoneBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            IsDefault = DefaultCheck.IsChecked == true,
            CountryId = Location.CountryId,
            CityId = Location.CityId,
            DistrictId = Location.DistrictId
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
