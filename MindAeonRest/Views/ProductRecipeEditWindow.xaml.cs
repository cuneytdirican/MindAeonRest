using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductRecipeEditWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    public ProductRecipe? Result { get; private set; }

    public ProductRecipeEditWindow(int currentProductId, ProductRecipe? existing = null)
    {
        InitializeComponent();

        ProductCombo.ItemsSource = new ProductService().GetAll()
            .Where(p => p.Id != currentProductId)
            .Select(p => new ComboItem(p.Id, $"{p.Name} ({p.Code})")).ToList();

        var units = new List<ComboItem> { new(0, "(birim yok)") };
        units.AddRange(new MainUnitService().GetAll().Select(u => new ComboItem(u.Id, u.Display)));
        UnitCombo.ItemsSource = units;
        UnitCombo.SelectedValue = 0;

        if (existing is not null)
        {
            ProductCombo.SelectedValue = existing.ComponentProductId;
            QtyBox.Text = existing.Quantity.ToString("0.##", Tr);
            UnitCombo.SelectedValue = existing.UnitId ?? 0;
        }
        Loaded += (_, _) => ProductCombo.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (ProductCombo.SelectedValue is not int compId || compId == 0) { ShowError("Bileşen ürün seçiniz."); return; }
        if (!decimal.TryParse(QtyBox.Text.Trim().Replace('.', ','), NumberStyles.Any, Tr, out var qty) || qty <= 0)
        { ShowError("Geçerli bir miktar giriniz."); return; }

        int? unitId = UnitCombo.SelectedValue is int u && u > 0 ? u : null;
        Result = new ProductRecipe
        {
            ComponentProductId = compId,
            ComponentName = (ProductCombo.SelectedItem as ComboItem)?.Text,
            Quantity = qty,
            UnitId = unitId,
            UnitName = unitId is null ? null : (UnitCombo.SelectedItem as ComboItem)?.Text
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
