using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class QuickProductWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly ProductService _products = new();
    private readonly CodeSeriesService _codeSeries = new();
    private readonly int _cur;
    private readonly ObservableCollection<QuickProdRow> _rows = new();
    private byte[]? _imageBytes;   // seçilen ürün resmi (opsiyonel)

    public QuickProductWindow()
    {
        InitializeComponent();
        _cur = new CurrencyService().GetDefaultId() ?? 0;
        int? uid = Session.CurrentUserId;

        CatPicker.Configure(() => new ProductCategoryService().GetAll(), () =>
            QuickAdd("Yeni Kategori", (c, n) => new ProductCategoryService().Save(new ProductCategory { Code = c, Name = n, IsActive = true }, uid).Id), null);
        UnitPicker.Configure(() => new MainUnitService().GetAll(), () =>
            QuickAdd("Yeni Birim", (c, n) => new MainUnitService().Create(c, n, uid)), null);
        TaxPicker.Configure(() => new TaxGroupService().GetAll(), () =>
            QuickAdd("Yeni KDV Grubu", (c, n) => new TaxGroupService().Save(new TaxGroup { Code = c, Name = n, IsActive = true }, Array.Empty<TaxGroupRate>(), uid).Id), null);

        var view = new ListCollectionView(_rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuickProdRow.CategoryNames)));
        Grid.ItemsSource = view;

        Loaded += (_, _) =>
        {
            var cats = new List<ComboItem> { new(0, "(Tümü)") };
            cats.AddRange(new ProductCategoryService().GetAll().Select(c => new ComboItem(c.Id, c.Name)));
            CatFilter.ItemsSource = cats;
            CatFilter.SelectedValue = 0;
            NextCode(); LoadList(); NameBox.Focus();
        };
    }

    private int? QuickAdd(string title, Func<string, string, int> create)
    {
        var w = new QuickAddWindow(title, "Kod", "Ad", create, field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private void NextCode() => CodeBox.Text = _codeSeries.PeekNext(CodeSeriesType.Product) ?? "";

    private void LoadList()
    {
        var sale = _products.GetGeneralPrices(PriceType.Satis);
        var buy = _products.GetGeneralPrices(PriceType.Alis);
        _rows.Clear();
        foreach (var p in _products.GetAll(includeDeleted: false))
        {
            sale.TryGetValue(p.Id, out var s);
            buy.TryGetValue(p.Id, out var b);
            _rows.Add(new QuickProdRow
            {
                ProductId = p.Id, Code = p.Code, Name = p.Name,
                CategoryNames = string.IsNullOrWhiteSpace(p.CategoryNames) ? "(Kategorisiz)" : p.CategoryNames,
                UnitId = (s?.UnitId is int u && u > 0) ? u : (p.MainUnitId ?? 0),
                UnitName = p.MainUnitName, CurrencyId = s?.CurrencyId ?? _cur,
                TaxIncluded = s?.IsTaxIncluded ?? true,
                SaleInput = s is null ? "" : s.Price.ToString("0.##", Tr),
                BuyInput = b is null ? "" : b.Price.ToString("0.##", Tr),
                Image = _products.GetMainImage(p.Id)
            });
        }
        InfoText.Text = $"{_rows.Count} ürün. Yeni eklemek için üstteki formu doldurup 'Ekle ve Kaydet'e basın.";
    }

    private static string Norm(string? s) =>
        (s ?? "").ToLowerInvariant().Replace("i̇", "i").Replace('İ', 'i').Replace('ı', 'i').Replace('î', 'i');

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (Grid.ItemsSource is not ListCollectionView view) return;
        var q = Norm(SearchBox.Text);
        string? catName = (CatFilter.SelectedItem as ComboItem)?.Value > 0 ? (CatFilter.SelectedItem as ComboItem)?.Text : null;
        view.Filter = (string.IsNullOrWhiteSpace(q) && catName == null) ? null
            : o => o is QuickProdRow r
                && (string.IsNullOrWhiteSpace(q) || Norm(r.Code).Contains(q) || Norm(r.Name).Contains(q))
                && (catName == null || (r.CategoryNames ?? "").Contains(catName, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryDec(string text, out decimal v) =>
        decimal.TryParse((text ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out v);

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Ürün Resmi Seç", Filter = "Resim Dosyaları|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm Dosyalar|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _imageBytes = File.ReadAllBytes(dlg.FileName);
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(_imageBytes)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze(); }
            ImagePreview.Source = bmp;
            ImageClearButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { Dialog.Error("Resim yüklenemedi: " + ex.Message, "Hızlı Ürün"); }
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        _imageBytes = null; ImagePreview.Source = null; ImageClearButton.Visibility = Visibility.Collapsed;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        string code = CodeBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Dialog.Warn("Ürün adı girin.", "Hızlı Ürün"); return; }
        if (string.IsNullOrWhiteSpace(code)) { Dialog.Warn("Kod girin.", "Hızlı Ürün"); return; }

        int unitId = UnitPicker.SelectedValue ?? 0;
        bool hasSale = TryDec(SaleBox.Text, out var sale) && !string.IsNullOrWhiteSpace(SaleBox.Text);
        bool hasBuy = TryDec(BuyBox.Text, out var buy) && !string.IsNullOrWhiteSpace(BuyBox.Text);
        if ((hasSale || hasBuy) && unitId <= 0) { Dialog.Warn("Fiyat girmek için BİRİM seçin (ör. Adet).", "Hızlı Ürün"); return; }

        var p = new Product
        {
            Code = code, Name = NameBox.Text.Trim(), MainUnitId = unitId > 0 ? unitId : null,
            SalesTaxGroupId = TaxPicker.SelectedValue, TrackingType = TrackingType.Yok, IsActive = true
        };
        var units = unitId > 0
            ? new List<ProductUnit> { new() { UnitId = unitId, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 } }
            : new List<ProductUnit>();
        var prices = new List<ProductPrice>();
        if (hasSale) prices.Add(new ProductPrice { UnitId = unitId, PriceType = PriceType.Satis, Price = sale, CurrencyId = _cur, IsTaxIncluded = TaxInclCheck.IsChecked == true, IsActive = true });
        if (hasBuy) prices.Add(new ProductPrice { UnitId = unitId, PriceType = PriceType.Alis, Price = buy, CurrencyId = _cur, IsTaxIncluded = TaxInclCheck.IsChecked == true, IsActive = true });
        var barcodes = !string.IsNullOrWhiteSpace(BarcodeBox.Text) && unitId > 0
            ? new List<ProductBarcode> { new() { Barcode = BarcodeBox.Text.Trim(), UnitId = unitId } }
            : new List<ProductBarcode>();
        var cats = CatPicker.SelectedValue is int cid && cid > 0 ? new[] { cid } : Array.Empty<int>();

        try
        {
            var images = _imageBytes is { Length: > 0 }
                ? new List<ProductImage> { new() { Image = _imageBytes, IsMain = true } }
                : new List<ProductImage>();
            _products.Save(p, cats, images, Array.Empty<ProductDescription>(),
                units, barcodes, prices, Array.Empty<ProductRecipe>(), Array.Empty<ProductBranchConsume>(), Session.CurrentUserId);
            if (!string.IsNullOrWhiteSpace(_codeSeries.PeekNext(CodeSeriesType.Product)) && code == _codeSeries.PeekNext(CodeSeriesType.Product))
                _codeSeries.Advance(CodeSeriesType.Product);
            // formu temizle, listeyi tazele → yeni stok kodu + Ürün Adı'na odak (sıradaki kayda hızlı geçiş)
            NameBox.Clear(); SaleBox.Clear(); BuyBox.Clear(); BarcodeBox.Clear();
            _imageBytes = null; ImagePreview.Source = null; ImageClearButton.Visibility = Visibility.Collapsed;
            NextCode(); LoadList();
            Dispatcher.BeginInvoke(new Action(() => { NameBox.Focus(); Keyboard.Focus(NameBox); }), System.Windows.Threading.DispatcherPriority.Input);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hızlı Ürün"); }
    }

    // Satır içi fiyat düzenleme → o satırın satış/alış fiyatını kaydet
    private void Grid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.Row.Item is not QuickProdRow r) return;
        Dispatcher.BeginInvoke(new Action(() => SaveRow(r)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SaveRow(QuickProdRow r)
    {
        if (r.UnitId <= 0) { Dialog.Warn($"{r.Code} {r.Name}: birim yok, fiyat kaydedilemez (ürün kartından ana birim ekleyin).", "Hızlı Ürün"); return; }
        try
        {
            if (TryDec(r.SaleInput, out var s) && !string.IsNullOrWhiteSpace(r.SaleInput))
                _products.UpsertGeneralPrice(r.ProductId, r.UnitId, PriceType.Satis, s, r.TaxIncluded, r.CurrencyId, Session.CurrentUserId);
            if (TryDec(r.BuyInput, out var b) && !string.IsNullOrWhiteSpace(r.BuyInput))
                _products.UpsertGeneralPrice(r.ProductId, r.UnitId, PriceType.Alis, b, r.TaxIncluded, r.CurrencyId, Session.CurrentUserId);
            InfoText.Text = $"{r.Code} {r.Name} fiyatı güncellendi.";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hızlı Ürün"); }
    }

    // Enter: yerleşim sırasına göre bir sonraki alana; Barkod'da Enter → Kaydet. Açık combo listesinde Enter seçim yapar.
    private void Form_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (AnyOpenCombo(EntryForm)) return;
        if (Keyboard.FocusedElement == BarcodeBox) { Add_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (Keyboard.FocusedElement is UIElement ui) { ui.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
    }

    private static bool AnyOpenCombo(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is ComboBox cb && cb.IsDropDownOpen) return true;
            if (AnyOpenCombo(c)) return true;
        }
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public class QuickProdRow : INotifyPropertyChanged
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CategoryNames { get; set; }
    public int UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? Barcode { get; set; }
    public int CurrencyId { get; set; }
    public byte[]? Image { get; set; }

    private string _sale = "";
    public string SaleInput { get => _sale; set { _sale = value; OnChanged(nameof(SaleInput)); } }
    private string _buy = "";
    public string BuyInput { get => _buy; set { _buy = value; OnChanged(nameof(BuyInput)); } }
    private bool _tax = true;
    public bool TaxIncluded { get => _tax; set { _tax = value; OnChanged(nameof(TaxIncluded)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
