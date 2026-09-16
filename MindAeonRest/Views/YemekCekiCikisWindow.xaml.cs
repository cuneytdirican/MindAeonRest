using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Yemek çeki portföyünü Yemek Çeki Şirketi'ne (cari) Satış Faturası olarak çıkar → cari borç (şirket bize borçlanır).</summary>
public partial class YemekCekiCikisWindow : Window
{
    private readonly int _branchId;
    private readonly int _currencyId;

    public YemekCekiCikisWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        _currencyId = new CurrencyService().GetDefaultId() ?? 0;

        AccountCombo.ItemsSource = new MealVoucherAccountService().GetAll()
            .Where(a => a.IsActive && a.BranchId == _branchId)
            .Select(a => new ComboItem(a.Id, a.Name)).ToList();
        if (AccountCombo.Items.Count > 0) AccountCombo.SelectedIndex = 0;

        CustomerPicker.Configure(() => new CustomerService().GetAll(), AddCustomer, null);
        DateBox.SelectedDate = DateTime.Today;
        Loaded += (_, _) => CustomerPicker.Focus();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Account_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AccountCombo.SelectedValue is not int accId) { BalanceHint.Text = ""; return; }
        try
        {
            // Bugüne kadar toplanan (tahsil edilmemiş) yemek çeki bakiyesi → varsayılan tutar
            var ex = new ExtractService().GetVoucherExtract(accId, DateTime.Today.AddYears(-5), DateTime.Today);
            BalanceHint.Text = $"Bu hesapta toplanan yemek çeki: {ex.Closing:N2} ₺ (varsayılan tutar)";
            if (string.IsNullOrWhiteSpace(AmountBox.Text) || AmountBox.Text == "0")
                AmountBox.Text = ex.Closing.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
        }
        catch { BalanceHint.Text = ""; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CustomerPicker.SelectedValue is not int companyId) { ShowError("Yemek Çeki Şirketi (cari) seçiniz."); return; }
        if (!decimal.TryParse((AmountBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var amount) || amount <= 0)
        { ShowError("Geçerli bir tutar giriniz (0'dan büyük)."); return; }
        string? note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        string? docNo = string.IsNullOrWhiteSpace(DocNoBox.Text) ? null : DocNoBox.Text.Trim();

        try
        {
            SaveButton.IsEnabled = false;
            new CariHareketService().AddManual(new CariHareket
            {
                BranchId = _branchId,
                CustomerId = companyId,
                TxnDate = DateBox.SelectedDate ?? DateTime.Now,
                DocType = CariDocType.SatisFaturasi,
                DocRefType = "YemekÇekiÇıkış",
                DocNo = docNo,
                Description = "Yemek çeki satış faturası" + (note is null ? "" : $" · {note}"),
                CurrencyId = _currencyId,
                Debt = amount,    // şirket bize borçlanır
                Credit = 0
            }, Session.CurrentUserId);
            Dialog.Success($"Yemek çeki satış faturası oluşturuldu: {amount:N2} ₺\nYemek Çeki Şirketi cari hesabına BORÇ yazıldı. Tahsilatı banka girişiyle yapabilirsiniz.");
            DialogResult = true; Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
