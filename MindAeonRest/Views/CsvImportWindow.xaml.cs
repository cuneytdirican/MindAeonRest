using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CsvImportWindow : Window
{
    private readonly CsvImportService _service = new();
    private string? _filePath;
    private List<CsvProductRow> _rows = new();

    public CsvImportWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("Toplu içe aktarım yalnızca yöneticiler içindir.", "Yetki"); Loaded += (_, __) => Close(); return; }

        UnitCombo.ItemsSource = WithNone(new MainUnitService().GetAll().Select(u => new ComboItem(u.Id, u.Display)));
        SalesTaxCombo.ItemsSource = WithNone(new TaxGroupService().GetAll().Select(t => new ComboItem(t.Id, t.Display)));
        CurrencyCombo.ItemsSource = WithNone(new CurrencyService().GetAll().Select(c => new ComboItem(c.Id, c.Code)));

        var scope = new List<ComboItem> { new(0, "Genel (tüm şubeler)") };
        scope.AddRange(new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0).Select(b => new ComboItem(b.Id, b.Display)));
        ScopeCombo.ItemsSource = scope;

        UnitCombo.SelectedValue = 0; SalesTaxCombo.SelectedValue = 0; CurrencyCombo.SelectedValue = 0; ScopeCombo.SelectedValue = 0;
    }

    private static List<ComboItem> WithNone(IEnumerable<ComboItem> items)
    {
        var list = new List<ComboItem> { new(0, "(yok)") };
        list.AddRange(items);
        return list;
    }

    private CsvImportOptions BuildOptions() => new()
    {
        DefaultUnitId = Val(UnitCombo),
        DefaultSalesTaxId = Val(SalesTaxCombo),
        DefaultPurchaseTaxId = Val(SalesTaxCombo),   // alış/iade için de satış varsayılanı (basitlik)
        DefaultReturnTaxId = Val(SalesTaxCombo),
        DefaultCurrencyId = Val(CurrencyCombo),
        TargetBranchId = Val(ScopeCombo),
        UpdateExisting = UpdateExistingCheck.IsChecked == true
    };

    private static int? Val(System.Windows.Controls.ComboBox c) => c.SelectedValue is int i && i > 0 ? i : null;

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "CSV Dosyası Seç", Filter = "CSV Dosyaları|*.csv|Tüm Dosyalar|*.*" };
        if (dlg.ShowDialog() == true)
        {
            _filePath = dlg.FileName;
            FileText.Text = _filePath;
            Validate();
        }
    }

    private void DownloadSample_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Örnek CSV", Filter = "CSV|*.csv", FileName = "stok_ornek.csv" };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, _service.BuildSample(), new UTF8Encoding(true));
                Dialog.Info("Örnek CSV kaydedildi.", "Bilgi");
            }
            catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e) => Validate();

    private void Validate()
    {
        if (string.IsNullOrEmpty(_filePath)) { Dialog.Info("Önce bir CSV dosyası seçin.", "Uyarı"); return; }
        try
        {
            _rows = _service.ParseAndValidate(_filePath, BuildOptions());
            PreviewGrid.ItemsSource = null;
            PreviewGrid.ItemsSource = _rows;
            int valid = _rows.Count(r => r.IsValid);
            int add = _rows.Count(r => r.IsValid && !r.ExistsInDb);
            int upd = _rows.Count(r => r.IsValid && r.ExistsInDb);
            int bad = _rows.Count - valid;
            SummaryText.Text = $"Toplam {_rows.Count} · {add} eklenecek · {upd} güncellenecek · {bad} hatalı";
            ImportButton.IsEnabled = valid > 0;
        }
        catch (Exception ex)
        {
            Dialog.Error("CSV okunamadı: " + ex.Message, "Hata");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        int valid = _rows.Count(r => r.IsValid);
        if (valid == 0) return;
        if (!Dialog.Confirm($"{valid} geçerli satır aktarılsın mı? (Hatalı satırlar atlanır)", "Onay"))
            return;

        try
        {
            ImportButton.IsEnabled = false;
            var result = _service.Import(_rows, Session.CurrentUserId);
            string msg = $"Aktarım tamamlandı.\n\nEklenen: {result.Created}\nGüncellenen: {result.Updated}\nBaşarısız: {result.Failed}";
            if (result.Errors.Count > 0)
                msg += "\n\nHatalar:\n" + string.Join("\n", result.Errors.Take(15));
            if (result.Failed > 0) Dialog.Warn(msg, "Sonuç"); else Dialog.Info(msg, "Sonuç");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Dialog.Error("Aktarım hatası: " + ex.Message, "Hata");
            ImportButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
