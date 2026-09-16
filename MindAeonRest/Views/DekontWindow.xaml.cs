using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Para giriş/çıkış dekontları (Kasa / Banka / Kredi Kartı). Cari seçilirse aynı anda cari hareketi de oluşur.</summary>
public enum DekontKind { KasaGiris, KasaCikis, BankaGiris, BankaCikis, KartTahsilat, KartOdeme }

public partial class DekontWindow : Window
{
    private readonly DekontKind _kind;
    private readonly int _branchId;

    private bool IsIn => _kind is DekontKind.KasaGiris or DekontKind.BankaGiris or DekontKind.KartTahsilat;
    private bool IsCard => _kind is DekontKind.KartTahsilat or DekontKind.KartOdeme;
    private bool IsCash => _kind is DekontKind.KasaGiris or DekontKind.KasaCikis;
    private bool IsBank => _kind is DekontKind.BankaGiris or DekontKind.BankaCikis;

    public DekontWindow(DekontKind kind)
    {
        InitializeComponent();
        _kind = kind;
        _branchId = Session.CurrentBranch?.Id ?? 0;

        (string title, string sub, string accLabel, string badge, Color color) = kind switch
        {
            DekontKind.KasaGiris => ("Nakit Kasa — Giriş Dekontu", "Kasaya para girişi. Cari seçilirse müşteriden TAHSİLAT olarak işlenir (cari alacak).", "NAKİT KASA", "GİRİŞ ▲", Color.FromRgb(0x16, 0xA3, 0x4A)),
            DekontKind.KasaCikis => ("Nakit Kasa — Çıkış Dekontu", "Kasadan para çıkışı. Cari seçilirse tedarikçiye ÖDEME olarak işlenir (cari borç).", "NAKİT KASA", "ÇIKIŞ ▼", Color.FromRgb(0xEF, 0x44, 0x44)),
            DekontKind.BankaGiris => ("Banka — Giriş Dekontu", "Banka hesabına para girişi. Cari seçilirse müşteriden TAHSİLAT olarak işlenir (cari alacak).", "BANKA HESABI", "GİRİŞ ▲", Color.FromRgb(0x16, 0xA3, 0x4A)),
            DekontKind.BankaCikis => ("Banka — Çıkış Dekontu", "Banka hesabından para çıkışı. Cari seçilirse tedarikçiye ÖDEME olarak işlenir (cari borç).", "BANKA HESABI", "ÇIKIŞ ▼", Color.FromRgb(0xEF, 0x44, 0x44)),
            DekontKind.KartTahsilat => ("Kredi Kartı — Tahsilat Evrağı", "POS üzerinden müşteriden tahsilat. Cari ZORUNLU (cari alacak oluşur).", "KREDİ KARTI / POS", "TAHSİLAT ▲", Color.FromRgb(0x16, 0xA3, 0x4A)),
            DekontKind.KartOdeme => ("Kredi Kartı — Ödeme Evrağı", "POS üzerinden ödeme/iade. Cari ZORUNLU (cari borç oluşur).", "KREDİ KARTI / POS", "ÖDEME ▼", Color.FromRgb(0xEF, 0x44, 0x44)),
            _ => ("Dekont", "", "HESAP", "DEKONT", Colors.Gray)
        };
        TitleText.Text = title; SubText.Text = sub; AccountLabel.Text = accLabel; KindText.Text = badge;
        KindBadge.Background = new SolidColorBrush(color);

        if (IsCard) CustomerLabel.Text = "CARİ (MÜŞTERİ) *";

        // Hesap listesi (şube + aktif)
        AccountCombo.ItemsSource = IsCash
            ? new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList()
            : IsBank
                ? new BankAccountService().GetAll().Where(x => x.IsActive).Select(x => new ComboItem(x.Id, x.Display)).ToList()
                : new PosService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).Select(x => new ComboItem(x.Id, x.Name)).ToList();
        if (AccountCombo.Items.Count > 0) AccountCombo.SelectedIndex = 0;

        CustomerPicker.Configure(() => new CustomerService().GetAll(), AddCustomer, null);
        DateBox.SelectedDate = DateTime.Today;
        Loaded += (_, _) => AmountBox.Focus();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (AccountCombo.SelectedValue is not int accId) { ShowError("Hesap seçiniz."); return; }
        if (!decimal.TryParse((AmountBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var amount) || amount <= 0)
        { ShowError("Geçerli bir tutar giriniz (0'dan büyük)."); return; }
        int? customerId = CustomerPicker.SelectedValue as int?;
        if (IsCard && (customerId is null or 0)) { ShowError("Kredi kartı tahsilat/ödeme için cari (müşteri) seçiniz."); return; }
        string? note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        try
        {
            SaveButton.IsEnabled = false;
            if (IsCash)
            {
                if (customerId is int cc && cc > 0)
                    new CashMovementService().AddCari(accId, IsIn, amount, cc, note, Session.CurrentUserId);
                else
                    new CashMovementService().AddManual(accId, IsIn ? CashMovementType.Giris : CashMovementType.Cikis, amount, note, Session.CurrentUserId);
            }
            else if (IsBank)
            {
                var type = customerId is int bc && bc > 0
                    ? (IsIn ? BankMovementType.Tahsilat : BankMovementType.Odeme)
                    : (IsIn ? BankMovementType.Giris : BankMovementType.Cikis);
                new BankMovementService().AddManual(accId, type, amount, customerId, note, Session.CurrentUserId);
            }
            else // kart
            {
                new PosMovementService().AddManual(accId, IsIn ? BankMovementType.Tahsilat : BankMovementType.Odeme, amount, customerId, note, Session.CurrentUserId);
            }
            Dialog.Success($"Dekont kaydedildi: {amount:N2} ₺" + (customerId is int ? " · cari hareketi de oluştu." : "."));
            DialogResult = true; Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
