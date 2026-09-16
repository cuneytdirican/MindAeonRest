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

public partial class FicheReportWindow : Window
{
    private readonly ReportService _reports = new();
    private List<FicheReportRow> _fiches = new();
    private List<CancelReportRow> _cancels = new();

    public FicheReportWindow() : this(null, null, null) { }

    /// <summary>Ön-filtreli açılış (ör. Z raporu detayı: o şube + o gün).</summary>
    public FicheReportWindow(int? branchId, DateTime? from, DateTime? to)
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;
        var branches = new BranchService().GetByCompany(companyId);
        var items = branches.Select(b => new ComboItem(b.Id, b.Name)).ToList();
        BranchCombo.ItemsSource = items;
        BranchCombo.SelectedValue = branchId ?? Session.CurrentBranch?.Id ?? items.FirstOrDefault()?.Value;
        Loaded += (_, _) =>
        {
            FromBox.SelectedDate = from ?? DateTime.Today.AddDays(-7);
            ToBox.SelectedDate = to ?? DateTime.Today;
            Load();
        };
    }

    private void Load()
    {
        try
        {
            if (BranchCombo.SelectedValue is not int branchId) { Dialog.Warn("Şube seçin."); return; }
            var from = FromBox.SelectedDate ?? DateTime.Today;
            var to = ToBox.SelectedDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);
            string q = (SearchBox.Text ?? "").Trim();

            _fiches = _reports.GetFicheReport(branchId, from, to);
            _cancels = _reports.GetCancellationReport(branchId, from, to);
            if (q.Length > 0)
            {
                _fiches = _fiches.Where(f => Match(q, f.FicheCode, f.OrderCode, f.WaiterName, f.CashierName, f.CustomerName, f.TableCodes)).ToList();
                _cancels = _cancels.Where(c => Match(q, c.OrderCode, c.ProductName, c.Reason, c.CancelledByName, c.TableCodes)).ToList();
            }
            FichesGrid.ItemsSource = _fiches;
            CancelsGrid.ItemsSource = _cancels;
            UpdateSummary();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private static bool Match(string q, params string?[] fields) =>
        fields.Any(f => !string.IsNullOrEmpty(f) && f.Contains(q, StringComparison.OrdinalIgnoreCase));

    private void UpdateSummary()
    {
        bool cancelsTab = TabCancels.IsChecked == true;
        if (cancelsTab)
        {
            decimal lost = _cancels.Sum(c => c.Amount);
            int fire = _cancels.Count(c => c.Disposition == "Fire");
            SummaryText.Text = $"İptal satırı: {_cancels.Count}  ·  Toplam iptal tutarı: {lost:N2} ₺  ·  Fire: {fire}  ·  Stoğa iade: {_cancels.Count(c => c.Disposition == "Stoğa İade")}";
        }
        else
        {
            decimal ciro = _fiches.Sum(f => f.Total);
            decimal ind = _fiches.Sum(f => f.Discount);
            decimal ikram = _fiches.Sum(f => f.Treat);
            SummaryText.Text = $"Fiş: {_fiches.Count}  ·  Ciro: {ciro:N2} ₺  ·  İndirim: {ind:N2} ₺  ·  İkram: {ikram:N2} ₺";
        }
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (FichesGrid == null) return;
        bool cancelsTab = TabCancels.IsChecked == true;
        FichesGrid.Visibility = cancelsTab ? Visibility.Collapsed : Visibility.Visible;
        CancelsGrid.Visibility = cancelsTab ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    private ReportTable BuildTable()
    {
        bool cancelsTab = TabCancels.IsChecked == true;
        var sub = $"{(BranchCombo.SelectedItem as ComboItem)?.Text} · {FromBox.SelectedDate:dd.MM.yyyy} - {ToBox.SelectedDate:dd.MM.yyyy}";
        if (cancelsTab)
        {
            var t = new ReportTable
            {
                Title = "İptal Raporu",
                Subtitle = sub,
                Columns =
                {
                    new("Tarih", 120), new("Adisyon", 90), new("Masa", 80), new("Ürün", 180),
                    new("Miktar", 70, true, "0.##"), new("Tutar", 90, true), new("Sebep", 160),
                    new("İptal Eden", 120), new("Akıbet", 100)
                }
            };
            foreach (var c in _cancels)
                t.Rows.Add(new object?[] { c.DateText, c.OrderCode, c.TableCodes, c.ProductName, c.Qty, c.Amount, c.Reason, c.CancelledByName, c.Disposition });
            t.Footer = new object?[] { "TOPLAM", "", "", "", null, _cancels.Sum(x => x.Amount), "", "", "" };
            return t;
        }
        else
        {
            var t = new ReportTable
            {
                Title = "Fiş / Adisyon Raporu",
                Subtitle = sub,
                Columns =
                {
                    new("Fiş No", 90), new("Adisyon", 90), new("Tarih", 120), new("Şube", 100),
                    new("Lokasyon", 100), new("Masa", 80), new("Garson", 110), new("Kasiyer", 110),
                    new("Müşteri", 110), new("İndirim", 80, true), new("İkram", 80, true),
                    new("Ödeme", 150), new("Tutar", 90, true), new("Durum", 70)
                }
            };
            foreach (var f in _fiches)
                t.Rows.Add(new object?[] { f.FicheCode, f.OrderCode, f.DateText, f.BranchName, f.ZoneName, f.TableCodes, f.WaiterName, f.CashierName, f.CustomerName, f.Discount, f.Treat, f.Payments, f.Total, f.StatusText });
            t.Footer = new object?[] { "TOPLAM", "", "", "", "", "", "", "", "", _fiches.Sum(x => x.Discount), _fiches.Sum(x => x.Treat), "", _fiches.Sum(x => x.Total), "" };
            return t;
        }
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Excel_Click(object sender, RoutedEventArgs e) => Exporter.SaveExcel(BuildTable());
    private void Csv_Click(object sender, RoutedEventArgs e) => Exporter.SaveCsv(BuildTable());
    private void Pdf_Click(object sender, RoutedEventArgs e) => Exporter.PrintPdf(BuildTable());
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
