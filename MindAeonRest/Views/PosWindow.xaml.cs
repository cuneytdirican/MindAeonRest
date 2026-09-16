using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PosWindow : Window
{
    private readonly PosMovementService _pos = new();

    public PosWindow()
    {
        InitializeComponent();
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";
        Loaded += (_, _) => LoadBalances();
    }

    private PosBalance? Selected => BalancesGrid.SelectedItem as PosBalance;

    private void LoadBalances()
    {
        int? keep = Selected?.PosId;
        var list = _pos.GetBalances();
        BalancesGrid.ItemsSource = list;
        var sel = list.FirstOrDefault(x => x.PosId == keep) ?? list.FirstOrDefault();
        if (sel != null) BalancesGrid.SelectedItem = sel;
    }

    private void Balance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is null) { MovementsGrid.ItemsSource = null; return; }
        MovTitle.Text = $"HAREKETLER · {Selected.Name}";
        MovementsGrid.ItemsSource = _pos.GetMovements(Selected.PosId);
    }

    private List<ComboItem> Customers() =>
        new CustomerService().GetAll().Select(c => new ComboItem(c.Id, $"{c.Code} · {c.Name}")).ToList();

    private void Collect_Click(object sender, RoutedEventArgs e) => CariMove(BankMovementType.Tahsilat, "Tahsilat");
    private void Pay_Click(object sender, RoutedEventArgs e) => CariMove(BankMovementType.Odeme, "Ödeme");

    private void CariMove(BankMovementType type, string title)
    {
        if (Selected is null) { NeedSelect(); return; }
        var w = new BankaIslemWindow($"{title} · {Selected.Name}", needsCustomer: true, transfer: false, customers: Customers()) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _pos.AddManual(Selected.PosId, type, w.Amount, w.CustomerId, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { NeedSelect(); return; }
        var src = Selected;
        var targets = _pos.GetBalances()
            .Where(x => x.PosId != src.PosId && x.CurrencyId == src.CurrencyId)
            .Select(x => new ComboItem(x.PosId, x.Name)).ToList();
        if (targets.Count == 0) { Dialog.Info("Aynı dövizde başka POS yok.", "Bilgi"); return; }
        var w = new BankaIslemWindow($"Transfer · {src.Name} →", needsCustomer: false, transfer: true, targets) { Owner = this };
        if (w.ShowDialog() != true || w.TargetId is not int target) return;
        try { _pos.Transfer(src.PosId, target, w.Amount, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadBalances();
    private static void NeedSelect() => Dialog.Info("Önce bir POS seçin.", "Bilgi");
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
