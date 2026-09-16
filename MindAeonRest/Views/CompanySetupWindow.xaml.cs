using System.Windows;
using System.Windows.Input;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CompanySetupWindow : Window
{
    private readonly CompanyService _companyService = new();
    private readonly AuthService _authService = new();

    private User? _admin;            // doğrulanmış / önceden doğrulanmış yönetici
    private readonly bool _preAuthenticated;

    public Company? CreatedCompany { get; private set; }
    public User? AuthenticatedAdmin => _admin;

    public CompanySetupWindow(string? codePrefill = null, User? authenticatedAdmin = null)
    {
        InitializeComponent();
        _admin = authenticatedAdmin;
        _preAuthenticated = authenticatedAdmin is not null;

        if (_preAuthenticated)
            AuthPanel.Visibility = Visibility.Collapsed;

        CodeBox.Text = CodeHelper.Normalize(codePrefill);

        // Döviz listesi (varsayılan TRY)
        var currencies = new CurrencyService().GetAll();
        CurrencyCombo.ItemsSource = currencies;
        CurrencyCombo.SelectedValue = new CurrencyService().GetDefaultId();

        UpdateDbPreview();
        Loaded += (_, _) =>
        {
            if (!_preAuthenticated) AdminPassBox.Focus();
            else if (string.IsNullOrEmpty(CodeBox.Text)) CodeBox.Focus();
            else NameBox.Focus();
        };
    }

    private void CodeBox_TextChanged(object sender, RoutedEventArgs e) => UpdateDbPreview();

    private void UpdateDbPreview()
    {
        string code = CodeHelper.Normalize(CodeBox.Text);
        DbNamePreview.Text = code.Length > 0
            ? $"Veritabanı: {CompanyDatabase.NameFor(code)}"
            : "Kod yalnızca harf/rakam olmalıdır (boşluk ve Türkçe karakter kaldırılır).";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        string code = CodeHelper.Normalize(CodeBox.Text);
        string name = NameBox.Text.Trim();
        int? currencyId = CurrencyCombo.SelectedValue as int?;

        if (code.Length == 0) { ShowError("Geçerli bir şirket kodu giriniz."); return; }
        if (string.IsNullOrWhiteSpace(name)) { ShowError("Şirket adını giriniz."); return; }

        try
        {
            CreateButton.IsEnabled = false;

            // Yönetici doğrulaması (önceden doğrulanmadıysa)
            if (!_preAuthenticated && !AuthenticateAdmin())
                return;

            CreatedCompany = _companyService.Create(code, name, currencyId, _admin?.Id);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private bool AuthenticateAdmin()
    {
        string user = AdminUserBox.Text.Trim();
        string pass = AdminPassBox.Password;
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            ShowError("Yönetici kullanıcı adı ve parolasını giriniz.");
            return false;
        }

        var result = _authService.Authenticate(user, pass, out var admin);
        if (result != AuthService.AuthResult.Success)
        {
            ShowError("Yönetici kullanıcı adı veya parola hatalı.");
            return false;
        }
        if (!admin!.RoleName.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Şirket kurmak için SysAdmin yetkisi gereklidir.");
            return false;
        }

        _admin = admin;
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
