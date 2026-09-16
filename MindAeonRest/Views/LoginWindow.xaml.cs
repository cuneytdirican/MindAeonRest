using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class LoginWindow : Window
{
    private readonly LoginService _loginService = new();
    private readonly CompanyService _companyService = new();
    private readonly BranchService _branchService = new();
    private readonly UserService _userService = new();
    private List<User> _loginUsers = new();   // seçili şirket+şube için giriş yapabilecek kullanıcılar

    private bool _suppressCompanyChange;
    private bool _themeReady;
    private LoginPreferences _prefs = new();

    public LoginWindow()
    {
        InitializeComponent();
        // Bu terminalin KIOSK numarası (appsettings.json) — login'de çok belirgin göster
        KioskBadgeText.Text = $"KIOSK No {AppConfig.KioskNo}";
        KioskChipText.Text = $"🖥 KIOSK #{AppConfig.KioskNo}";
        Loaded += (_, _) =>
        {
            _prefs = LoginPreferences.Load();
            ThemeCombo.ItemsSource = ThemeManager.Options;
            ThemeCombo.SelectedValue = ThemeManager.Current;
            _themeReady = true;
            LoadCompanies();
            ApplyRemembered();
            CompanyCombo.Focus();
        };
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeReady) return;
        if (ThemeCombo.SelectedValue is AppTheme theme && theme != ThemeManager.Current)
        {
            ThemeManager.ChangeAndSave(theme);
            var w = new LoginWindow();
            Application.Current.MainWindow = w;
            w.Show();
            Close();
        }
    }

    /// <summary>Son giriş yapılan şirket/şube/kullanıcı bilgisini doldurur.</summary>
    private void ApplyRemembered()
    {
        if (!string.IsNullOrEmpty(_prefs.LastCompanyCode) &&
            CompanyCombo.ItemsSource is IEnumerable<CompanyListItem> items)
        {
            var match = items.FirstOrDefault(i => i.Company?.Code == _prefs.LastCompanyCode);
            if (match is not null)
            {
                CompanyCombo.SelectedItem = match; // şubeleri yükler
                if (_prefs.LastBranchId.HasValue)
                    BranchCombo.SelectedValue = _prefs.LastBranchId.Value;
            }
        }

        if (!string.IsNullOrEmpty(_prefs.LastUsername))
            UsernameBox.Text = _prefs.LastUsername;
    }

    /// <summary>Şirketleri listeye doldurur; en sona "Yeni Şirket Kur" seçeneğini ekler.</summary>
    private void LoadCompanies(int? selectCompanyId = null)
    {
        List<CompanyListItem> items;
        try
        {
            items = _companyService.GetAll().Select(c => new CompanyListItem(c)).ToList();
        }
        catch (Exception ex)
        {
            ShowError("Şirketler yüklenemedi: " + ex.Message);
            items = new List<CompanyListItem>();
        }
        items.Add(CompanyListItem.NewItem());

        _suppressCompanyChange = true;
        CompanyCombo.ItemsSource = items;
        CompanyCombo.SelectedItem = null;
        _suppressCompanyChange = false;

        BranchCombo.ItemsSource = null;
        BranchCombo.Tag = "Önce şirket";

        if (selectCompanyId.HasValue)
        {
            var match = items.FirstOrDefault(i => i.Company?.Id == selectCompanyId.Value);
            if (match is not null)
                CompanyCombo.SelectedItem = match; // SelectionChanged şubeleri yükler
        }
    }

    private void CompanyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCompanyChange) return;
        HideError();

        BranchCombo.ItemsSource = null;
        BranchCombo.Tag = "Önce şirket";
        CompanyLogo.Visibility = Visibility.Collapsed;

        if (CompanyCombo.SelectedItem is not CompanyListItem item)
            return;

        if (item.IsNew)
        {
            StartNewCompany();
            return;
        }

        LoadBranches(item.Company!);
        ShowLogo(item.Company!);
    }

    private void ShowLogo(Company company)
    {
        try
        {
            var (color, _) = _companyService.GetLogos(company.Id);
            if (color is null || color.Length == 0) { CompanyLogo.Visibility = Visibility.Collapsed; return; }
            var img = new BitmapImage();
            using var ms = new MemoryStream(color);
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
            CompanyLogo.Source = img;
            CompanyLogo.Visibility = Visibility.Visible;
        }
        catch { CompanyLogo.Visibility = Visibility.Collapsed; }
    }

    private void LoadBranches(Company company)
    {
        try
        {
            var branches = _branchService.GetActiveByCompany(company.Id);
            BranchCombo.ItemsSource = branches;
            BranchCombo.Tag = branches.Count == 0 ? "Şube yok" : "Şube seçin";
            if (branches.Count == 1)
                BranchCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError("Şubeler yüklenemedi: " + ex.Message);
        }
    }

    /// <summary>"Yeni Şirket Kur" seçildiğinde: DB bağlantısı varsa kurulum penceresini açar.</summary>
    private void StartNewCompany()
    {
        var (ok, error) = Database.TestConnection();
        if (!ok)
        {
            ShowError("Veritabanı bağlantısı kurulamadı, şirket kurulamaz.\n" + error);
            ResetCompanySelection();
            return;
        }

        var setup = new CompanySetupWindow() { Owner = this };
        if (setup.ShowDialog() == true && setup.CreatedCompany is not null && setup.AuthenticatedAdmin is not null)
        {
            // Yönetici doğrulandı ve şirket kuruldu → doğrudan giriş yap
            var branch = _branchService.GetActiveByCompany(setup.CreatedCompany.Id).FirstOrDefault();
            EnterApplication(setup.AuthenticatedAdmin, setup.CreatedCompany, branch);
        }
        else
        {
            ResetCompanySelection();
        }
    }

    private void ResetCompanySelection()
    {
        _suppressCompanyChange = true;
        CompanyCombo.SelectedItem = null;
        _suppressCompanyChange = false;
        BranchCombo.ItemsSource = null;
        BranchCombo.Tag = "Önce şirket";
    }

    // ===== KIOSK numerik tuş takımı =====
    private void KeypadDigit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digit })
            PasswordBox.Password += digit;
    }

    private void KeypadBack_Click(object sender, RoutedEventArgs e)
    {
        string current = PasswordBox.Password;
        if (current.Length > 0)
            PasswordBox.Password = current[..^1];
    }

    private void KeypadClear_Click(object sender, RoutedEventArgs e) => PasswordBox.Password = string.Empty;

    /// <summary>Seçili şirket+şubeye girebilecek kullanıcıları listeler (kiosk için).</summary>
    private void PickUser_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CompanyCombo.SelectedItem is not CompanyListItem item || item.IsNew || item.Company is null)
        {
            ShowError("Önce şirket seçiniz.");
            return;
        }

        int? branchId = BranchCombo.SelectedValue as int?;
        string branchName = (BranchCombo.SelectedItem as CompanyBranch)?.Name ?? "Tüm şubeler";
        string subtitle = $"{item.Company.Name} · {branchName}";

        var picker = new UserPickerWindow(item.Company.Id, branchId, subtitle) { Owner = this };
        if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedUsername))
        {
            UsernameBox.Text = picker.SelectedUsername;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    // ŞUBE seçilince: o şirket+şubeye giriş yapabilecek kullanıcıları (sistem DB'den, login öncesi) çek,
    // ROL listesini doldur. (GetForLogin RoleName ile döner; SysAdmin tüm şubelerde görünür.)
    private void BranchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadLoginUsers();

    private void LoadLoginUsers()
    {
        if (RoleCombo is null || UserCombo is null) return;   // init sırasında erken tetiklenmeye karşı
        RoleCombo.ItemsSource = null;
        UserCombo.ItemsSource = null;
        if (UsernameBox != null) UsernameBox.Text = string.Empty;

        if (CompanyCombo.SelectedItem is not CompanyListItem item || item.IsNew || item.Company is null)
            return;

        int? branchId = BranchCombo.SelectedValue as int?;
        try { _loginUsers = _userService.GetForLogin(item.Company.Id, branchId); }
        catch { _loginUsers = new List<User>(); }

        var roles = _loginUsers.Select(u => u.RoleName)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r).ToList();
        RoleCombo.ItemsSource = roles;
        if (roles.Count == 1) RoleCombo.SelectedIndex = 0;   // tek rol varsa otomatik seç
    }

    // ROL seçilince: o role sahip kullanıcıları KULLANICI listesine doldur.
    private void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserCombo is null) return;
        if (UsernameBox != null) UsernameBox.Text = string.Empty;
        UserCombo.ItemsSource = null;
        if (RoleCombo.SelectedItem is string role)
        {
            var users = _loginUsers
                .Where(u => string.Equals(u.RoleName, role, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(u => u.FullName).ToList();
            UserCombo.ItemsSource = users;
            if (users.Count == 1) UserCombo.SelectedIndex = 0;   // tek kullanıcı varsa otomatik seç
        }
    }

    // KULLANICI seçilince: gizli username alanını doldur (TryLogin bunu okur), parolaya odaklan.
    private void UserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsernameBox is null) return;
        if (UserCombo.SelectedItem is User u)
        {
            UsernameBox.Text = u.Username;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryLogin();
    }

    private void TryLogin()
    {
        HideError();

        if (CompanyCombo.SelectedItem is not CompanyListItem item || item.IsNew || item.Company is null)
        {
            ShowError("Lütfen şirket seçiniz.");
            return;
        }

        string company = item.Company.Code;
        string username = UsernameBox.Text.Trim();
        string password = PasswordBox.Password;
        int? branchId = BranchCombo.SelectedValue as int?;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Lütfen kullanıcı adı ve parolayı giriniz.");
            return;
        }

        try
        {
            LoginButton.IsEnabled = false;
            var outcome = _loginService.Login(company, branchId, username, password);

            switch (outcome.Status)
            {
                case LoginStatus.Success:
                    EnterApplication(outcome.User!, outcome.Company!, outcome.Branch);
                    break;

                case LoginStatus.CompanyNeedsSetup:
                    HandleCompanySetup(outcome);
                    break;

                default:
                    ShowError(outcome.Message ?? "Giriş başarısız.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Giriş sırasında hata oluştu: {ex.Message}");
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void HandleCompanySetup(LoginOutcome outcome)
    {
        var setup = new CompanySetupWindow(outcome.NormalizedCompanyCode, outcome.User) { Owner = this };
        if (setup.ShowDialog() != true || setup.CreatedCompany is null)
            return;

        var company = setup.CreatedCompany;
        var branch = _branchService.GetActiveByCompany(company.Id).FirstOrDefault();
        EnterApplication(outcome.User!, company, branch);
    }

    private void EnterApplication(User user, Company company, CompanyBranch? branch)
    {
        Session.CurrentUser = user;
        Session.CurrentCompany = company;
        Session.CurrentBranch = branch;
        // Çoklu rol: kullanıcının tüm rollerini oturuma yükle (yetki birleşimi + ekran yönlendirme)
        var roles = new UserRoleService().GetRoleNames(user.Id);
        Session.CurrentRoles = roles.Count > 0 ? roles : new List<string> { user.RoleName };

        // KIOSKNO (terminal kimliği): appsettings.json'dan oku, DEĞİŞTİRİLEMEZ. KIOSK kaydını garanti et.
        Session.CurrentKioskNo = AppConfig.KioskNo;
        Session.CurrentKioskId = null;
        if (branch is not null)
        {
            try { Session.CurrentKioskId = new KioskService().EnsureForBranch(branch.Id, Session.CurrentKioskNo, user.Id).Id; }
            catch { /* KIOSK çözümlenemezse oturum yine de açılır; kapsam filtresi varsayılana düşer */ }
        }

        // Son giriş bilgisini sakla (tema tercihini koruyarak)
        var prefs = LoginPreferences.Load();
        prefs.LastCompanyCode = company.Code;
        prefs.LastBranchId = branch?.Id;
        prefs.LastUsername = user.Username;
        prefs.Save();

        // TCMB döviz kurlarını arka planda güncelle (giriş akışını bloklamaz)
        System.Threading.Tasks.Task.Run(() => new CurrencyRateService().EnsureUpToDate());

        // LİSANS KONTROLÜ (şirket/DB bazlı): geçerli lisans ya da deneme yoksa lisans ekranını aç; yine yoksa girişi engelle.
        try
        {
            var lic = new LicenseService().GetStatus(company.Code, Licensing.HardwareId.Current);
            Session.CurrentLicense = lic;
            // Lisanslı DEĞİLSE (deneme/süre dolmuş/geçersiz) lisans ekranını aç. Deneme geçerliyse "Devam" ile girilir.
            if (lic.State != Models.LicenseState.Licensed)
            {
                var lw = new LicenseWindow(company.Code, company.Name, lic) { Owner = this };
                lw.ShowDialog();
                Session.CurrentLicense = lw.Status;
                if (!lw.Usable) { Session.Clear(); Dialog.Warn("Geçerli bir lisans olmadan uygulamaya girilemez.", "Lisans"); return; }
            }
        }
        catch (Exception ex) { Session.Clear(); Dialog.Error("Lisans kontrolü yapılamadı: " + ex.Message, "Lisans"); return; }

        // Online heartbeat (revoke tespiti) + ciro/kullanım raporu — en iyi çaba, girişi BLOKLAMAZ.
        if (LicenceApiClient.IsConfigured && Session.CurrentLicense?.State == Models.LicenseState.Licensed)
        {
            string cc = company.Code, hw = Licensing.HardwareId.Current;
            int branches = 0, users = 0;
            try { branches = new BranchService().GetActiveByCompany(company.Id).Count; } catch { }
            try { users = new UserService().GetForCompany(company.Id).Count; } catch { }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await new LicenceApiClient().ReportUsageAsync(cc, hw, branches, users, 0, 0m, DateTime.UtcNow.ToString("yyyy-MM"));
                    await new LicenseService().OnlineHeartbeatAsync(cc, hw);
                }
                catch { }
            });
        }

        // Rol bazlı açılış: yalnız SysAdmin yönetim ekranına (MainWindow) girer. Non-admin ASLA yönetime düşmez
        // (yetki bypass'ını önler). Şubesiz/uygun rolü olmayan non-admin girişi engellenir.
        Window main;
        if (Session.IsSysAdmin) main = new MainWindow();
        else if (branch is null)
        { Session.Clear(); Dialog.Warn("Bu kullanıcı için aktif şube tanımlı değil. Yöneticinize başvurun.", "Giriş"); return; }
        else if (Session.HasRole("Mutfak"))
        {
            // Mutfak rolü MainWindow'u hiç açmadığı için ApplyLicense'taki Feature.Kitchen kapısı baypas oluyordu.
            // KDS açmadan ÖNCE lisans kontrolü (en kritik baypas savunması).
            if (!LicenseGuard.Has(MindAeonRest.Licensing.Feature.Kitchen))
            { Session.Clear(); Dialog.Warn("Mutfak (KDS) modülü lisansınızda yok.", "Lisans"); return; }
            main = new CompanyUiSettingsService().Get().IsMutfakV2 ? new KdsWindowV2() : (Window)new KdsWindow();
        }
        else if (Session.HasRole("Garson") || Session.HasRole("Kasiyer")) main = new CompanyUiSettingsService().Get().IsAdisyonV2 ? new WaiterTerminalV2Window() : (Window)new MasaBoardWindow();
        else { Session.Clear(); Dialog.Warn("Bu rol için açılacak bir ekran tanımlı değil. Yöneticinize başvurun.", "Giriş"); return; }

        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Brand_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

/// <summary>Login şirket ComboBox'ı için öğe (gerçek şirket veya "Yeni Kur" seçeneği).</summary>
public class CompanyListItem
{
    public Company? Company { get; }
    public string Display { get; }
    public bool IsNew { get; }

    public CompanyListItem(Company company)
    {
        Company = company;
        Display = $"{company.Name} ({company.Code})";
        IsNew = false;
    }

    private CompanyListItem()
    {
        Display = "＋ Yeni Şirket Kur";
        IsNew = true;
    }

    public static CompanyListItem NewItem() => new();
}
