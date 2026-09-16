using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

public partial class PaketNewWindow : Window
{
    public OrderType OrderType { get; private set; }
    public DeliveryPlatform Platform { get; private set; }
    public string? CustomerName { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }

    public PaketNewWindow()
    {
        InitializeComponent();
        TypeCombo.ItemsSource = new List<ComboItem> { new((int)Models.OrderType.Paket, "Paket Servis"), new((int)Models.OrderType.TakeAway, "Gel-Al (Take Away)") };
        TypeCombo.SelectedIndex = 0;
        PlatformCombo.ItemsSource = Enum.GetValues<DeliveryPlatform>().Select(p => new ComboItem((int)p, p.ToTr())).ToList();
        PlatformCombo.SelectedIndex = 0;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        OrderType = (OrderType)(TypeCombo.SelectedValue ?? 1);
        Platform = (DeliveryPlatform)(PlatformCombo.SelectedValue ?? 0);
        // Paket serviste adres önerilir; Gel-Al'da gerekmez
        if (OrderType == OrderType.Paket && string.IsNullOrWhiteSpace(AddressBox.Text))
        { ErrorText.Text = "Paket servis için adres giriniz."; ErrorText.Visibility = Visibility.Visible; return; }
        CustomerName = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
        Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
