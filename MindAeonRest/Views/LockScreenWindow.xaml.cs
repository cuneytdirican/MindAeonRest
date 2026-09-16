using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Tam ekran "afili" kilit ekranı. Kilitleyen kullanıcı VEYA bir yönetici (SysAdmin) şifresiyle açılır.</summary>
public partial class LockScreenWindow : Window
{
    private readonly User _lockUser;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _unlocked;

    public LockScreenWindow(User lockUser)
    {
        InitializeComponent();
        _lockUser = lockUser;
        UserNameText.Text = string.IsNullOrWhiteSpace(lockUser.FullName) ? lockUser.Username : lockUser.FullName;
        HintText.Text = $"Yalnızca {UserNameText.Text} veya bir yönetici açabilir.";

        // Şirket logosu (varsa) — yoksa marka rozeti (M) görünür
        try
        {
            if (Session.CurrentCompany is { } co)
            {
                var (color, _) = new CompanyService().GetLogos(co.Id);
                if (color is { Length: > 0 })
                {
                    var bmp = new BitmapImage();
                    using var ms = new System.IO.MemoryStream(color);
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    LogoImage.Source = bmp; LogoImage.Visibility = Visibility.Visible; LogoLetter.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch { /* logo yoksa rozet kalır */ }

        Tick(null, EventArgs.Empty);
        _clock.Tick += Tick;
        _clock.Start();
        Loaded += (_, _) => PwdBox.Focus();
    }

    private void Tick(object? s, EventArgs e)
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dd MMMM yyyy dddd", new System.Globalization.CultureInfo("tr-TR"));
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) => VirtualKeyboardWindow.ShowFor(PwdBox);

    private void Pwd_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Unlock(); }
    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();

    private void Unlock()
    {
        if (string.IsNullOrEmpty(PwdBox.Password)) { Fail("Şifre girin."); return; }
        if (CheckPassword(PwdBox.Password)) Success();
        else Fail("Şifre hatalı. Yalnızca kilitleyen kullanıcı veya yönetici açabilir.");
    }

    /// <summary>Şifre kilitleyen kullanıcıya VEYA bir yöneticiye (SysAdmin) ait mi?</summary>
    private bool CheckPassword(string pwd)
    {
        if (string.IsNullOrEmpty(pwd)) return false;
        // 1) Kilitleyen kullanıcının şifresi
        if (!string.IsNullOrEmpty(_lockUser.PasswordHash) && PasswordHasher.Verify(pwd, _lockUser.PasswordHash)) return true;
        // 2) Yönetici (SysAdmin) override — şirketteki aktif adminlerden biri
        try
        {
            var users = new UserService().GetForLogin(Session.CurrentCompany?.Id ?? 0, Session.CurrentBranch?.Id);
            var roles = new UserRoleService();
            foreach (var u in users.Where(u => u.Id != _lockUser.Id && !string.IsNullOrEmpty(u.PasswordHash)))
            {
                bool isAdmin = string.Equals(u.RoleName, "SysAdmin", StringComparison.OrdinalIgnoreCase)
                    || roles.GetRoleNames(u.Id).Any(r => r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase));
                if (isAdmin && PasswordHasher.Verify(pwd, u.PasswordHash)) return true;
            }
        }
        catch { /* yönetici listesi alınamazsa yalnız kilitleyen açabilir */ }
        return false;
    }

    private void Success()
    {
        _unlocked = true;
        _clock.Stop();
        DialogResult = true;
        Close();
    }

    private void Fail(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
        PwdBox.Clear();
        PwdBox.Focus();
    }

    // Kilitliyken kapatma engellenir (✕ yok, Alt+F4 / Esc çalışmaz)
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { if (!_unlocked) e.Cancel = true; }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key is Key.Escape or Key.System) e.Handled = true; }
}
