using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductPriceEditWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Sonuç fiyat(lar)ı: Satış zorunlu, Alış girilmişse ikincisi de döner.</summary>
    public List<ProductPrice> Results { get; } = new();

    public ProductPriceEditWindow(IEnumerable<ProductUnit> productUnits, string? productLabel = null, ProductPrice? existing = null)
    {
        InitializeComponent();

        // Pencere ekrana sığsın (büyük ekran/küçük ekran fark etmez; alanlar ScrollViewer'da)
        Height = System.Math.Min(700, SystemParameters.WorkArea.Height - 40);
        if (!string.IsNullOrWhiteSpace(productLabel)) HeaderTitle.Text = $"Fiyat · {productLabel}";

        var units = productUnits.GroupBy(u => u.UnitId).Select(g => g.First()).ToList();
        UnitCombo.ItemsSource = units.Select(u => new ComboItem(u.UnitId, u.UnitName ?? u.UnitId.ToString())).ToList();
        CurrencyCombo.ItemsSource = new CurrencyService().GetAll().Select(c => new ComboItem(c.Id, c.Code)).ToList();

        // Opsiyonel kapsam (0 = genel)
        BranchCombo.ItemsSource = WithGeneral(new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0).Select(b => new ComboItem(b.Id, b.Display)), "Tüm şubeler");
        CustomerCombo.ItemsSource = WithGeneral(new CustomerService().GetAll().Select(c => new ComboItem(c.Id, $"{c.Name} ({c.Code})")), "(Genel)");
        GroupCombo.ItemsSource = WithGeneral(new CustomerGroupService().GetAll().Select(g => new ComboItem(g.Id, g.Display)), "(Genel)");
        PriceListCombo.ItemsSource = WithGeneral(new PriceListService().GetAll().Select(p => new ComboItem(p.Id, p.Display)), "(Genel)");

        // BİRİM otomatik: ürünün ANA birimi (yoksa ilk birim) — kullanıcı isterse değiştirir
        int? mainUnit = units.FirstOrDefault(u => u.IsMainUnit)?.UnitId ?? units.FirstOrDefault()?.UnitId;
        // DÖVİZ otomatik: şirket varsayılanı (genelde TRY) — kullanıcı isterse değiştirir
        int? defCur = new CurrencyService().GetDefaultId();

        if (existing is not null)
        {
            UnitCombo.SelectedValue = existing.UnitId;
            CurrencyCombo.SelectedValue = existing.CurrencyId;
            if (existing.PriceType == PriceType.Alis) BuyPriceBox.Text = existing.Price.ToString("0.##", Tr);
            else SalePriceBox.Text = existing.Price.ToString("0.##", Tr);
            BranchCombo.SelectedValue = existing.BranchId ?? 0;
            CustomerCombo.SelectedValue = existing.CustomerId ?? 0;
            GroupCombo.SelectedValue = existing.CustomerGroupId ?? 0;
            PriceListCombo.SelectedValue = existing.PriceListId ?? 0;
            StartBox.Text = DateHelper.ToDateTimeText(existing.StartDateTime);
            EndBox.Text = DateHelper.ToDateTimeText(existing.EndDateTime);
            ActiveCheck.IsChecked = existing.IsActive;
            ListPriceBox.Text = existing.ListPrice is null or 0 ? "" : existing.ListPrice.Value.ToString("0.##", Tr);
            TaxInclCheck.IsChecked = existing.IsTaxIncluded;
        }
        else
        {
            UnitCombo.SelectedValue = mainUnit ?? 0;
            CurrencyCombo.SelectedValue = defCur ?? (CurrencyCombo.Items.Count > 0 ? ((ComboItem)CurrencyCombo.Items[0]!).Value : 0);
            BranchCombo.SelectedValue = 0;
            CustomerCombo.SelectedValue = 0;
            GroupCombo.SelectedValue = 0;
            PriceListCombo.SelectedValue = 0;
        }

        Loaded += (_, _) => SalePriceBox.Focus();
    }

    private static List<ComboItem> WithGeneral(IEnumerable<ComboItem> items, string generalText)
    {
        var list = new List<ComboItem> { new(0, generalText) };
        list.AddRange(items);
        return list;
    }

    private bool TryDec(string text, out decimal value) =>
        decimal.TryParse((text ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out value);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (UnitCombo.SelectedValue is not int unitId || unitId == 0) { Fail("Birim seçiniz (ör. Adet)."); return; }
        if (CurrencyCombo.SelectedValue is not int currencyId || currencyId == 0) { Fail("Döviz seçiniz."); return; }

        bool hasSale = !string.IsNullOrWhiteSpace(SalePriceBox.Text);
        bool hasBuy = !string.IsNullOrWhiteSpace(BuyPriceBox.Text);
        if (!hasSale && !hasBuy) { Fail("En az bir fiyat girin: Satış veya Alış."); return; }

        decimal sale = 0, buy = 0;
        if (hasSale && (!TryDec(SalePriceBox.Text, out sale) || sale < 0)) { Fail("Satış fiyatı geçerli bir sayı olmalı."); return; }
        if (hasBuy && (!TryDec(BuyPriceBox.Text, out buy) || buy < 0)) { Fail("Alış fiyatı geçerli bir sayı olmalı."); return; }

        DateTime? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(StartBox.Text))
        {
            if (!DateHelper.TryParseDateTime(StartBox.Text, out var s)) { Fail("Başlangıç gg.aa.yyyy SS:dd biçiminde olmalı."); return; }
            start = s;
        }
        if (!string.IsNullOrWhiteSpace(EndBox.Text))
        {
            if (!DateHelper.TryParseDateTime(EndBox.Text, out var en)) { Fail("Bitiş gg.aa.yyyy SS:dd biçiminde olmalı."); return; }
            if (start.HasValue && en < start) { Fail("Bitiş, başlangıçtan önce olamaz."); return; }
            end = en;
        }

        decimal? listPrice = null;
        if (!string.IsNullOrWhiteSpace(ListPriceBox.Text))
        {
            if (!TryDec(ListPriceBox.Text, out var lp) || lp < 0) { Fail("Liste fiyatı geçerli bir sayı olmalı."); return; }
            listPrice = lp;
        }

        int? NullIfZero(object? v) => v is int i && i > 0 ? i : null;
        ProductPrice Build(PriceType type, decimal price) => new()
        {
            UnitId = unitId,
            UnitName = (UnitCombo.SelectedItem as ComboItem)?.Text,
            PriceType = type,
            Price = price,
            ListPrice = listPrice,
            IsTaxIncluded = TaxInclCheck.IsChecked == true,
            CurrencyId = currencyId,
            CurrencyCode = (CurrencyCombo.SelectedItem as ComboItem)?.Text,
            BranchId = NullIfZero(BranchCombo.SelectedValue),
            BranchName = BranchCombo.SelectedValue is int b && b > 0 ? (BranchCombo.SelectedItem as ComboItem)?.Text : null,
            StartDateTime = start,
            EndDateTime = end,
            CustomerId = NullIfZero(CustomerCombo.SelectedValue),
            CustomerGroupId = NullIfZero(GroupCombo.SelectedValue),
            PriceListId = NullIfZero(PriceListCombo.SelectedValue),
            IsActive = ActiveCheck.IsChecked == true
        };

        Results.Clear();
        if (hasSale) Results.Add(Build(PriceType.Satis, sale));
        if (hasBuy) Results.Add(Build(PriceType.Alis, buy));

        DialogResult = true;
        Close();
    }

    // Hata: hem her zaman görünür alt satırda, hem de popup (kullanıcı asla "hiçbir şey olmadı" sanmasın)
    private void Fail(string m) { ShowError(m); Dialog.Warn(m, "Fiyat"); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
