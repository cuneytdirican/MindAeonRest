using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class KasaWindow : Window
{
    private readonly CashMovementService _cash = new();
    private readonly int _branchId;

    public KasaWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";
        Loaded += (_, _) => LoadBalances();
    }

    private CashBalance? Selected => BalancesGrid.SelectedItem as CashBalance;

    private void LoadBalances()
    {
        int? keep = Selected?.CashRegisterId;
        var list = _cash.GetBalances(_branchId);
        BalancesGrid.ItemsSource = list;
        var sel = list.FirstOrDefault(x => x.CashRegisterId == keep) ?? list.FirstOrDefault();
        if (sel != null) BalancesGrid.SelectedItem = sel;
    }

    private void Balance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is null) { MovementsGrid.ItemsSource = null; return; }
        MovTitle.Text = $"HAREKETLER · {Selected.Name}";
        MovementsGrid.ItemsSource = _cash.GetMovements(Selected.CashRegisterId);
    }

    private void In_Click(object sender, RoutedEventArgs e) => Manual(CashMovementType.Giris, "Para Girişi");
    private void Out_Click(object sender, RoutedEventArgs e) => Manual(CashMovementType.Cikis, "Para Çıkışı");

    private void Collect_Click(object sender, RoutedEventArgs e) => Cari(true, "Tahsilat");
    private void Pay_Click(object sender, RoutedEventArgs e) => Cari(false, "Ödeme");

    private void Cari(bool tahsilat, string title)
    {
        if (!Permissions.Cash) { Dialog.Warn("Kasa işlemi için yetkiniz yok (yalnızca Kasiyer/Yönetici)."); return; }
        if (Selected is null) { NeedSelect(); return; }
        var customers = new CustomerService().GetAll().Select(x => new ComboItem(x.Id, $"{x.Code} · {x.Name}")).ToList();
        var w = new BankaIslemWindow($"{title} · {Selected.Name}", needsCustomer: true, transfer: false, customers: customers) { Owner = this };
        if (w.ShowDialog() != true || w.CustomerId is not int cid) return;
        try { _cash.AddCari(Selected.CashRegisterId, tahsilat, w.Amount, cid, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Manual(CashMovementType type, string title)
    {
        if (!Permissions.Cash) { Dialog.Warn("Kasa işlemi için yetkiniz yok (yalnızca Kasiyer/Yönetici)."); return; }
        if (Selected is null) { NeedSelect(); return; }
        var w = new KasaIslemWindow($"{title} · {Selected.Name}", transfer: false) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _cash.AddManual(Selected.CashRegisterId, type, w.Amount, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Cash) { Dialog.Warn("Kasa transferi için yetkiniz yok (yalnızca Kasiyer/Yönetici)."); return; }
        if (Selected is null) { NeedSelect(); return; }
        var src = Selected;
        var targets = _cash.GetBalances(_branchId)
            .Where(x => x.CashRegisterId != src.CashRegisterId && x.CurrencyId == src.CurrencyId)
            .Select(x => new ComboItem(x.CashRegisterId, x.Name)).ToList();
        if (targets.Count == 0) { Dialog.Info("Aynı dövizde başka kasa yok.", "Uyarı"); return; }
        var w = new KasaIslemWindow($"Transfer · {src.Name} →", transfer: true, targets) { Owner = this };
        if (w.ShowDialog() != true || w.TargetId is not int target) return;
        try { _cash.Transfer(src.CashRegisterId, target, w.Amount, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadBalances();
    private static void NeedSelect() => Dialog.Info("Önce bir kasa seçin.", "Uyarı");
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
