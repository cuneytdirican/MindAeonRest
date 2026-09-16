using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class EInvoiceSettingsWindow : Window
{
    private readonly EInvoiceService _service = new();
    private readonly EInvoiceSettings _settings;

    public EInvoiceSettingsWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("e-Fatura ayarları yalnızca yöneticiler içindir.", "Yetki"); Loaded += (_, __) => Close(); return; }
        ProviderCombo.ItemsSource = Enum.GetValues<EInvoiceProvider>().Select(p => new ComboItem((int)p, p.ToTr())).ToList();
        _settings = _service.GetSettings();
        ProviderCombo.SelectedValue = (int)_settings.Provider;
        UserBox.Text = _settings.Username;
        PassBox.Text = _settings.Password;
        TestCheck.IsChecked = _settings.IsTest;
        EnabledCheck.IsChecked = _settings.IsEnabled;
        Loaded += (_, _) => ProviderCombo.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Provider = (EInvoiceProvider)(ProviderCombo.SelectedValue ?? 0);
        _settings.Username = UserBox.Text;
        _settings.Password = PassBox.Text;
        _settings.IsTest = TestCheck.IsChecked == true;
        _settings.IsEnabled = EnabledCheck.IsChecked == true;
        try { _service.SaveSettings(_settings, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
