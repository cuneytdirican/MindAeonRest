using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class KioskWindow : Window
{
    private readonly PosCatalogService _catalog = new();
    private readonly OrderService _orders = new();
    private readonly ObservableCollection<KioskCartItem> _cart = new();
    private List<PosProduct> _all = new();
    private readonly int _branchId;
    private int _zoneId;
    private int _pa;
    private int _cur;
    private int? _posId;

    public KioskWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        CartList.ItemsSource = _cart;
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        try
        {
            var zone = new ZoneService().GetAll().FirstOrDefault(z => z.BranchId == _branchId);
            if (zone is null) { Dialog.Info("KIOSK için şubede en az bir lokasyon tanımlı olmalı.", "Uyarı"); Close(); return; }
            _zoneId = zone.Id;
            _pa = new CustomerService().GetAll().First(c => c.Code == "PA").Id;
            _cur = new CurrencyService().GetDefaultId() ?? 0;
            _posId = new PosService().GetAll().FirstOrDefault(p => p.IsActive && p.BranchId == _branchId)?.Id;
            if (_posId is null)
            {
                CompleteButton.IsEnabled = false;
                CompleteButton.ToolTip = "Bu şubede aktif bir POS (kredi kartı) cihazı tanımlı değil; KIOSK siparişi alınamaz.";
                Dialog.Info("Bu şubede aktif bir POS (kredi kartı) cihazı tanımlı değil. KIOSK sipariş alamaz; lütfen yöneticinizle iletişime geçin.", "Uyarı");
            }

            var (color, _) = Session.CurrentCompany is null ? (null, null) : new CompanyService().GetLogos(Session.CurrentCompany.Id);
            if (color is { Length: > 0 })
            {
                var img = new BitmapImage(); using var ms = new MemoryStream(color);
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
                LogoImg.Source = img;
            }

            var cats = new List<ProductCategory> { new() { Id = 0, Name = "Tümü" } };
            cats.AddRange(_catalog.GetCategories());
            CategoryList.ItemsSource = cats;
            _all = _catalog.GetProducts(_branchId);
            CategoryList.SelectedIndex = 0;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        int catId = (CategoryList.SelectedItem as ProductCategory)?.Id ?? 0;
        IEnumerable<PosProduct> items = _all;
        if (catId != 0) items = items.Where(p => p.CategoryIds.Contains(catId));
        ProductHost.ItemsSource = items.ToList();
    }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PosProduct p) return;
        if (p.IsSalesStopped) { Dialog.Info($"{p.Name} satışa kapalı.", "Bilgi"); return; }
        var existing = _cart.FirstOrDefault(x => x.ProductId == p.Id && string.IsNullOrEmpty(x.Note));
        if (existing != null) existing.Qty++;
        else _cart.Add(new KioskCartItem(p));
        Recalc();
    }

    private void Barcode_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        string code = BarcodeBox.Text.Trim();
        BarcodeBox.Clear();
        if (code.Length == 0) return;
        int? pid = _orders.ResolveProductIdByBarcode(code);
        var p = pid is int id ? _all.FirstOrDefault(x => x.Id == id) : null;
        if (p is null) { Dialog.Info("Barkod bulunamadı.", "Barkod"); return; }
        if (p.IsSalesStopped) { Dialog.Info($"{p.Name} satışa kapalı.", "Bilgi"); return; }
        var existing = _cart.FirstOrDefault(x => x.ProductId == p.Id && string.IsNullOrEmpty(x.Note));
        if (existing != null) existing.Qty++; else _cart.Add(new KioskCartItem(p));
        Recalc();
    }

    private void Inc_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is KioskCartItem it) { it.Qty++; Recalc(); } }
    private void Dec_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KioskCartItem it) return;
        it.Qty--;
        if (it.Qty <= 0) _cart.Remove(it);
        Recalc();
    }

    private void Note_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KioskCartItem it) return;
        var w = new NoteWindow(it.ProductId, it.Note, it.Name) { Owner = this };
        if (w.ShowDialog() == true) it.Note = w.ResultNote ?? "";
    }

    private void Recalc() => TotalText.Text = $"₺{_cart.Sum(x => x.Qty * x.Price):N2}";

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) { Dialog.Info("Sepetiniz boş.", "Bilgi"); return; }
        if (_posId is not int pos)
        {
            Dialog.Warn("Bu şubede aktif bir POS (kredi kartı) cihazı tanımlı olmadığından ödeme alınamaz; sipariş tamamlanamadı.", "Ödeme alınamadı");
            return;
        }
        try
        {
            var order = _orders.CreateOrder(_branchId, _zoneId, _pa, Array.Empty<int>(), Session.CurrentUserId);
            foreach (var it in _cart)
                _orders.AddLine(order.Id, _zoneId, it.ProductId, it.Qty, it.Price, it.TaxRate, string.IsNullOrWhiteSpace(it.Note) ? null : it.Note, 0, Session.CurrentUserId);
            _orders.SendToKitchen(order.Id, Session.CurrentUserId);

            decimal total = _cart.Sum(x => x.Qty * x.Price);
            _orders.SettleLines(order.Id, null, new[] { new PaymentLine { Kind = PaymentKind.KrediKarti, InstrumentId = pos, CurrencyId = _cur, Amount = total } }, null, Session.CurrentUserId);

            OrderNoText.Text = order.Code;
            SuccessOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void NewOrder_Click(object sender, RoutedEventArgs e)
    {
        _cart.Clear(); Recalc();
        SuccessOverlay.Visibility = Visibility.Collapsed;
        if (CategoryList.Items.Count > 0) CategoryList.SelectedIndex = 0;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public class KioskCartItem : INotifyPropertyChanged
{
    public int ProductId { get; }
    public string Name { get; }
    public decimal Price { get; }
    public decimal TaxRate { get; }
    private int _qty = 1;
    private string _note = "";

    public KioskCartItem(PosProduct p) { ProductId = p.Id; Name = p.Name; Price = p.Price; TaxRate = p.TaxRate; }

    public int Qty { get => _qty; set { _qty = value; On(nameof(Qty)); On(nameof(TotalText)); } }
    public string Note { get => _note; set { _note = value; On(nameof(Note)); } }
    public string TotalText => $"₺{Qty * Price:N2}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
