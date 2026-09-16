using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class RefundDialog : Window
{
    private readonly int _branchId;
    private readonly decimal _maxQty;

    public decimal RefundQty { get; private set; }
    public bool ReturnToStock { get; private set; }
    public PaymentKind Kind { get; private set; }
    public int? InstrumentId { get; private set; }

    public RefundDialog(OrderLine line, int branchId)
    {
        InitializeComponent();
        _branchId = branchId;
        _maxQty = line.RefundableQty;
        TitleText.Text = $"İade · {line.ProductName}";
        SubText.Text = $"İade edilebilir: {_maxQty:0.##} adet · birim {line.UnitPrice:N2} ₺";
        QtyBox.Text = _maxQty.ToString("0.##", CultureInfo.InvariantCulture);

        KindCombo.ItemsSource = new List<ComboItem> { new(1, "Stoğa İade (kullanılabilir)"), new(0, "Fire (çöp, stoğa dönmez)") };
        KindCombo.SelectedIndex = 0;
        MethodCombo.ItemsSource = new List<ComboItem> { new((int)PaymentKind.Nakit, "Nakit"), new((int)PaymentKind.KrediKarti, "Kredi Kartı"), new((int)PaymentKind.YemekCeki, "Yemek Çeki") };
        MethodCombo.SelectedIndex = 0;
        Loaded += (_, _) => QtyBox.Focus();
    }

    private void Method_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MethodCombo.SelectedValue is not int k) return;
        var kind = (PaymentKind)k;
        List<ComboItem> items = kind switch
        {
            PaymentKind.Nakit => new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList(),
            PaymentKind.KrediKarti => new PosService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList(),
            PaymentKind.YemekCeki => new MealVoucherAccountService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList(),
            _ => new List<ComboItem>()
        };
        InstrumentCombo.ItemsSource = items;
        if (items.Count > 0) InstrumentCombo.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!decimal.TryParse(QtyBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        { Err("Geçerli bir miktar girin."); return; }
        if (qty > _maxQty) { Err($"En fazla {_maxQty:0.##} iade edilebilir."); return; }
        if (InstrumentCombo.SelectedValue is not int inst) { Err("İade aracı (kasa/POS/hesap) seçin."); return; }

        RefundQty = qty;
        ReturnToStock = (KindCombo.SelectedValue as int?) == 1;
        Kind = (PaymentKind)(MethodCombo.SelectedValue ?? 0);
        InstrumentId = inst;
        DialogResult = true;
        Close();
    }

    private void Err(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
