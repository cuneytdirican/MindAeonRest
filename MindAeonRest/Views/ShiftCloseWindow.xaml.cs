using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>
/// Vardiya Kapanışı: bu terminalin (şube + KIOSK) açık vardiyasını yükler.
/// Açık vardiya yoksa açılış kasası girilerek "Vardiya Aç". Açık vardiya varsa raporu
/// (ödeme yöntemi + tahsil eden kullanıcı + beklenen nakit) gösterip sayılan nakit girdirir,
/// farkı hesaplar ve "Vardiya Kapat" ile kapatır. Yetki: Permissions.Payment (kasiyer/admin).
/// </summary>
public partial class ShiftCloseWindow : Window
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private readonly ShiftService _shifts = new();
    private readonly int _branchId;
    private readonly int _kioskNo;

    private Shift? _open;
    private ShiftReport? _report;

    public ShiftCloseWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        _kioskNo = Session.CurrentKioskNo;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        try
        {
            ContextText.Text = $"{Session.CurrentBranch?.Name ?? "-"} · KIOSK {_kioskNo}";
            _open = _shifts.GetOpenShift(_branchId, _kioskNo);

            if (_open is null)
            {
                StatusText.Text = "Vardiya yok";
                OpenPanel.Visibility = Visibility.Visible;
                ClosePanel.Visibility = Visibility.Collapsed;
                OpenShiftButton.Visibility = Visibility.Visible;
                CloseShiftButton.Visibility = Visibility.Collapsed;
                return;
            }

            StatusText.Text = $"Açık · {_open.OpenedAtText}";
            OpenPanel.Visibility = Visibility.Collapsed;
            ClosePanel.Visibility = Visibility.Visible;
            OpenShiftButton.Visibility = Visibility.Collapsed;
            CloseShiftButton.Visibility = Visibility.Visible;

            _report = _shifts.GetReport(_open.Id);
            ShiftSub.Text = $"Açılış: {_open.OpenedAtText}";
            TotalText.Text = _report.TotalText;
            PayHost.ItemsSource = _report.Payments;
            UserHost.ItemsSource = _report.ByUser;
            OpeningText.Text = _open.OpeningFloatText;
            CashCollectedText.Text = _report.CashCollectedText;
            ExpectedCashText.Text = _report.ExpectedCashText;
            CountedCashBox.Text = _report.ExpectedCash.ToString("0.##", Tr);
            UpdateDiff();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private static decimal ParseAmount(string? raw)
    {
        decimal.TryParse((raw ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out var v);
        return v;
    }

    private void Counted_Changed(object sender, TextChangedEventArgs e) => UpdateDiff();

    private void UpdateDiff()
    {
        if (_report is null) return;
        decimal counted = ParseAmount(CountedCashBox.Text);
        decimal diff = counted - _report.ExpectedCash;
        DiffText.Text = $"₺{diff:N2}";
    }

    private void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("Vardiya açmak için yetkiniz yok. Yalnız kasiyer veya yönetici yapabilir."); return; }
        if (_branchId == 0) { Dialog.Warn("Vardiya için aktif bir şube gerekir."); return; }
        decimal opening = ParseAmount(OpeningFloatBox.Text);
        try
        {
            _shifts.OpenShift(_branchId, _kioskNo, opening, Session.CurrentUserId);
            Dialog.Info("Vardiya açıldı.", "Bilgi");
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (_open is null) return;
        if (!Permissions.Payment) { Dialog.Warn("Vardiya kapatmak için yetkiniz yok. Yalnız kasiyer veya yönetici yapabilir."); return; }
        decimal counted = ParseAmount(CountedCashBox.Text);
        decimal expected = _report?.ExpectedCash ?? 0;
        decimal diff = counted - expected;
        string note = (NoteBox.Text ?? "").Trim();

        string msg = $"Vardiya kapatılsın mı?\n\nBeklenen nakit: ₺{expected:N2}\nSayılan nakit: ₺{counted:N2}\nFark: ₺{diff:N2}";
        if (!Dialog.Confirm(msg, "Vardiya Kapat")) return;
        try
        {
            _shifts.CloseShift(_open.Id, counted, string.IsNullOrEmpty(note) ? null : note, Session.CurrentUserId);
            Dialog.Info("Vardiya kapatıldı. (Nakit terminalde kalır; gün sonu Z devri ayrıdır.)", "Bilgi");
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
