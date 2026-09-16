using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductPriceMatrixWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly ProductService _products = new();
    private readonly ObservableCollection<MatrixRow> _rows = new();

    public ProductPriceMatrixWindow()
    {
        InitializeComponent();
        ProductPicker.Configure(() => _products.GetAll(includeDeleted: false), null, null);

        var view = new ListCollectionView(_rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MatrixRow.ProductText)));
        Grid.ItemsSource = view;

        Loaded += (_, _) =>
        {
            var branches = new List<ComboItem> { new(0, "(Tümü)"), new(-1, "Tüm Şubeler (genel)") };
            branches.AddRange(new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0).Select(b => new ComboItem(b.Id, b.Name)));
            BranchFilter.ItemsSource = branches; BranchFilter.SelectedValue = 0;
            var lists = new List<ComboItem> { new(0, "(Tümü)"), new(-1, "Genel (listesiz)") };
            lists.AddRange(new PriceListService().GetAll().Select(p => new ComboItem(p.Id, p.Name)));
            ListFilter.ItemsSource = lists; ListFilter.SelectedValue = 0;
            LoadList();
        };
    }

    private void LoadList()
    {
        _rows.Clear();
        foreach (var p in _products.GetScopedPrices()) _rows.Add(new MatrixRow(p));
        InfoText.Text = $"{_rows.Count} fiyat satırı.";
        ApplyFilter();
    }

    private static string Norm(string? s) =>
        (s ?? "").ToLowerInvariant().Replace("i̇", "i").Replace('İ', 'i').Replace('ı', 'i').Replace('î', 'i');

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (Grid.ItemsSource is not ListCollectionView view) return;
        var q = Norm(SearchBox.Text);
        int branch = (BranchFilter.SelectedItem as ComboItem)?.Value ?? 0;
        int list = (ListFilter.SelectedItem as ComboItem)?.Value ?? 0;
        view.Filter = o =>
        {
            if (o is not MatrixRow r) return false;
            if (q.Length > 0 && !Norm(r.Src.ProductCode).Contains(q) && !Norm(r.Src.ProductName).Contains(q)) return false;
            if (branch == -1 && r.Src.BranchId != null) return false;
            if (branch > 0 && r.Src.BranchId != branch) return false;
            if (list == -1 && r.Src.PriceListId != null) return false;
            if (list > 0 && r.Src.PriceListId != list) return false;
            return true;
        };
    }

    private void Grid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.Row.Item is not MatrixRow r) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!decimal.TryParse((r.PriceInput ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out var price) || price < 0)
            { Dialog.Warn($"{r.Src.ProductText}: geçersiz fiyat '{r.PriceInput}'.", "Fiyat Matrisi"); return; }
            try { _products.UpdatePriceById(r.Src.Id, price, r.TaxIncluded, Session.CurrentUserId); InfoText.Text = $"{r.Src.ProductText} fiyatı güncellendi."; }
            catch (Exception ex) { Dialog.Error(ex.Message, "Fiyat Matrisi"); }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AddPrice_Click(object sender, RoutedEventArgs e)
    {
        if (ProductPicker.SelectedValue is not int pid || pid <= 0) { Dialog.Warn("Önce sağdan bir ürün seçin.", "Fiyat Matrisi"); return; }
        var prod = _products.GetById(pid);
        if (prod is null) return;
        var units = _products.GetUnits(pid);
        if (units.Count == 0 && prod.MainUnitId is int mu)
            units = new List<ProductUnit> { new() { UnitId = mu, UnitName = prod.MainUnitName, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 } };
        if (units.Count == 0) { Dialog.Warn("Üründe birim yok; önce ürün kartından ana birim ekleyin.", "Fiyat Matrisi"); return; }

        var w = new ProductPriceEditWindow(units, prod.Name) { Owner = this };
        if (w.ShowDialog() == true && w.Results.Count > 0)
        {
            try { foreach (var pr in w.Results) _products.AddScopedPrice(pid, pr, Session.CurrentUserId); LoadList(); }
            catch (Exception ex) { Dialog.Error(ex.Message, "Fiyat Matrisi"); }
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MatrixRow r) return;
        if (!Dialog.Confirm($"{r.Src.ProductText}\n{r.BranchText} · {r.ListText} · {r.Src.PriceText}\n\nBu fiyat satırı silinsin mi?", "Fiyat Sil")) return;
        try { _products.DeletePriceById(r.Src.Id, Session.CurrentUserId); LoadList(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Fiyat Matrisi"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public class MatrixRow : INotifyPropertyChanged
{
    public ProductPrice Src { get; }
    public MatrixRow(ProductPrice p) { Src = p; _price = p.Price.ToString("0.##", new CultureInfo("tr-TR")); _tax = p.IsTaxIncluded; }

    public string ProductText => Src.ProductText;
    public string TypeText => Src.PriceTypeText;
    public string? UnitName => Src.UnitName;
    public string BranchText => Src.BranchScopeText;
    public string ListText => Src.ListScopeText;
    public string? CurrencyCode => Src.CurrencyCode;
    public string PeriodText => Src.PeriodText;

    private string _price;
    public string PriceInput { get => _price; set { _price = value; OnChanged(nameof(PriceInput)); } }
    private bool _tax;
    public bool TaxIncluded { get => _tax; set { _tax = value; OnChanged(nameof(TaxIncluded)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
