using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CourierEditWindow : Window
{
    private readonly CourierService _service = new();
    private readonly Courier _courier;

    public CourierEditWindow(Courier? existing = null)
    {
        InitializeComponent();
        _courier = existing ?? new Courier();
        if (existing is not null)
        {
            TitleText.Text = $"Kurye · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Kurye adını giriniz."); return; }
        _courier.Code = CodeBox.Text; _courier.Name = NameBox.Text.Trim(); _courier.Phone = PhoneBox.Text; _courier.IsActive = ActiveCheck.IsChecked == true;
        try { SaveButton.IsEnabled = false; _service.Save(_courier, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
