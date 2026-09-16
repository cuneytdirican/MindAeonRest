using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Yetkili onay diyaloğu: bir kullanıcının kimliğini doğrular ve yetkili olup olmadığını kontrol eder.
/// Örn. iade için 'İade' rolü veya SysAdmin gerektiğinde, başka yetkili bir kullanıcı girip onaylayabilir.</summary>
public partial class SupervisorAuthWindow : Window
{
    private readonly Func<User, bool> _isAuthorized;
    public User? AuthorizedUser { get; private set; }

    public SupervisorAuthWindow(string message, Func<User, bool> isAuthorized)
    {
        InitializeComponent();
        _isAuthorized = isAuthorized;
        MsgText.Text = message;
        Loaded += (_, _) => UserBox.Focus();
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e)
        => VirtualKeyboardWindow.ShowFor(string.IsNullOrEmpty(UserBox.Text) ? UserBox : PwdBox);

    private void Pwd_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) TryAuth(); }
    private void Ok_Click(object sender, RoutedEventArgs e) => TryAuth();

    private void TryAuth()
    {
        var uname = UserBox.Text.Trim();
        var pwd = PwdBox.Password;
        if (uname.Length == 0 || pwd.Length == 0) { Fail("Kullanıcı adı ve şifre girin."); return; }

        try
        {
            var user = new UserService().GetForLogin(Session.CurrentCompany?.Id ?? 0, Session.CurrentBranch?.Id)
                .FirstOrDefault(u => string.Equals(u.Username, uname, StringComparison.OrdinalIgnoreCase));
            if (user is null || string.IsNullOrEmpty(user.PasswordHash) || !PasswordHasher.Verify(pwd, user.PasswordHash))
            { Fail("Kullanıcı adı veya şifre hatalı."); return; }
            if (!_isAuthorized(user)) { Fail($"'{user.FullName}' bu işlem için yetkili değil."); return; }

            AuthorizedUser = user;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private void Fail(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; PwdBox.Clear(); PwdBox.Focus(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
