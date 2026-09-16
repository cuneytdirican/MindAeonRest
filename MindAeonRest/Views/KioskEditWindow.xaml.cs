using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class KioskEditWindow : Window
{
    private const string NoPrinter = "(yazıcı yok)";
    private readonly KioskService _service = new();
    private readonly BranchService _branchService = new();
    private readonly Kiosk _kiosk;

    public KioskEditWindow(Kiosk? existing = null)
    {
        InitializeComponent();
        _kiosk = existing ?? new Kiosk();
        int companyId = Session.CurrentCompany?.Id ?? 0;
        BranchPicker.Configure(() => _branchService.GetByCompany(companyId), null, existing?.BranchId);
        NoBox.Text = (existing?.No ?? 1).ToString();
        var printers = new List<string> { NoPrinter };
        printers.AddRange(PrinterHelper.GetInstalled());
        if (!string.IsNullOrWhiteSpace(existing?.ReceiptPrinterName) && !printers.Contains(existing!.ReceiptPrinterName!))
            printers.Add(existing.ReceiptPrinterName!);
        PrinterCombo.ItemsSource = printers;
        PrinterCombo.SelectedItem = string.IsNullOrWhiteSpace(existing?.ReceiptPrinterName) ? NoPrinter : existing!.ReceiptPrinterName;

        if (existing is not null)
        {
            TitleText.Text = $"KIOSK · #{existing.No} {existing.Code}";
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
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("KIOSK adını giriniz."); return; }
        if (BranchPicker.SelectedValue is not int branchId) { ShowError("Şube seçiniz."); return; }
        if (!int.TryParse(NoBox.Text.Trim(), out int no) || no <= 0) { ShowError("Geçerli bir KIOSK numarası giriniz (1, 2, ...)."); return; }

        string? printer = PrinterCombo.SelectedItem as string;
        if (printer == NoPrinter) printer = null;
        _kiosk.ReceiptPrinterName = printer;
        _kiosk.No = no;
        _kiosk.Code = CodeBox.Text;
        _kiosk.Name = NameBox.Text.Trim();
        _kiosk.BranchId = branchId;
        _kiosk.IsActive = ActiveCheck.IsChecked == true;
        try { SaveButton.IsEnabled = false; _service.Save(_kiosk, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
