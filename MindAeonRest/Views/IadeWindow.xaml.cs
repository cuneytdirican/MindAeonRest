using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class IadeWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly int _branchId;

    public IadeWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        Loaded += (_, _) => { DateBox.SelectedDate = DateTime.Today; LoadOrders(); };
    }

    private Order? SelOrder => OrdersGrid.SelectedItem as Order;
    private OrderLine? SelLine => LinesGrid.SelectedItem as OrderLine;

    private void LoadOrders()
    {
        var date = DateBox.SelectedDate ?? DateTime.Today;
        OrdersGrid.ItemsSource = _orders.GetClosedOrders(_branchId, date);
        LinesGrid.ItemsSource = null;
    }

    private void Date_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) LoadOrders(); }

    private void Order_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SelOrder is null) { LinesGrid.ItemsSource = null; return; }
        LinesGrid.ItemsSource = _orders.GetLines(SelOrder.Id)
            .Where(l => l.ParentLineId == null && l.SettledClFicheId != null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat).ToList();
    }

    private void Refund_Click(object sender, RoutedEventArgs e)
    {
        if (SelLine is not { } line) { Dialog.Info("İade edilecek satırı seçin.", "Uyarı"); return; }
        if (line.RefundableQty <= 0) { Dialog.Info("Bu satır tamamen iade edilmiş.", "Bilgi"); return; }

        // İade yetkisi: 'İade' rolü olan kasiyer veya yönetici. Yoksa BAŞKA yetkili bir kullanıcı girip onaylayabilir.
        int? authUserId;
        if (Permissions.Refund) authUserId = Session.CurrentUserId;
        else
        {
            var sup = new SupervisorAuthWindow(
                "İade için 'İade' yetkisine sahip bir kullanıcı (veya yönetici) onayı gerekir. Lütfen yetkili giriş yapsın.",
                IsRefundAuthorized) { Owner = this };
            if (sup.ShowDialog() == true && sup.AuthorizedUser is { } au) authUserId = au.Id;
            else { Dialog.Warn("İade için yetki onayı alınamadı.", "İade"); return; }
        }

        var dlg = new RefundDialog(line, _branchId) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var cl = _orders.RefundLine(line.Id, dlg.RefundQty, dlg.ReturnToStock, dlg.Kind, dlg.InstrumentId, authUserId);
            Dialog.Info($"İade fişi: {cl.Code}\nTutar: {cl.Total:N2} ₺", "İade Tamam");
            Order_Changed(this, null!);
            LoadOrders();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private static bool IsRefundAuthorized(Models.User u)
        => string.Equals(u.RoleName, "SysAdmin", StringComparison.OrdinalIgnoreCase)
        || new UserRoleService().GetRoleNames(u.Id).Any(r =>
               r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) || r.Equals("İade", StringComparison.OrdinalIgnoreCase));

    private void EInvoice_Click(object sender, RoutedEventArgs e)
    {
        if (SelOrder is not { } o) { Dialog.Info("Sipariş seçin.", "Uyarı"); return; }
        try
        {
            var res = new EInvoiceService().SendForOrder(o, Session.CurrentUserId);
            if (res.Success)
                Dialog.Info($"e-Fatura gönderildi.\nNo: {res.InvoiceNo}\nETTN: {res.Ettn}\n{res.Message}", "e-Fatura");
            else
                Dialog.Warn(res.Message, "e-Fatura");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
