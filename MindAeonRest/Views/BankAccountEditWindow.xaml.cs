using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class BankAccountEditWindow : Window
{
    private readonly BankAccountService _service = new();
    private readonly BankService _bankService = new();
    private readonly BankAccount _account;

    public BankAccountEditWindow(BankAccount? existing = null)
    {
        InitializeComponent();
        _account = existing ?? new BankAccount();
        BankPicker.Configure(() => _bankService.GetAll(), AddBank, existing?.BankId);
        CurrencyCombo.ItemsSource = new CurrencyService().GetAll().Select(c => new ComboItem(c.Id, c.Code)).ToList();
        CurrencyCombo.SelectedValue = existing?.CurrencyId ?? new CurrencyService().GetDefaultId();

        if (existing is not null)
        {
            TitleText.Text = $"Banka Hesabı · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private int? AddBank()
    {
        var w = new BankEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Hesap adını giriniz."); return; }
        if (BankPicker.SelectedValue is not int bankId) { ShowError("Banka seçiniz."); return; }
        if (CurrencyCombo.SelectedValue is not int curId) { ShowError("Döviz tipi seçiniz."); return; }

        _account.Code = CodeBox.Text;
        _account.Name = NameBox.Text.Trim();
        _account.BankId = bankId;
        _account.CurrencyId = curId;
        _account.IsActive = ActiveCheck.IsChecked == true;
        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_account, Session.CurrentUserId);
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
