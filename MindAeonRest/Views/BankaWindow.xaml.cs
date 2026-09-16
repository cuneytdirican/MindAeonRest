using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class BankaWindow : Window
{
    private readonly BankMovementService _bank = new();

    public BankaWindow()
    {
        InitializeComponent();
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";
        Loaded += (_, _) => LoadBalances();
    }

    private BankBalance? Selected => BalancesGrid.SelectedItem as BankBalance;

    private void LoadBalances()
    {
        int? keep = Selected?.BankAccountId;
        var list = _bank.GetBalances();
        BalancesGrid.ItemsSource = list;
        var sel = list.FirstOrDefault(x => x.BankAccountId == keep) ?? list.FirstOrDefault();
        if (sel != null) BalancesGrid.SelectedItem = sel;
    }

    private void Balance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is null) { MovementsGrid.ItemsSource = null; return; }
        MovTitle.Text = $"HAREKETLER · {Selected.Name}";
        MovementsGrid.ItemsSource = _bank.GetMovements(Selected.BankAccountId);
    }

    private List<ComboItem> Customers() =>
        new CustomerService().GetAll().Select(c => new ComboItem(c.Id, $"{c.Code} · {c.Name}")).ToList();

    private void Collect_Click(object sender, RoutedEventArgs e) => CariMove(BankMovementType.Tahsilat, "Tahsilat");
    private void Pay_Click(object sender, RoutedEventArgs e) => CariMove(BankMovementType.Odeme, "Ödeme");
    private void In_Click(object sender, RoutedEventArgs e) => Manual(BankMovementType.Giris, "Para Girişi");
    private void Out_Click(object sender, RoutedEventArgs e) => Manual(BankMovementType.Cikis, "Para Çıkışı");

    private void CariMove(BankMovementType type, string title)
    {
        if (Selected is null) { NeedSelect(); return; }
        var w = new BankaIslemWindow($"{title} · {Selected.Name}", needsCustomer: true, transfer: false, customers: Customers()) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _bank.AddManual(Selected.BankAccountId, type, w.Amount, w.CustomerId, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Manual(BankMovementType type, string title)
    {
        if (Selected is null) { NeedSelect(); return; }
        var w = new BankaIslemWindow($"{title} · {Selected.Name}", needsCustomer: false, transfer: false) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _bank.AddManual(Selected.BankAccountId, type, w.Amount, null, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { NeedSelect(); return; }
        var src = Selected;
        var targets = _bank.GetBalances()
            .Where(x => x.BankAccountId != src.BankAccountId && x.CurrencyId == src.CurrencyId)
            .Select(x => new ComboItem(x.BankAccountId, x.Name)).ToList();
        if (targets.Count == 0) { Dialog.Info("Aynı dövizde başka banka hesabı yok.", "Bilgi"); return; }
        var w = new BankaIslemWindow($"Transfer · {src.Name} →", needsCustomer: false, transfer: true, targets) { Owner = this };
        if (w.ShowDialog() != true || w.TargetId is not int target) return;
        try { _bank.Transfer(src.BankAccountId, target, w.Amount, w.Note, Session.CurrentUserId); LoadBalances(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadBalances();
    private static void NeedSelect() => Dialog.Info("Önce bir banka hesabı seçin.", "Bilgi");
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
