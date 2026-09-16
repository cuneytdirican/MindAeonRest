using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductBulkPriceWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly ProductService _products = new();
    private readonly List<BulkPriceRow> _rows = new();

    public ProductBulkPriceWindow()
    {
        InitializeComponent();
        Height = System.Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        Load();
    }

    private void Load()
    {
        int defCur = new CurrencyService().GetDefaultId() ?? 0;
        string defCurCode = new CurrencyService().GetAll().FirstOrDefault(c => c.Id == defCur)?.Code ?? "TRY";
        var prices = _products.GetGeneralSalePrices();

        _rows.Clear();
        foreach (var p in _products.GetAll(includeDeleted: false))
        {
            prices.TryGetValue(p.Id, out var pr);
            _rows.Add(new BulkPriceRow
            {
                ProductId = p.Id,
                Code = p.Code,
                Name = p.Name,
                CategoryNames = string.IsNullOrWhiteSpace(p.CategoryNames) ? "(Kategorisiz)" : p.CategoryNames,
                UnitId = pr?.UnitId is int u && u > 0 ? u : (p.MainUnitId ?? 0),
                UnitName = p.MainUnitName,
                CurrencyId = pr?.CurrencyId ?? defCur,
                CurrencyCode = defCurCode,
                TaxIncluded = pr?.IsTaxIncluded ?? true,
                PriceInput = pr is null ? "" : pr.Price.ToString("0.##", Tr)
            });
        }

        var view = new ListCollectionView(_rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BulkPriceRow.CategoryNames)));
        view.SortDescriptions.Add(new SortDescription(nameof(BulkPriceRow.CategoryNames), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(BulkPriceRow.Name), ListSortDirection.Ascending));
        Grid.ItemsSource = view;
        InfoText.Text = $"{_rows.Count} ürün listelendi.";
    }

    private static string Norm(string? s) =>
        (s ?? "").ToLowerInvariant().Replace("i̇", "i").Replace('İ', 'i').Replace('ı', 'i').Replace('î', 'i');

    private void Search_Changed(object sender, RoutedEventArgs e)
    {
        if (Grid.ItemsSource is not ListCollectionView view) return;
        var q = Norm(SearchBox.Text);
        view.Filter = string.IsNullOrWhiteSpace(q)
            ? null
            : o => o is BulkPriceRow r && (Norm(r.Code).Contains(q) || Norm(r.Name).Contains(q));
    }

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);

        int saved = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var r in _rows)
        {
            if (string.IsNullOrWhiteSpace(r.PriceInput)) { skipped++; continue; }
            if (!decimal.TryParse(r.PriceInput.Trim().Replace('.', ','), NumberStyles.Any, Tr, out var price) || price < 0)
            { errors.Add($"{r.Code} {r.Name}: geçersiz fiyat '{r.PriceInput}'"); continue; }
            if (r.UnitId <= 0) { errors.Add($"{r.Code} {r.Name}: birim yok (ürün kartından ana birim ekleyin)"); continue; }
            try { _products.UpsertGeneralSalePrice(r.ProductId, r.UnitId, price, r.TaxIncluded, r.CurrencyId, Session.CurrentUserId); saved++; }
            catch (System.Exception ex) { errors.Add($"{r.Code} {r.Name}: {ex.Message}"); }
        }

        if (errors.Count > 0)
            Dialog.Warn($"{saved} ürün kaydedildi, {errors.Count} hata:\n\n• {string.Join("\n• ", errors.Take(15))}", "Toplu Fiyat");
        else
            Dialog.Info($"{saved} ürünün fiyatı kaydedildi. ({skipped} ürün boş bırakıldığı için atlandı.)", "Toplu Fiyat");

        DialogResult = saved > 0;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public class BulkPriceRow : INotifyPropertyChanged
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CategoryNames { get; set; }
    public int UnitId { get; set; }
    public string? UnitName { get; set; }
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }

    private string _priceInput = "";
    public string PriceInput { get => _priceInput; set { _priceInput = value; OnChanged(nameof(PriceInput)); } }

    private bool _taxIncluded = true;
    public bool TaxIncluded { get => _taxIncluded; set { _taxIncluded = value; OnChanged(nameof(TaxIncluded)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
