using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductEditWindow : Window
{
    private readonly ProductService _service = new();
    private readonly MainUnitService _unitService = new();
    private readonly TaxGroupService _taxService = new();
    private readonly ProductCategoryService _categoryService = new();
    private readonly CodeSeriesService _codeSeries = new();
    private readonly Product _product;
    public int? SavedId => _product.Id == 0 ? null : _product.Id;
    private List<CategoryCheckItem> _categories = new();
    private readonly List<ProductImageVM> _images = new();
    private readonly ObservableCollection<ProductDescription> _descriptions = new();
    private readonly List<ProductUnit> _units = new();
    private readonly List<ProductBarcode> _barcodes = new();
    private readonly List<ProductPrice> _prices = new();
    private readonly List<ProductRecipe> _recipe = new();
    private readonly List<ProductOption> _options = new();
    private readonly List<ProductBranchConsume> _consume = new();
    private List<Warehouse> _allWarehouses = new();

    public ProductEditWindow(Product? existing = null)
    {
        InitializeComponent();
        _product = existing ?? new Product();
        DescList.ItemsSource = _descriptions;

        // Birim ve vergi grubu seçicileri ("+" ile ekleme)
        UnitPicker.Configure(() => _unitService.GetAll(), AddUnit, _product.MainUnitId);
        SalesTaxPicker.Configure(() => _taxService.GetAll(), AddTaxGroup, _product.SalesTaxGroupId);
        PurchaseTaxPicker.Configure(() => _taxService.GetAll(), AddTaxGroup, _product.PurchaseTaxGroupId);
        ReturnTaxPicker.Configure(() => _taxService.GetAll(), AddTaxGroup, _product.ReturnTaxGroupId);

        TrackingCombo.ItemsSource = Enum.GetValues<TrackingType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();
        TrackingCombo.SelectedValue = (int)_product.TrackingType;

        LoadCategories();

        if (existing is not null)
        {
            TitleText.Text = $"Ürün · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            DescBox.Text = existing.Description;
            ActiveCheck.IsChecked = existing.IsActive;
            SpicyCheck.IsChecked = existing.IsSpicy;

            foreach (var img in _service.GetImages(existing.Id))
                _images.Add(new ProductImageVM(img.Image) { IsMain = img.IsMain });
            RefreshImages();

            foreach (var d in _service.GetDescriptions(existing.Id))
                _descriptions.Add(d);

            _units.AddRange(_service.GetUnits(existing.Id));
            _barcodes.AddRange(_service.GetBarcodes(existing.Id));
            _prices.AddRange(_service.GetPrices(existing.Id));
            _recipe.AddRange(_service.GetRecipe(existing.Id));
            _options.AddRange(_service.GetOptions(existing.Id));
        }

        // Tüketim deposu (tüm şubeler; yeni üründe de listelenir)
        _consume.AddRange(_service.GetConsumeSettings(_product.Id));
        _allWarehouses = new WarehouseService().GetAll();
        ConsumeBranchCombo.ItemsSource = new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0)
            .Select(b => new ComboItem(b.Id, b.Display)).ToList();

        if (existing is null)
        {
            var nextCode = _codeSeries.PeekNext(CodeSeriesType.Product);
            if (nextCode is not null) CodeBox.Text = nextCode;
        }

        RefreshUnits();
        RefreshBarcodes();
        RefreshPrices();
        RefreshRecipe();
        RefreshOptions();
        RefreshConsume();
        Loaded += (_, _) => CodeBox.Focus();
    }

    // ===== Şube tüketim depoları =====
    private void RefreshConsume() { ConsumeGrid.ItemsSource = null; ConsumeGrid.ItemsSource = _consume; }

    private void ConsumeBranch_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ConsumeBranchCombo.SelectedValue is int bId)
            ConsumeWarehouseCombo.ItemsSource = _allWarehouses.Where(w => w.BranchId == bId)
                .Select(w => new ComboItem(w.Id, w.Display)).ToList();
        else
            ConsumeWarehouseCombo.ItemsSource = null;
    }

    private void AssignConsume_Click(object sender, RoutedEventArgs e)
    {
        if (ConsumeBranchCombo.SelectedValue is not int bId) { ShowError("Şube seçin."); return; }
        if (ConsumeWarehouseCombo.SelectedValue is not int wId) { ShowError("Bu şubeye ait bir depo seçin."); return; }
        HideError();

        var row = _consume.FirstOrDefault(c => c.BranchId == bId);
        if (row is null) { row = new ProductBranchConsume { BranchId = bId, BranchName = (ConsumeBranchCombo.SelectedItem as ComboItem)?.Text ?? "" }; _consume.Add(row); }
        row.ConsumeWarehouseId = wId;
        row.WarehouseName = (ConsumeWarehouseCombo.SelectedItem as ComboItem)?.Text;
        RefreshConsume();
    }

    // ===== Reçete =====
    private void RefreshRecipe() { RecipeGrid.ItemsSource = null; RecipeGrid.ItemsSource = _recipe; }

    private void AddRecipe_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProductRecipeEditWindow(_product.Id) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _recipe.Add(w.Result); RefreshRecipe(); }
    }

    private void EditRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is not ProductRecipe sel) { ShowError("Önce bir bileşen seçin."); return; }
        int idx = _recipe.IndexOf(sel);
        var w = new ProductRecipeEditWindow(_product.Id, sel) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _recipe[idx] = w.Result; RefreshRecipe(); }
    }

    private void RemoveRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeGrid.SelectedItem is ProductRecipe sel) { _recipe.Remove(sel); RefreshRecipe(); }
    }

    // ===== Fiyatlar =====
    private void RefreshPrices() { PricesGrid.ItemsSource = null; PricesGrid.ItemsSource = _prices; }

    /// <summary>Fiyat/barkod birime bağlıdır. Hiç birim yoksa, seçili ANA BİRİM'den otomatik birim üretir
    /// (kullanıcının ayrıca "Birim Ekle" yapmasına gerek kalmaz — eski "Önce birim ekleyin" takılması giderildi).</summary>
    private bool EnsureUnitForPricing()
    {
        if (_units.Count > 0) return true;
        if (UnitPicker.SelectedValue is not int mainUnitId)
        { ShowError("Önce yukarıdan bir ANA BİRİM seçin (ör. Adet). Fiyat ve barkod birime bağlıdır."); return false; }
        var mu = _unitService.GetAll().FirstOrDefault(u => u.Id == mainUnitId);
        _units.Add(new ProductUnit { UnitId = mainUnitId, UnitName = mu?.Name, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 });
        RefreshUnits();
        return true;
    }

    private void AddPrice_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnitForPricing()) return;
        var w = new ProductPriceEditWindow(_units, NameBox.Text) { Owner = this };
        if (w.ShowDialog() == true && w.Results.Count > 0) { _prices.AddRange(w.Results); RefreshPrices(); }
    }

    private void EditPrice_Click(object sender, RoutedEventArgs e)
    {
        if (PricesGrid.SelectedItem is not ProductPrice sel) { ShowError("Önce bir fiyat seçin."); return; }
        var w = new ProductPriceEditWindow(_units, NameBox.Text, sel) { Owner = this };
        if (w.ShowDialog() == true && w.Results.Count > 0) { _prices.Remove(sel); _prices.AddRange(w.Results); RefreshPrices(); }
    }

    private void RemovePrice_Click(object sender, RoutedEventArgs e)
    {
        if (PricesGrid.SelectedItem is ProductPrice sel) { _prices.Remove(sel); RefreshPrices(); }
    }

    // ===== Seçenekler (Varyant) =====
    private void RefreshOptions() { OptionsGrid.ItemsSource = null; OptionsGrid.ItemsSource = _options; }

    private void AddOption_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProductOptionEditWindow { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _options.Add(w.Result); RefreshOptions(); }
    }

    private void EditOption_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsGrid.SelectedItem is not ProductOption sel) { ShowError("Önce bir seçenek seçin."); return; }
        int idx = _options.IndexOf(sel);
        var w = new ProductOptionEditWindow(sel) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null) { _options[idx] = w.Result; RefreshOptions(); }
    }

    private void RemoveOption_Click(object sender, RoutedEventArgs e)
    {
        if (OptionsGrid.SelectedItem is ProductOption sel) { _options.Remove(sel); RefreshOptions(); }
    }

    // ===== Birimler =====
    private void RefreshUnits() { UnitsGrid.ItemsSource = null; UnitsGrid.ItemsSource = _units; }

    private void AddUnit_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProductUnitEditWindow { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null)
        {
            _units.Add(w.Result);
            EnforceSingleMain(w.Result);
            RefreshUnits();
        }
    }

    private void EditUnit_Click(object sender, RoutedEventArgs e)
    {
        if (UnitsGrid.SelectedItem is not ProductUnit sel) { ShowError("Önce bir birim seçin."); return; }
        int idx = _units.IndexOf(sel);
        var w = new ProductUnitEditWindow(sel) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null)
        {
            _units[idx] = w.Result;
            EnforceSingleMain(w.Result);
            RefreshUnits();
        }
    }

    private void RemoveUnit_Click(object sender, RoutedEventArgs e)
    {
        if (UnitsGrid.SelectedItem is not ProductUnit sel) { ShowError("Önce bir birim seçin."); return; }
        _units.Remove(sel);
        _barcodes.RemoveAll(b => b.UnitId == sel.UnitId);   // birime bağlı barkodları da kaldır
        RefreshUnits();
        RefreshBarcodes();
    }

    private void EnforceSingleMain(ProductUnit main)
    {
        if (!main.IsMainUnit) return;
        foreach (var u in _units) u.IsMainUnit = ReferenceEquals(u, main);
    }

    // ===== Barkodlar =====
    private void RefreshBarcodes() { BarcodesGrid.ItemsSource = null; BarcodesGrid.ItemsSource = _barcodes; }

    private void AddBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureUnitForPricing()) return;
        var w = new ProductBarcodeEditWindow(_units) { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null)
        {
            _barcodes.Add(w.Result);
            RefreshBarcodes();
        }
    }

    private void RemoveBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (BarcodesGrid.SelectedItem is ProductBarcode sel) { _barcodes.Remove(sel); RefreshBarcodes(); }
    }

    // ===== Resimler =====
    private void RefreshImages()
    {
        ImageList.ItemsSource = null;
        ImageList.ItemsSource = _images;
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Ürün Resmi Seç", Filter = "Resim Dosyaları|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm Dosyalar|*.*", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var file in dlg.FileNames)
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                _images.Add(new ProductImageVM(bytes) { IsMain = _images.Count == 0 });
            }
            catch (Exception ex) { ShowError("Resim yüklenemedi: " + ex.Message); }
        }
        RefreshImages();
    }

    private void SetMainImage_Click(object sender, RoutedEventArgs e)
    {
        if (ImageList.SelectedItem is not ProductImageVM sel) { ShowError("Önce bir resim seçin."); return; }
        foreach (var i in _images) i.IsMain = false;
        sel.IsMain = true;
        RefreshImages();
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (ImageList.SelectedItem is not ProductImageVM sel) { ShowError("Önce bir resim seçin."); return; }
        bool wasMain = sel.IsMain;
        _images.Remove(sel);
        if (wasMain && _images.Count > 0) _images[0].IsMain = true;
        RefreshImages();
    }

    // ===== Açıklamalar =====
    private void AddDesc_Click(object sender, RoutedEventArgs e) => AddDescription();
    private void DescInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddDescription(); }

    private void AddDescription()
    {
        string text = DescInput.Text.Trim();
        if (text.Length == 0) return;
        _descriptions.Add(new ProductDescription { Description = text });
        DescInput.Clear();
        DescInput.Focus();
    }

    private void RemoveDesc_Click(object sender, RoutedEventArgs e)
    {
        if (DescList.SelectedItem is ProductDescription d) _descriptions.Remove(d);
    }

    private void LoadCategories()
    {
        _categories = _categoryService.GetAll()
            .Select(c => new CategoryCheckItem { Id = c.Id, Name = c.Display, IsSelected = _product.CategoryIds.Contains(c.Id) })
            .ToList();
        CategoryList.ItemsSource = _categories;
    }

    private int? AddUnit()
    {
        var w = new QuickAddWindow("Yeni Birim", "Kod", "Birim Adı",
            (code, name) => _unitService.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        return w.ShowDialog() == true ? w.CreatedId : null;
    }

    private int? AddTaxGroup()
    {
        var w = new TaxGroupEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedGroupId : null;
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProductCategoryEditWindow { Owner = this };
        if (w.ShowDialog() == true)
        {
            // seçili kategorileri koru, listeyi tazele
            var selected = _categories.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet();
            _product.CategoryIds.Clear();
            _product.CategoryIds.AddRange(selected);
            LoadCategories();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir ürün kodu giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Ürün adını giriniz."); return; }

        _product.Code = CodeBox.Text;
        _product.Name = NameBox.Text.Trim();
        _product.Description = DescBox.Text;
        _product.MainUnitId = UnitPicker.SelectedValue;
        _product.SalesTaxGroupId = SalesTaxPicker.SelectedValue;
        _product.PurchaseTaxGroupId = PurchaseTaxPicker.SelectedValue;
        _product.ReturnTaxGroupId = ReturnTaxPicker.SelectedValue;
        _product.TrackingType = TrackingCombo.SelectedValue is int tt ? (TrackingType)tt : TrackingType.Yok;
        _product.IsActive = ActiveCheck.IsChecked == true;
        _product.IsSpicy = SpicyCheck.IsChecked == true;

        var catIds = _categories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        var imgs = _images.Select(vm => new ProductImage { Image = vm.Bytes, IsMain = vm.IsMain }).ToList();

        bool wasNew = _product.Id == 0;
        string? seriesCode = wasNew ? _codeSeries.PeekNext(CodeSeriesType.Product) : null;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_product, catIds, imgs, _descriptions.ToList(), _units, _barcodes, _prices, _recipe, _consume, Session.CurrentUserId, _options);
            // Kod seriden geldiyse seriyi ilerlet
            if (wasNew && seriesCode is not null && CodeHelper.Normalize(seriesCode) == _product.Code)
                _codeSeries.Advance(CodeSeriesType.Product);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public class CategoryCheckItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public record ComboItem(int Value, string Text)
{
    // ComboBox'larda DisplayMemberPath ayarlı olmasa bile metin görünsün (record varsayılan ToString'i yerine).
    public override string ToString() => Text;
}

/// <summary>Ürün resmi düzenleme öğesi (bellekte; önizleme dahil).</summary>
public class ProductImageVM
{
    public byte[] Bytes { get; }
    public bool IsMain { get; set; }
    public ImageSource? Preview { get; }

    public ProductImageVM(byte[] bytes)
    {
        Bytes = bytes;
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 180;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            Preview = img;
        }
        catch { Preview = null; }
    }
}
