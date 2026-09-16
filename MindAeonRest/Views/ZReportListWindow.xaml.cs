using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ZReportListWindow : Window
{
    private readonly ReportService _reports = new();
    private List<ZReportRow> _rows = new();

    public ZReportListWindow()
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;

        var branchItems = new List<ComboItem> { new(0, "Tüm Şubeler") };
        branchItems.AddRange(new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branchItems;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? 0;

        // KIOSK NO filtresi: Tümü + tanımlı KIOSK numaraları
        var nos = new KioskService().GetAll().Select(k => k.No).Distinct().OrderBy(n => n).ToList();
        if (!nos.Contains(Session.CurrentKioskNo)) nos.Add(Session.CurrentKioskNo);
        var kioskItems = new List<ComboItem> { new(0, "Tüm KIOSK'lar") };
        kioskItems.AddRange(nos.OrderBy(n => n).Select(n => new ComboItem(n, $"#{n}")));
        KioskCombo.ItemsSource = kioskItems;
        KioskCombo.SelectedValue = 0;

        Loaded += (_, _) =>
        {
            FromBox.SelectedDate = DateTime.Today.AddDays(-30);
            ToBox.SelectedDate = DateTime.Today;
            Load();
        };
    }

    private void Load()
    {
        try
        {
            var from = FromBox.SelectedDate ?? DateTime.Today;
            var to = ToBox.SelectedDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);
            int? branchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;
            int? kioskNo = KioskCombo.SelectedValue is int k && k > 0 ? k : null;

            _rows = _reports.GetZReportList(branchId, kioskNo, from, to);
            ZGrid.ItemsSource = _rows;
            decimal toplam = _rows.Sum(z => z.SalesTotal);
            int fis = _rows.Sum(z => z.FisCount);
            SummaryText.Text = $"Z sayısı: {_rows.Count}  ·  Toplam satış: {toplam:N2} ₺  ·  Toplam fiş: {fis}   —   💡 Bir Z satırına ÇİFT TIKLAYIN: o günün tüm fiş/iptal hareketleri açılır.";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private ReportTable BuildTable()
    {
        var sub = $"{(BranchCombo.SelectedItem as ComboItem)?.Text} · {(KioskCombo.SelectedItem as ComboItem)?.Text} · {FromBox.SelectedDate:dd.MM.yyyy} - {ToBox.SelectedDate:dd.MM.yyyy}";
        var t = new ReportTable
        {
            Title = "Z (Gün Sonu) Raporları",
            Subtitle = sub,
            Columns =
            {
                new("Tarih", 100), new("Şube", 150), new("KIOSK", 70),
                new("Fiş Adedi", 90, true, "0"), new("Satış Toplamı", 120, true),
                new("Alınma", 130), new("Alan", 140)
            }
        };
        foreach (var z in _rows)
            t.Rows.Add(new object?[] { z.DateText, z.BranchName, z.KioskText, z.FisCount, z.SalesTotal, z.TakenAtText, z.TakenByName });
        t.Footer = new object?[] { "TOPLAM", "", "", _rows.Sum(x => x.FisCount), _rows.Sum(x => x.SalesTotal), "", "" };
        return t;
    }

    private void ZGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ZGrid.SelectedItem is not ZReportRow z) return;
        // Z detayı = o şubenin o günkü tüm fiş/adisyon + iptal hareketleri
        new FicheReportWindow(z.BranchId, z.ReportDate, z.ReportDate) { Owner = this }.ShowDialog();
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Excel_Click(object sender, RoutedEventArgs e) => Exporter.SaveExcel(BuildTable());
    private void Csv_Click(object sender, RoutedEventArgs e) => Exporter.SaveCsv(BuildTable());
    private void Pdf_Click(object sender, RoutedEventArgs e) => Exporter.PrintPdf(BuildTable());
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
