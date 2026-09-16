using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReservationWindow : Window
{
    private readonly ZoneService _zoneService = new();
    private readonly TableService _tableService = new();
    private readonly ReservationService _reservationService = new();
    private readonly ObservableCollection<TableItemVM> _items = new();
    private readonly Dictionary<int, int> _zoneBranch = new();
    private readonly Dictionary<int, Table> _tablesById = new();
    private int _zoneId;
    private int _branchId;
    private bool _ready;

    public ReservationWindow()
    {
        InitializeComponent();
        TableHost.ItemsSource = _items;
        var zones = _zoneService.GetAll();
        foreach (var z in zones) _zoneBranch[z.Id] = z.BranchId;
        ZoneCombo.ItemsSource = zones.Select(z => new ComboItem(z.Id, z.Display)).ToList();
        DateBox.SelectedDate = DateTime.Today;
        StartBox.Text = "19:00";
        EndBox.Text = "21:00";
        _ready = true;
        if (ZoneCombo.Items.Count > 0) ZoneCombo.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) Refresh(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private bool ParseWindow(out DateTime start, out DateTime end)
    {
        start = end = default;
        if (DateBox.SelectedDate is not DateTime date) return false;
        if (!TimeSpan.TryParse(StartBox.Text, CultureInfo.InvariantCulture, out var st)) return false;
        if (!TimeSpan.TryParse(EndBox.Text, CultureInfo.InvariantCulture, out var et)) return false;
        start = date.Date + st;
        end = date.Date + et;
        return end > start;
    }

    private void Refresh()
    {
        if (ZoneCombo.SelectedValue is not int zid) return;
        _zoneId = zid;
        _branchId = _zoneBranch.TryGetValue(zid, out var b) ? b : 0;

        try
        {
            _items.Clear();
            _tablesById.Clear();
            foreach (var t in _tableService.GetByZone(zid))
            {
                _tablesById[t.Id] = t;
                _items.Add(TableItemVM.From(t));
            }

            if (ParseWindow(out var start, out var end))
            {
                var reserved = _reservationService.GetReservedTableIds(zid, start, end);
                foreach (var vm in _items) vm.BoardState = reserved.Contains(vm.Id) ? 1 : 0;
            }
            else
            {
                foreach (var vm in _items) vm.BoardState = 0;
            }

            DateTime date = DateBox.SelectedDate ?? DateTime.Today;
            DayList.ItemsSource = _reservationService.GetByDate(zid, date);
            UpdateSelection();
        }
        catch (Exception ex)
        {
            Dialog.Error(ex.Message, "Hata");
        }
    }

    private void Table_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TableItemVM vm }) return;

        if (vm.BoardState == 1)
        {
            // Dolu masa: ilgili rezervasyonu aç
            var res = (DayList.ItemsSource as IEnumerable<Reservation>)?.FirstOrDefault(r => r.TableIds.Contains(vm.Id));
            if (res != null) OpenReservation(res);
            return;
        }
        vm.BoardState = vm.BoardState == 2 ? 0 : 2;   // boş <-> seçili
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var sel = _items.Where(i => i.BoardState == 2).ToList();
        ReserveButton.IsEnabled = sel.Count > 0;
        SelectedText.Text = sel.Count == 0
            ? "Boş masalara tıklayarak seçin."
            : $"{sel.Count} masa: {string.Join(", ", sel.Select(s => s.Code))} · {sel.Sum(s => _tablesById.TryGetValue(s.Id, out var t) ? t.Seats : 0)} kişilik";
    }

    private void Reserve_Click(object sender, RoutedEventArgs e)
    {
        var tables = _items.Where(i => i.BoardState == 2)
            .Select(i => _tablesById.GetValueOrDefault(i.Id)).Where(t => t != null).Cast<Table>().ToList();
        if (tables.Count == 0) return;
        if (!ParseWindow(out var start, out var end)) { Dialog.Info("Geçerli bir tarih/saat aralığı girin.", "Uyarı"); return; }

        string zoneName = (ZoneCombo.SelectedItem as ComboItem)?.Text ?? "";
        var win = new ReservationEditWindow(_zoneId, _branchId, zoneName, tables, start, end) { Owner = this };
        if (win.ShowDialog() == true) Refresh();
    }

    private void DayList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DayList.SelectedItem is Reservation res) OpenReservation(res);
    }

    private void OpenReservation(Reservation res)
    {
        var tables = res.TableIds.Select(id => _tablesById.GetValueOrDefault(id)).Where(t => t != null).Cast<Table>().ToList();
        string zoneName = (ZoneCombo.SelectedItem as ComboItem)?.Text ?? res.ZoneName ?? "";
        var win = new ReservationEditWindow(res.ZoneId, res.BranchId, zoneName, tables, res.StartTime, res.EndTime, res) { Owner = this };
        if (win.ShowDialog() == true) Refresh();
    }

    private void OpenOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is not Reservation res) { Dialog.Info("Önce listeden bir rezervasyon seçin.", "Bilgi"); return; }
        var orders = new OrderService();
        try
        {
            var tableIds = _reservationService.GetTableIds(res.Id);
            int branch = _zoneBranch.TryGetValue(res.ZoneId, out var b) ? b : (Session.CurrentBranch?.Id ?? 0);
            int custId = res.CustomerId != 0 ? res.CustomerId : new CustomerService().GetAll().First(c => c.Code == "PA").Id;
            var order = orders.CreateOrder(branch, res.ZoneId, custId, tableIds, Session.CurrentUserId, res.Id);
            _reservationService.SetStatus(res.Id, ReservationStatus.Geldi, Session.CurrentUserId);
            new AdisyonWindow(order) { Owner = this }.ShowDialog();
            Refresh();
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
