using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class UserEditWindow : Window
{
    private readonly UserService _userService = new();
    private readonly RoleService _roleService = new();
    private readonly UserAccessService _accessService = new();

    private readonly Company _company;
    private readonly User? _existing;
    private readonly List<BranchAccessItem> _branchItems;
    private readonly List<RoleCheckItem> _roleItems;

    private static int Priority(string name) => name switch
    { "SysAdmin" => 0, "Kasiyer" => 1, "Garson" => 2, "Mutfak" => 3, _ => 9 };

    public UserEditWindow(Company company, User? existing = null)
    {
        InitializeComponent();
        _company = company;
        _existing = existing;

        var selectedIds = existing is null ? new List<int>() : new UserRoleService().GetRoleIds(existing.Id);
        _roleItems = _roleService.GetAll()
            .Select(r => new RoleCheckItem { Id = r.Id, Name = r.Name, IsSelected = selectedIds.Contains(r.Id) })
            .ToList();
        RoleList.ItemsSource = _roleItems;

        // Şirketin şubeleri (erişim seçimi için)
        _branchItems = new BranchService().GetByCompany(company.Id)
            .Select(b => new BranchAccessItem { Id = b.Id, Name = b.Display }).ToList();
        BranchList.ItemsSource = _branchItems;

        if (existing is null)
        {
            TitleText.Text = "Yeni Kullanıcı";
            SubtitleText.Text = $"{company.Name} şirketine kullanıcı ekleyin.";
            AllBranchesCheck.IsChecked = true;
            Loaded += (_, _) => UsernameBox.Focus();
        }
        else
        {
            TitleText.Text = $"Kullanıcı Düzenle · {existing.Username}";
            SubtitleText.Text = company.Name;
            UsernameBox.Text = existing.Username;
            UsernameBox.IsEnabled = false;
            FullNameBox.Text = existing.FullName;
            ActiveCheck.IsChecked = existing.IsActive;
            PasswordLabel.Text = "YENİ PAROLA / PIN (boş bırakılırsa değişmez)";

            var (all, ids) = _accessService.GetAccess(existing.Id, company.Id);
            AllBranchesCheck.IsChecked = all;
            foreach (var item in _branchItems)
                item.IsSelected = ids.Contains(item.Id);

            Loaded += (_, _) => FullNameBox.Focus();
        }

        UpdateBranchListState();
    }

    private void AllBranches_Toggled(object sender, RoutedEventArgs e) => UpdateBranchListState();

    private void UpdateBranchListState()
    {
        bool all = AllBranchesCheck.IsChecked == true;
        BranchListBorder.IsEnabled = !all;
        BranchListBorder.Opacity = all ? 0.45 : 1.0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        string username = UsernameBox.Text.Trim();
        string fullName = FullNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username)) { ShowError("Kullanıcı adını giriniz."); return; }
        if (string.IsNullOrWhiteSpace(fullName)) { ShowError("Ad soyad giriniz."); return; }

        var selected = _roleItems.Where(r => r.IsSelected).ToList();
        var roleNames = selected.Select(r => r.Name).ToList();
        var roleError = UserRoleService.ValidateRoleSet(roleNames);
        if (roleError != null) { ShowError(roleError); return; }
        // Birincil rol = en yetkili (gösterim/login için); SysAdmin>Kasiyer>Garson>Mutfak
        var roleIds = selected.OrderBy(r => Priority(r.Name)).Select(r => r.Id).ToList();

        bool allBranches = AllBranchesCheck.IsChecked == true;
        var selectedBranchIds = _branchItems.Where(b => b.IsSelected).Select(b => b.Id).ToList();
        if (!allBranches && selectedBranchIds.Count == 0)
        {
            ShowError("En az bir şube seçin veya 'Tüm şubeler'i işaretleyin.");
            return;
        }

        bool isActive = ActiveCheck.IsChecked == true;
        string password = PasswordBox.Password;

        try
        {
            SaveButton.IsEnabled = false;
            int userId;

            if (_existing is null)
            {
                if (string.IsNullOrEmpty(password)) { ShowError("Yeni kullanıcı için parola/PIN giriniz."); SaveButton.IsEnabled = true; return; }
                var created = _userService.Create(username, fullName, roleIds, isActive, password, Session.CurrentUserId);
                userId = created.Id;
            }
            else
            {
                userId = _existing.Id;
                _userService.Update(userId, fullName, roleIds, isActive, Session.CurrentUserId);
                if (!string.IsNullOrEmpty(password))
                    _userService.SetPassword(userId, password, Session.CurrentUserId);
            }

            _accessService.SetBranchAccess(userId, _company.Id, allBranches, selectedBranchIds, Session.CurrentUserId);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

/// <summary>Kullanıcı şube erişim seçimi için liste öğesi.</summary>
public class BranchAccessItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>Kullanıcı rol seçimi (çoklu) için liste öğesi.</summary>
public class RoleCheckItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}
