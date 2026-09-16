using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class MasaRaporWindow : Window
{
    private readonly ReportService _reports = new();
    private List<TableLiveRow> _live = new();
    private List<TableHistoryRow> _hist = new();

    public MasaRaporWindow()
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;
        var branchItems = new List<ComboItem> { new(0, "Tüm Şubeler") };
        branchItems.AddRange(new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branchItems;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? 0;
        Loaded += (_, _) =>
        {
            FromBox.SelectedDate = DateTime.Today.AddDays(-7);
            ToBox.SelectedDate = DateTime.Today;
            ReloadZones();
            Load();
        };
    }

    private void ReloadZones()
    {
        int? branchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;
        var items = new List<ComboItem> { new(0, "Tüm Lokasyonlar") };
        items.AddRange(new ZoneService().GetAll().Where(z => z.IsActive && (branchId is null || z.BranchId == branchId))
            .Select(z => new ComboItem(z.Id, z.Name)));
        ZoneCombo.ItemsSource = items;
        ZoneCombo.SelectedValue = 0;
    }

    private void Load()
    {
        try
        {
            int? branchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;
            int? zoneId = ZoneCombo.SelectedValue is int z && z > 0 ? z : null;
            bool history = TabHistory.IsChecked == true;

            if (history)
            {
                var from = FromBox.SelectedDate ?? DateTime.Today;
                var to = ToBox.SelectedDate ?? DateTime.Today;
                if (to < from) (from, to) = (to, from);
                _hist = _reports.GetTableHistory(branchId, zoneId, from, to);
                HistoryGrid.ItemsSource = _hist;
                decimal ciro = _hist.Sum(x => x.Revenue);
                int adis = _hist.Sum(x => x.AdisyonCount);
                SummaryText.Text = $"Masa: {_hist.Count}  ·  Toplam adisyon (devir): {adis}  ·  Toplam ciro: {ciro:N2} ₺  ·  Ort. fiş: {(adis > 0 ? ciro / adis : 0):N2} ₺";
            }
            else
            {
                _live = _reports.GetTableLiveStatus(branchId, zoneId);
                LiveGrid.ItemsSource = _live;
                int dolu = _live.Count(x => x.State == "Dolu");
                int bos = _live.Count(x => x.State == "Boş");
                int rez = _live.Count(x => x.State == "Rezerve");
                decimal acikCiro = _live.Where(x => x.State == "Dolu").Sum(x => x.Total);
                SummaryText.Text = $"Masa: {_live.Count}  ·  🔴 Dolu: {dolu}  ·  🟢 Boş: {bos}  ·  🟠 Rezerve: {rez}  ·  Açık adisyon tutarı: {acikCiro:N2} ₺";
            }
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (LiveGrid == null) return;
        bool history = TabHistory.IsChecked == true;
        LiveGrid.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        HistoryGrid.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        RangePanel.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        Load();
    }

    private ReportTable BuildTable()
    {
        bool history = TabHistory.IsChecked == true;
        var sub = $"{(BranchCombo.SelectedItem as ComboItem)?.Text} · {(ZoneCombo.SelectedItem as ComboItem)?.Text}";
        if (history)
        {
            var t = new ReportTable
            {
                Title = "Masa Geçmiş Performans Raporu",
                Subtitle = $"{sub} · {FromBox.SelectedDate:dd.MM.yyyy} - {ToBox.SelectedDate:dd.MM.yyyy}",
                Columns = { new("Lokasyon", 120), new("Masa", 80), new("Kişilik", 60, true, "0"), new("Adisyon", 80, true, "0"),
                            new("Ciro", 100, true), new("Ort. Fiş", 90, true), new("Kişi", 60, true, "0"), new("İptal", 60, true, "0"),
                            new("Koltuk Başı", 100, true), new("Son Kullanım", 130) }
            };
            foreach (var r in _hist)
                t.Rows.Add(new object?[] { r.ZoneName, r.TableCode, r.Seats, r.AdisyonCount, r.Revenue, r.AvgTicket, r.Covers, r.CancelCount, r.RevenuePerSeat, r.LastUsedText });
            t.Footer = new object?[] { "TOPLAM", "", null, _hist.Sum(x => x.AdisyonCount), _hist.Sum(x => x.Revenue), null, _hist.Sum(x => x.Covers), _hist.Sum(x => x.CancelCount), null, "" };
            return t;
        }
        else
        {
            var t = new ReportTable
            {
                Title = "Masa Anlık Durum Raporu",
                Subtitle = $"{sub} · {DateTime.Now:dd.MM.yyyy HH:mm}",
                Columns = { new("Lokasyon", 110), new("Masa", 80), new("Kişilik", 60, true, "0"), new("Durum", 80),
                            new("Açık Adisyon", 100), new("Açılış", 90), new("Süre", 80), new("Kişi", 50, true, "0"), new("Tutar", 100, true), new("Garson", 110) }
            };
            foreach (var r in _live)
                t.Rows.Add(new object?[] { r.ZoneName, r.TableCode, r.Seats, r.State, r.OrderCode, r.OpenedText, r.DurationText, r.Covers == 0 ? null : r.Covers, r.State == "Dolu" ? r.Total : (object?)null, r.WaiterName });
            return t;
        }
    }

    private void Branch_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded) ReloadZones(); }
    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Excel_Click(object sender, RoutedEventArgs e) => Exporter.SaveExcel(BuildTable());
    private void Pdf_Click(object sender, RoutedEventArgs e) => Exporter.PrintPdf(BuildTable());
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
