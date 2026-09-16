using System.Windows;
using System.Windows.Input;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

public partial class ProductBarcodeEditWindow : Window
{
    public ProductBarcode? Result { get; private set; }

    public ProductBarcodeEditWindow(IEnumerable<ProductUnit> productUnits)
    {
        InitializeComponent();
        var units = productUnits
            .Select(u => new ProductBarcode { UnitId = u.UnitId, UnitName = u.UnitName }) // hafif öğe (UnitId+UnitName)
            .GroupBy(u => u.UnitId).Select(g => g.First()).ToList();
        UnitCombo.ItemsSource = units;
        if (units.Count == 1) UnitCombo.SelectedIndex = 0;
        Loaded += (_, _) => BarcodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (UnitCombo.SelectedItem is not ProductBarcode sel) { ShowError("Birim seçiniz."); return; }
        if (string.IsNullOrWhiteSpace(BarcodeBox.Text)) { ShowError("Barkod giriniz."); return; }

        Result = new ProductBarcode { UnitId = sel.UnitId, UnitName = sel.UnitName, Barcode = BarcodeBox.Text.Trim() };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
