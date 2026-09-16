using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class BankEditWindow : Window
{
    private readonly BankService _service = new();
    private readonly Bank _bank;

    public int? SavedId => _bank.Id == 0 ? null : _bank.Id;

    public BankEditWindow(Bank? existing = null)
    {
        InitializeComponent();
        _bank = existing ?? new Bank();
        if (existing is not null)
        {
            TitleText.Text = $"Banka · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Banka adını giriniz."); return; }

        _bank.Code = CodeBox.Text;
        _bank.Name = NameBox.Text.Trim();
        _bank.IsActive = ActiveCheck.IsChecked == true;
        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_bank, Session.CurrentUserId);
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
