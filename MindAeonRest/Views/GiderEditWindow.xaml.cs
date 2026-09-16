using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class GiderEditWindow : Window
{
    private readonly ExpenseService _service = new();
    private List<ExpenseType> _types = new();

    public GiderEditWindow()
    {
        InitializeComponent();
        _types = new ExpenseTypeService().GetAll().Where(t => t.IsActive).ToList();
        TypeCombo.ItemsSource = _types.Select(t => new ComboItem(t.Id, t.Name)).ToList();

        PersonnelPicker.Configure(() => new PersonnelService().GetAll().Where(p => p.IsActive), AddPersonnel, null);

        var prj = new List<ComboItem> { new(0, "(Yok)") };
        prj.AddRange(new ProjectService().GetAll().Where(p => p.IsActive).Select(p => new ComboItem(p.Id, $"{p.Code} · {p.Name}")));
        ProjectCombo.ItemsSource = prj; ProjectCombo.SelectedIndex = 0;

        PayKindCombo.ItemsSource = new List<ComboItem> { new((int)ExpensePayKind.Kasa, "Kasa"), new((int)ExpensePayKind.Banka, "Banka") };
        PayKindCombo.SelectedIndex = 0;

        DateBox.SelectedDate = DateTime.Today;
        if (TypeCombo.Items.Count > 0) TypeCombo.SelectedIndex = 0;
    }

    private void Type_Changed(object sender, SelectionChangedEventArgs e)
    {
        var t = _types.FirstOrDefault(x => x.Id == (TypeCombo.SelectedValue as int? ?? 0));
        PersonnelPanel.Visibility = t is { IsSalary: true } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PayKind_Changed(object sender, SelectionChangedEventArgs e) => ReloadInstruments();

    private void ReloadInstruments()
    {
        int br = Session.CurrentBranch?.Id ?? 0;
        var kind = (ExpensePayKind)(PayKindCombo.SelectedValue as int? ?? 0);
        InstrumentCombo.ItemsSource = kind == ExpensePayKind.Kasa
            ? new CashRegisterService().GetAll().Where(k => k.IsActive && k.BranchId == br).Select(k => new ComboItem(k.Id, k.Name)).ToList()
            : new BankAccountService().GetAll().Where(a => a.IsActive).Select(a => new ComboItem(a.Id, a.Name)).ToList();
        if (InstrumentCombo.Items.Count > 0) InstrumentCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (TypeCombo.SelectedValue is not int et || et == 0) { ShowError("Gider tipi seçiniz."); return; }
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0) { ShowError("Geçerli tutar girin."); return; }
        if (InstrumentCombo.SelectedValue is not int inst || inst == 0) { ShowError("Ödeme aracı seçiniz."); return; }

        var x = new Expense
        {
            BranchId = Session.CurrentBranch?.Id ?? 0,
            ExpenseTypeId = et,
            PersonnelId = PersonnelPanel.Visibility == Visibility.Visible && PersonnelPicker.SelectedValue is int pe && pe > 0 ? pe : null,
            ProjectId = ProjectCombo.SelectedValue is int pr && pr > 0 ? pr : null,
            Amount = amt,
            CurrencyId = new CurrencyService().GetDefaultId() ?? 0,
            PayKind = (ExpensePayKind)(PayKindCombo.SelectedValue ?? 0),
            InstrumentId = inst,
            ExpenseDate = (DateBox.SelectedDate ?? DateTime.Today).Date.Add(DateTime.Now.TimeOfDay),
            Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim()
        };
        try { SaveButton.IsEnabled = false; _service.Save(x, Session.CurrentUserId); Dialog.Success($"Gider kaydedildi: {x.Code} (araçtan çıkış yapıldı)"); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private int? AddPersonnel()
    {
        var w = new PersonnelEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
