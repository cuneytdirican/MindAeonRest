using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class MealVoucherAccountEditWindow : Window
{
    private readonly MealVoucherAccountService _service = new();
    private readonly BranchService _branchService = new();
    private readonly MealVoucherService _voucherService = new();
    private readonly MealVoucherAccount _account;

    public MealVoucherAccountEditWindow(MealVoucherAccount? existing = null)
    {
        InitializeComponent();
        _account = existing ?? new MealVoucherAccount();
        int companyId = Session.CurrentCompany?.Id ?? 0;
        BranchPicker.Configure(() => _branchService.GetByCompany(companyId), AddBranch, existing?.BranchId);
        BranchPicker.SelectionChanged += (_, _) => LoadKiosks();
        VoucherPicker.Configure(() => _voucherService.GetAll(), AddVoucher, existing?.MealVoucherId);
        CurrencyCombo.ItemsSource = new CurrencyService().GetAll().Select(c => new ComboItem(c.Id, c.Code)).ToList();
        CurrencyCombo.SelectedValue = existing?.CurrencyId ?? new CurrencyService().GetDefaultId();
        ScopeCombo.ItemsSource = new[] { new ComboItem((int)FinanceScope.Sube, "Şube Hesabı"), new ComboItem((int)FinanceScope.Kiosk, "KIOSK Hesabı") };
        ScopeCombo.SelectedValue = (int)(existing?.Scope ?? FinanceScope.Sube);

        if (existing is not null)
        {
            TitleText.Text = $"Yemek Çek Hesabı · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
            LoadKiosks();
            KioskCombo.SelectedValue = existing.KioskId;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void LoadKiosks()
    {
        if (BranchPicker.SelectedValue is int b)
            KioskCombo.ItemsSource = new KioskService().GetByBranch(b).Select(k => new ComboItem(k.Id, k.Name)).ToList();
        else KioskCombo.ItemsSource = null;
    }

    private void Scope_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (KioskPanel == null) return;
        bool kiosk = ScopeCombo.SelectedValue is int s && s == (int)FinanceScope.Kiosk;
        KioskPanel.Visibility = kiosk ? Visibility.Visible : Visibility.Collapsed;
        if (kiosk) LoadKiosks();
    }

    private int? AddBranch()
    {
        if (Session.CurrentCompany is null) return null;
        var w = new BranchEditWindow(Session.CurrentCompany.Id, Session.CurrentCompany.Name) { Owner = this };
        return w.ShowDialog() == true ? w.SavedBranch?.Id : null;
    }

    private int? AddVoucher()
    {
        var w = new MealVoucherEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Hesap adını giriniz."); return; }
        if (BranchPicker.SelectedValue is not int branchId) { ShowError("Şube seçiniz."); return; }
        if (VoucherPicker.SelectedValue is not int voucherId) { ShowError("Yemek çeki seçiniz."); return; }
        if (CurrencyCombo.SelectedValue is not int curId) { ShowError("Döviz tipi seçiniz."); return; }

        _account.Code = CodeBox.Text;
        _account.Name = NameBox.Text.Trim();
        _account.BranchId = branchId;
        _account.MealVoucherId = voucherId;
        _account.CurrencyId = curId;
        _account.Scope = ScopeCombo.SelectedValue is int sc ? (FinanceScope)sc : FinanceScope.Sube;
        if (_account.Scope == FinanceScope.Kiosk)
        {
            if (KioskCombo.SelectedValue is not int kid) { ShowError("KIOSK hesabı için KIOSK seçiniz."); return; }
            _account.KioskId = kid;
        }
        else _account.KioskId = null;
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
