using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReservationEditWindow : Window
{
    private readonly ReservationService _service = new();
    private readonly CustomerService _customerService = new();
    private readonly ReservationTagService _tagService = new();
    private readonly Reservation _reservation;
    private readonly List<int> _tableIds;
    private readonly int _zoneId;
    private readonly int _branchId;

    public ReservationEditWindow(int zoneId, int branchId, string zoneName, List<Table> tables, DateTime start, DateTime end, Reservation? existing = null)
    {
        InitializeComponent();
        _zoneId = zoneId;
        _branchId = branchId;
        _reservation = existing ?? new Reservation();
        _tableIds = tables.Select(t => t.Id).ToList();

        CustomerPicker.Configure(() => _customerService.GetAll(), AddCustomer, existing?.CustomerId);
        TagPicker.Configure(() => _tagService.GetAll(), AddTag, existing?.TagId);
        StatusCombo.ItemsSource = Enum.GetValues<ReservationStatus>().Select(s => new ComboItem((int)s, s.ToTr())).ToList();
        StatusCombo.SelectedValue = (int)(existing?.Status ?? ReservationStatus.Beklemede);

        TablesHost.ItemsSource = tables.Select(t => $"{t.Code} ({t.Seats}k)").ToList();
        SubText.Text = $"{zoneName} · {tables.Count} masa";

        DateTime s = existing?.StartTime ?? start;
        DateTime e = existing?.EndTime ?? end;
        DateBox.SelectedDate = s.Date;
        StartBox.Text = s.ToString("HH:mm");
        EndBox.Text = e.ToString("HH:mm");
        PartyBox.Text = (existing?.PartySize ?? tables.Sum(t => t.Seats)).ToString();
        NoteBox.Text = existing?.Note ?? "";

        if (existing is not null) TitleText.Text = $"Rezervasyon · {existing.Code}";
        Loaded += (_, _) => CustomerPicker.Focus();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private int? AddTag()
    {
        var w = new ReservationTagEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CustomerPicker.SelectedValue is not int customerId) { ShowError("Müşteri (misafir) seçiniz."); return; }
        if (DateBox.SelectedDate is not DateTime date) { ShowError("Tarih seçiniz."); return; }
        if (!TimeSpan.TryParse(StartBox.Text, CultureInfo.InvariantCulture, out var st)) { ShowError("Başlangıç saati geçersiz (örn. 19:30)."); return; }
        if (!TimeSpan.TryParse(EndBox.Text, CultureInfo.InvariantCulture, out var et)) { ShowError("Bitiş saati geçersiz (örn. 21:30)."); return; }

        DateTime startTime = date.Date + st;
        DateTime endTime = date.Date + et;
        if (endTime <= startTime) { ShowError("Bitiş saati başlangıçtan sonra olmalı."); return; }
        int.TryParse(PartyBox.Text, out var party);

        _reservation.BranchId = _branchId;
        _reservation.ZoneId = _zoneId;
        _reservation.CustomerId = customerId;
        _reservation.TagId = TagPicker.SelectedValue;
        _reservation.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        _reservation.StartTime = startTime;
        _reservation.EndTime = endTime;
        _reservation.PartySize = party;
        _reservation.Status = StatusCombo.SelectedValue is int s ? (ReservationStatus)s : ReservationStatus.Beklemede;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_reservation, _tableIds, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
