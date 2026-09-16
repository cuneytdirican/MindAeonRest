using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ExtractWindow : Window
{
    private enum AccType { Cash = 0, Pos = 1, Bank = 2, Voucher = 3 }

    private readonly ExtractService _extract = new();
    private ExtractResult _result = new();
    private bool _ready;

    public ExtractWindow(int initialType = 0)
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;

        var branchItems = new List<ComboItem>();
        branchItems.AddRange(new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branchItems;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? branchItems.FirstOrDefault()?.Value;

        TypeCombo.ItemsSource = new[]
        {
            new ComboItem((int)AccType.Cash, "💵 Nakit Kasa"),
            new ComboItem((int)AccType.Pos, "💳 POS / Kredi Kartı"),
            new ComboItem((int)AccType.Bank, "🏦 Banka Hesabı"),
            new ComboItem((int)AccType.Voucher, "🎫 Yemek Çeki"),
        };

        Loaded += (_, _) =>
        {
            FromBox.SelectedDate = DateTime.Today.AddDays(-30);
            ToBox.SelectedDate = DateTime.Today;
            _ready = true;
            TypeCombo.SelectedValue = initialType;   // hesap listesini doldurur + ilk ekstre
        };
    }

    private AccType CurrentType => TypeCombo.SelectedValue is int t ? (AccType)t : AccType.Cash;

    private void LoadAccounts()
    {
        if (!_ready) return;
        int? branchId = BranchCombo.SelectedValue as int?;
        // Banka hesapları şube bağımsız → şube filtresini gizle
        BranchPanel.Visibility = CurrentType == AccType.Bank ? Visibility.Collapsed : Visibility.Visible;

        List<ComboItem> items = CurrentType switch
        {
            AccType.Cash => new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == branchId).Select(x => new ComboItem(x.Id, $"{x.Name} ({x.ScopeText})")).ToList(),
            AccType.Pos => new PosService().GetAll().Where(x => x.IsActive && x.BranchId == branchId).Select(x => new ComboItem(x.Id, $"{x.Name} ({x.ScopeText})")).ToList(),
            AccType.Bank => new BankAccountService().GetAll().Where(x => x.IsActive).Select(x => new ComboItem(x.Id, x.Display)).ToList(),
            AccType.Voucher => new MealVoucherAccountService().GetAll().Where(x => x.IsActive && x.BranchId == branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList(),
            _ => new List<ComboItem>()
        };
        AccountCombo.ItemsSource = items;
        if (items.Count > 0) AccountCombo.SelectedIndex = 0;
        else { AccountCombo.SelectedIndex = -1; ClearGrid(); }
    }

    private void Load()
    {
        if (!_ready) return;
        if (AccountCombo.SelectedValue is not int accId) { ClearGrid(); return; }
        try
        {
            var from = FromBox.SelectedDate ?? DateTime.Today;
            var to = ToBox.SelectedDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);

            _result = CurrentType switch
            {
                AccType.Cash => _extract.GetCashExtract(accId, from, to),
                AccType.Pos => _extract.GetPosExtract(accId, from, to),
                AccType.Bank => _extract.GetBankExtract(accId, from, to),
                AccType.Voucher => _extract.GetVoucherExtract(accId, from, to),
                _ => new ExtractResult()
            };
            ExtractGrid.ItemsSource = _result.Rows;
            OpeningText.Text = $"{_result.Opening:N2}";
            InText.Text = $"{_result.TotalIn:N2}";
            OutText.Text = $"{_result.TotalOut:N2}";
            ClosingText.Text = $"{_result.Closing:N2}";
            TitleText.Text = $"📒 {(TypeCombo.SelectedItem as ComboItem)?.Text} Ekstresi · {(AccountCombo.SelectedItem as ComboItem)?.Text}";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void ClearGrid()
    {
        _result = new ExtractResult();
        ExtractGrid.ItemsSource = null;
        OpeningText.Text = InText.Text = OutText.Text = ClosingText.Text = "0,00";
    }

    private ReportTable BuildTable()
    {
        var accName = (AccountCombo.SelectedItem as ComboItem)?.Text ?? "";
        var t = new ReportTable
        {
            Title = $"{(TypeCombo.SelectedItem as ComboItem)?.Text} Ekstresi — {accName}",
            Subtitle = $"{FromBox.SelectedDate:dd.MM.yyyy} - {ToBox.SelectedDate:dd.MM.yyyy}  ·  Açılış: {_result.Opening:N2}  ·  Kapanış: {_result.Closing:N2}",
            Columns =
            {
                new("Tarih", 120), new("İşlem", 130), new("Açıklama", 200),
                new("Giriş", 100, true), new("Çıkış", 100, true), new("Bakiye", 110, true)
            }
        };
        t.Rows.Add(new object?[] { "", "AÇILIŞ BAKİYE", "", null, null, _result.Opening });
        foreach (var r in _result.Rows)
            t.Rows.Add(new object?[] { r.DateText, r.TypeText, r.Note, r.In == 0 ? null : r.In, r.Out == 0 ? null : r.Out, r.Running });
        t.Footer = new object?[] { "TOPLAM / KAPANIŞ", "", "", _result.TotalIn, _result.TotalOut, _result.Closing };
        return t;
    }

    private void Type_Changed(object sender, SelectionChangedEventArgs e) => LoadAccounts();
    private void Branch_Changed(object sender, SelectionChangedEventArgs e) => LoadAccounts();
    private void Account_Changed(object sender, SelectionChangedEventArgs e) => Load();
    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Excel_Click(object sender, RoutedEventArgs e) { if (AccountCombo.SelectedValue != null) Exporter.SaveExcel(BuildTable()); }
    private void Csv_Click(object sender, RoutedEventArgs e) { if (AccountCombo.SelectedValue != null) Exporter.SaveCsv(BuildTable()); }
    private void Pdf_Click(object sender, RoutedEventArgs e) { if (AccountCombo.SelectedValue != null) Exporter.PrintPdf(BuildTable()); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
