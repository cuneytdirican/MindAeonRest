using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PaketWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly CourierService _couriers = new();
    private readonly int _branchId;
    private bool _outgoing;

    public PaketWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";
        Loaded += (_, _) => { ShowIncoming(); };
    }

    private Order? Selected => OrderList.SelectedItem as Order;

    private void LoadList()
    {
        int? keep = Selected?.Id;
        var list = _orders.GetPackages(_branchId, _outgoing);
        OrderList.ItemsSource = list;
        IncomingBtn.Opacity = _outgoing ? 0.5 : 1; OutgoingBtn.Opacity = _outgoing ? 1 : 0.5;
        var sel = list.FirstOrDefault(o => o.Id == keep);
        if (sel != null) OrderList.SelectedItem = sel;
        else if (list.Count > 0) OrderList.SelectedIndex = 0;
        else ShowDetail(null);
    }

    private void Order_Changed(object sender, SelectionChangedEventArgs e) => ShowDetail(Selected);

    private void ShowDetail(Order? o)
    {
        if (o is null) { DetailRoot.Visibility = Visibility.Collapsed; return; }
        DetailRoot.Visibility = Visibility.Visible;
        DName.Text = string.IsNullOrWhiteSpace(o.DeliveryName) ? o.CustomerName : o.DeliveryName;
        DInfo.Text = $"{o.PlatformText} · {o.Code} · {o.DeliveryPhone}";
        DAddr.Text = o.DeliveryAddress;
        DNote.Text = o.Note;
        DStatus.Text = o.DeliveryStatusText;
        DStatusBadge.Background = o.DeliveryStatus switch
        {
            DeliveryStatus.TeslimEdildi => new SolidColorBrush(Color.FromRgb(0x17, 0xB2, 0x6A)),
            DeliveryStatus.YolaCikti => new SolidColorBrush(Color.FromRgb(0x3F, 0x51, 0xB5)),
            DeliveryStatus.Iptal => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
            _ => new SolidColorBrush(Color.FromRgb(0xE9, 0xA2, 0x3B))
        };
        ItemsHost.ItemsSource = _orders.GetLines(o.Id).Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal).ToList();
        CourierCombo.ItemsSource = _couriers.GetAll().Where(k => k.IsActive).Select(k => new ComboItem(k.Id, k.Name)).ToList();
        CourierCombo.SelectedValue = o.CourierId;
    }

    private void ShowIncoming() { _outgoing = false; LoadList(); }
    private void ShowOutgoing() { _outgoing = true; LoadList(); }
    private void ShowIncoming_Click(object sender, RoutedEventArgs e) => ShowIncoming();
    private void ShowOutgoing_Click(object sender, RoutedEventArgs e) => ShowOutgoing();
    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadList();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var zone = new ZoneService().GetAll().FirstOrDefault(z => z.BranchId == _branchId);
        if (zone is null) { Dialog.Info("Şubede lokasyon tanımlı olmalı.", "Uyarı"); return; }
        var dlg = new PaketNewWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int pa = new CustomerService().GetAll().First(c => c.Code == "PA").Id;
        try
        {
            var order = _orders.CreatePackage(_branchId, zone.Id, pa, dlg.Platform, dlg.OrderType, dlg.CustomerName, dlg.Phone, dlg.Address, Session.CurrentUserId);
            new AdisyonWindow(order) { Owner = this }.ShowDialog();   // ürün ekle
            ShowIncoming();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void SetStatus(DeliveryStatus s)
    {
        if (Selected is not { } o) return;
        _orders.SetDeliveryStatus(o.Id, s, Session.CurrentUserId);
        LoadList();
    }
    private void StHazir_Click(object sender, RoutedEventArgs e) => SetStatus(DeliveryStatus.Hazirlaniyor);
    private void StYola_Click(object sender, RoutedEventArgs e) => SetStatus(DeliveryStatus.YolaCikti);
    private void StTeslim_Click(object sender, RoutedEventArgs e) => SetStatus(DeliveryStatus.TeslimEdildi);
    private void StIptal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (!Dialog.Confirm("Sipariş iptal edilsin mi?", "İptal")) return;
        SetStatus(DeliveryStatus.Iptal);
    }

    private void AssignCourier_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } o) return;
        if (CourierCombo.SelectedValue is not int cid) { Dialog.Info("Kurye seçin.", "Uyarı"); return; }
        _orders.AssignCourier(o.Id, cid, Session.CurrentUserId);
        LoadList();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } o) return;
        new AdisyonWindow(o) { Owner = this }.ShowDialog();
        LoadList();
    }

    private void Receipt_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } o) new HesapFisiWindow(o) { Owner = this }.ShowDialog();
    }

    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("Ödeme alma yetkiniz yok (yalnızca Kasiyer/Yönetici)."); return; }
        if (Selected is not { } o) return;
        var w = new PaymentWindow(o) { Owner = this };
        if (w.ShowDialog() == true) LoadList();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
