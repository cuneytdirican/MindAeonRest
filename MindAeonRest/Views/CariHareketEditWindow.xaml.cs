using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CariHareketEditWindow : Window
{
    private readonly CariHareketService _service = new();

    public CariHareketEditWindow(int? presetCustomerId = null)
    {
        InitializeComponent();
        CustomerPicker.Configure(() => new CustomerService().GetAll(), AddCustomer, presetCustomerId);

        TypeCombo.ItemsSource = new List<ComboItem>
        {
            new((int)CariDocType.Acilis, CariDocType.Acilis.ToTr()),
            new((int)CariDocType.Manuel, CariDocType.Manuel.ToTr()),
            new((int)CariDocType.Tahsilat, CariDocType.Tahsilat.ToTr()),
            new((int)CariDocType.Odeme, CariDocType.Odeme.ToTr()),
        };
        TypeCombo.SelectedIndex = 1;

        var projects = new List<ComboItem> { new(0, "(Yok)") };
        projects.AddRange(new ProjectService().GetAll().Where(p => p.IsActive).Select(p => new ComboItem(p.Id, $"{p.Code} · {p.Name}")));
        ProjectCombo.ItemsSource = projects;
        ProjectCombo.SelectedIndex = 0;

        DateBox.SelectedDate = DateTime.Today;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CustomerPicker.SelectedValue is not int cid || cid == 0) { ShowError("Cari seçiniz."); return; }
        if (!decimal.TryParse(DebtBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var debt) || debt < 0) { ShowError("Borç geçersiz."); return; }
        if (!decimal.TryParse(CreditBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var credit) || credit < 0) { ShowError("Alacak geçersiz."); return; }
        if (debt == 0 && credit == 0) { ShowError("Borç veya Alacak giriniz."); return; }

        var h = new CariHareket
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            CustomerId = cid,
            TxnDate = (DateBox.SelectedDate ?? DateTime.Today).Date.Add(DateTime.Now.TimeOfDay),
            DocType = (CariDocType)(TypeCombo.SelectedValue ?? (int)CariDocType.Manuel),
            Debt = debt,
            Credit = credit,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            ProjectId = ProjectCombo.SelectedValue is int pid && pid > 0 ? pid : null,
            Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim()
        };
        try { SaveButton.IsEnabled = false; _service.AddManual(h, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

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
