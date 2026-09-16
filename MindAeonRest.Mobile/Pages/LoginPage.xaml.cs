using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;

    // needBranch akışında sunucudan gelen şubeler; seçilince aynı kimlikle 2. login yapılır.
    private List<BranchDto> _branches = new();

    public LoginPage(AppState state, ApiClient api)
    {
        InitializeComponent();
        _state = state;
        _api = api;
    }

    private async void Login_Clicked(object sender, EventArgs e)
    {
        Error(null);
        var code = CompanyCodeEntry.Text?.Trim() ?? "";
        var user = UsernameEntry.Text?.Trim() ?? "";
        var pass = PasswordEntry.Text ?? "";

        if (code.Length == 0) { Error("Şirket kodu girin."); return; }
        if (user.Length == 0) { Error("Kullanıcı adı girin."); return; }
        if (pass.Length == 0) { Error("Parola girin."); return; }

        // Şube bölümü görünürse ve seçim yapılmışsa branchId gönderilir (2. aşama).
        int? branchId = BranchSection.IsVisible ? (BranchPicker.SelectedItem as BranchDto)?.Id : null;
        if (BranchSection.IsVisible && branchId is null) { Error("Şube seçin."); return; }

        LoginBtn.IsEnabled = false;
        LoginBtn.Text = "Giriş yapılıyor…";
        try
        {
            var login = await _api.LoginAsync(new LoginRequest(code, user, pass, branchId));

            // Kimlik doğru ama şube seçimi gerekiyor → şubeleri göster, kullanıcı seçip tekrar giriş yapar.
            if (login.NeedBranch || string.IsNullOrEmpty(login.Token))
            {
                _branches = login.Branches ?? new();
                BranchPicker.ItemsSource = _branches;
                if (_branches.Count == 1) BranchPicker.SelectedIndex = 0;
                BranchSection.IsVisible = true;
                Error(_branches.Count == 0 ? "Bu kullanıcı için şube bulunamadı." : "Lütfen şube seçip tekrar giriş yapın.");
                return;
            }

            PasswordEntry.Text = "";
            (Application.Current as App)?.GoToLanding(login.LandingScreen ?? "");
        }
        catch (Exception ex) { Error(ex.Message); }
        finally { LoginBtn.IsEnabled = true; LoginBtn.Text = "GİRİŞ YAP"; }
    }

    private void Config_Clicked(object sender, EventArgs e) => (Application.Current as App)?.GoTo<ConfigPage>();

    private void Error(string? msg)
    {
        ErrorLabel.Text = msg ?? "";
        ErrorLabel.IsVisible = !string.IsNullOrEmpty(msg);
    }
}
