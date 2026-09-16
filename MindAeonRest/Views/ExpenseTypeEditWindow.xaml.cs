using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ExpenseTypeEditWindow : Window
{
    private readonly ExpenseTypeService _service = new();
    private readonly ExpenseType _item;

    public ExpenseTypeEditWindow(ExpenseType? existing = null)
    {
        InitializeComponent();
        _item = existing ?? new ExpenseType();
        if (existing is not null)
        {
            TitleText.Text = $"Gider Tipi · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            SalaryCheck.IsChecked = existing.IsSalary;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Gider tipi adını giriniz."); return; }
        _item.Code = CodeBox.Text; _item.Name = NameBox.Text.Trim();
        _item.IsSalary = SalaryCheck.IsChecked == true; _item.IsActive = ActiveCheck.IsChecked == true;
        try { SaveButton.IsEnabled = false; _service.Save(_item, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
