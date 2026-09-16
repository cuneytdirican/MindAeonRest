using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class UserPickerWindow : Window
{
    private readonly UserService _userService = new();

    public string? SelectedUsername { get; private set; }

    public UserPickerWindow(int companyId, int? branchId, string subtitle)
    {
        InitializeComponent();
        SubtitleText.Text = subtitle;

        var users = _userService.GetForLogin(companyId, branchId)
            .Select(u => new UserPickItem
            {
                Username = u.Username,
                FullName = u.FullName,
                RoleName = u.RoleName,
                Initial = string.IsNullOrWhiteSpace(u.FullName) ? "?" : u.FullName.Trim()[..1].ToUpperInvariant()
            })
            .ToList();

        UserList.ItemsSource = users;
        EmptyText.Visibility = users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void User_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string username })
        {
            SelectedUsername = username;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public class UserPickItem
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Initial { get; set; } = "?";
}
