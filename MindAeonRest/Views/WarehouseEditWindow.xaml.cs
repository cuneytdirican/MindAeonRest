using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class WarehouseEditWindow : Window
{
    private readonly WarehouseService _service = new();
    private readonly BranchService _branchService = new();
    private readonly Warehouse _warehouse;

    public WarehouseEditWindow(Warehouse? existing = null)
    {
        InitializeComponent();
        _warehouse = existing ?? new Warehouse();

        int companyId = Session.CurrentCompany?.Id ?? 0;
        BranchPicker.Configure(() => _branchService.GetByCompany(companyId), AddBranch, existing?.BranchId);

        if (existing is not null)
        {
            TitleText.Text = $"Depo · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private int? AddBranch()
    {
        if (Session.CurrentCompany is null) return null;
        var w = new BranchEditWindow(Session.CurrentCompany.Id, Session.CurrentCompany.Name) { Owner = this };
        return w.ShowDialog() == true ? w.SavedBranch?.Id : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Depo adını giriniz."); return; }
        if (BranchPicker.SelectedValue is not int branchId) { ShowError("Şube seçiniz."); return; }

        _warehouse.Code = CodeBox.Text;
        _warehouse.Name = NameBox.Text.Trim();
        _warehouse.BranchId = branchId;
        _warehouse.IsActive = ActiveCheck.IsChecked == true;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_warehouse, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
