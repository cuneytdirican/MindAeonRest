using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CekSenetEditWindow : Window
{
    private readonly CheckNoteService _service = new();

    public CekSenetEditWindow()
    {
        InitializeComponent();
        KindCombo.ItemsSource = new List<ComboItem> { new((int)CheckNoteKind.Cek, "Çek"), new((int)CheckNoteKind.Senet, "Senet") };
        KindCombo.SelectedIndex = 0;
        DirCombo.ItemsSource = new List<ComboItem> { new((int)CheckDirection.Alinan, "Alınan (müşteriden)"), new((int)CheckDirection.Verilen, "Verilen (tedarikçiye)") };
        DirCombo.SelectedIndex = 0;
        CustomerPicker.Configure(() => new CustomerService().GetAll(), AddCustomer, null);
        IssueBox.SelectedDate = DateTime.Today;
        DueBox.SelectedDate = DateTime.Today.AddDays(30);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CustomerPicker.SelectedValue is not int cid || cid == 0) { ShowError("Cari seçiniz."); return; }
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0) { ShowError("Tutar geçersiz."); return; }
        if (IssueBox.SelectedDate is null || DueBox.SelectedDate is null) { ShowError("Düzenleme ve vade tarihi seçiniz."); return; }

        var n = new CheckNote
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            Kind = (CheckNoteKind)(KindCombo.SelectedValue ?? 0),
            Direction = (CheckDirection)(DirCombo.SelectedValue ?? 0),
            CustomerId = cid,
            Serial = Empty(SerialBox.Text),
            BankName = Empty(BankBox.Text),
            Amount = amt,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            IssueDate = IssueBox.SelectedDate.Value,
            DueDate = DueBox.SelectedDate.Value,
            Status = CheckStatus.Portfoyde,
            Note = Empty(NoteBox.Text)
        };
        try { SaveButton.IsEnabled = false; _service.Add(n, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private static string? Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
