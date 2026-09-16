using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductUnitEditWindow : Window
{
    private readonly MainUnitService _unitService = new();
    private static readonly CultureInfo Tr = new("tr-TR");

    public ProductUnit? Result { get; private set; }

    public ProductUnitEditWindow(ProductUnit? existing = null)
    {
        InitializeComponent();
        Height = System.Math.Min(Height, SystemParameters.WorkArea.Height - 40);   // ekrana sığsın
        UnitPicker.Configure(() => _unitService.GetAll(), AddUnit, existing?.UnitId);

        if (existing is not null)
        {
            MainCheck.IsChecked = existing.IsMainUnit;
            Conv1Box.Text = existing.ConversionFactor1.ToString("0.####", Tr);
            Conv2Box.Text = existing.ConversionFactor2.ToString("0.####", Tr);
            WidthBox.Text = Num(existing.Width);
            LengthBox.Text = Num(existing.Length);
            HeightBox.Text = Num(existing.Height);
            WeightBox.Text = Num(existing.Weight);
        }
        Loaded += (_, _) => UnitPicker.Focus();
    }

    private static string Num(decimal? d) => d?.ToString("0.###", Tr) ?? string.Empty;

    private int? AddUnit()
    {
        var w = new QuickAddWindow("Yeni Birim", "Kod", "Birim Adı",
            (code, name) => _unitService.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (UnitPicker.SelectedValue is not int unitId) { ShowError("Birim seçiniz."); return; }
        if (!TryDec(Conv1Box.Text, out var c1, 1) || c1 <= 0) { ShowError("Çevrim katsayısı 1 geçersiz."); return; }
        if (!TryDec(Conv2Box.Text, out var c2, 1) || c2 <= 0) { ShowError("Çevrim katsayısı 2 geçersiz."); return; }

        var unitName = _unitService.GetAll().FirstOrDefault(u => u.Id == unitId)?.Display;

        Result = new ProductUnit
        {
            UnitId = unitId,
            UnitName = unitName,
            IsMainUnit = MainCheck.IsChecked == true,
            ConversionFactor1 = c1,
            ConversionFactor2 = c2,
            Width = NullDec(WidthBox.Text),
            Length = NullDec(LengthBox.Text),
            Height = NullDec(HeightBox.Text),
            Weight = NullDec(WeightBox.Text)
        };
        DialogResult = true;
        Close();
    }

    private static bool TryDec(string text, out decimal value, decimal fallback)
    {
        text = (text ?? string.Empty).Trim().Replace('.', ',');
        if (string.IsNullOrEmpty(text)) { value = fallback; return true; }
        return decimal.TryParse(text, NumberStyles.Any, Tr, out value);
    }

    private static decimal? NullDec(string text)
        => TryDec(text, out var v, 0) && !string.IsNullOrWhiteSpace(text) ? v : null;

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; Dialog.Warn(m, "Ürün Birimi"); }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
