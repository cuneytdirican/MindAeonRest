using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Hediye çeki yönetimi: liste (kod/bakiye/durum/müşteri), yeni çek satışı (yükleme), iptal, seçili çekin ekstresi.</summary>
public partial class GiftCardWindow : Window
{
    private readonly GiftCardService _service = new();

    public GiftCardWindow()
    {
        InitializeComponent();
        CustomerPicker.Configure(() => new CustomerService().GetAll().Where(c => c.Code != "PA"), AddCustomer, null);

        // Satış ödeme yöntemi: Nakit (kasa) / Kart (POS)
        MethodCombo.ItemsSource = new List<ComboItem> { new(0, "Nakit"), new(1, "Kart") };
        MethodCombo.SelectedValue = 0;
        LoadInstruments();

        Loaded += (_, _) => Load();
    }

    /// <summary>Aktif şubenin kasa ve POS cihazlarını yükler (satış bedeli tahsilatı için).</summary>
    private void LoadInstruments()
    {
        int branch = Session.CurrentBranch?.Id ?? 0;
        var cashes = new CashRegisterService().GetAll()
            .Where(k => k.IsActive && k.BranchId == branch)
            .Select(k => new ComboItem(k.Id, $"{k.Code} · {k.Name}")).ToList();
        CashCombo.ItemsSource = cashes;
        CashCombo.SelectedValue = cashes.FirstOrDefault()?.Value;

        var poses = new PosService().GetAll()
            .Where(p => p.IsActive && p.BranchId == branch)
            .Select(p => new ComboItem(p.Id, $"{p.Code} · {p.Name}")).ToList();
        PosCombo.ItemsSource = poses;
        PosCombo.SelectedValue = poses.FirstOrDefault()?.Value;
    }

    private void Method_Changed(object sender, SelectionChangedEventArgs e)
    {
        bool cash = (MethodCombo.SelectedValue as int?) == 0;
        if (CashLabel == null) return;   // ilk InitializeComponent sırası
        CashLabel.Visibility = CashCombo.Visibility = cash ? Visibility.Visible : Visibility.Collapsed;
        PosLabel.Visibility = PosCombo.Visibility = cash ? Visibility.Collapsed : Visibility.Visible;
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Load()
    {
        try
        {
            var list = _service.GetAll(ShowInactive.IsChecked == true);
            Grid.ItemsSource = list;
            MovGrid.ItemsSource = null;
            RefreshLiability();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hediye Çekleri"); }
    }

    private void RefreshLiability()
    {
        try { LiabilityText.Text = $"₺{_service.GetTotalLiability():N2}"; }
        catch { LiabilityText.Text = "₺0,00"; }
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not GiftCard g) { MovGrid.ItemsSource = null; return; }
        try { MovGrid.ItemsSource = _service.GetMovements(g.Id); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Ekstre"); }
    }

    private void Issue_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (!decimal.TryParse((AmountBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var amount) || amount <= 0)
        { ShowError("Geçerli bir tutar girin (0'dan büyük)."); return; }

        int? customerId = CustomerPicker.SelectedValue;
        string? code = string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim();
        string? note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        DateTime? expires = ExpiresBox.SelectedDate?.Date.AddDays(1).AddSeconds(-1);   // gün sonu

        // Satış ödemesi: Nakit→kasa, Kart→POS (servis fail-closed, ikisi de zorunlu)
        bool cash = (MethodCombo.SelectedValue as int?) == 0;
        PaymentKind saleKind = cash ? PaymentKind.Nakit : PaymentKind.KrediKarti;
        int? instrument = cash ? CashCombo.SelectedValue as int? : PosCombo.SelectedValue as int?;
        if (instrument is null) { ShowError(cash ? "Satış için bir kasa seçin (aktif şube kasası tanımlı değilse Kasalar'dan ekleyin)." : "Satış için bir POS cihazı seçin (aktif şube POS'u tanımlı değilse POS'lardan ekleyin)."); return; }

        try
        {
            IssueButton.IsEnabled = false;
            var card = _service.Issue(amount, null, customerId, expires, code, note, saleKind, instrument, Session.CurrentUserId);
            Dialog.Success($"Hediye çeki oluşturuldu.\n\nKod: {card.Code}\nBakiye: {card.BalanceText}\n\nÇek bedeli {(cash ? "kasaya" : "POS'a")} tahsilat olarak yazıldı.", "Hediye Çeki");
            AmountBox.Text = ""; CodeBox.Text = ""; NoteBox.Text = ""; ExpiresBox.SelectedDate = null;
            CustomerPicker.SelectedValue = null;
            Load();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { IssueButton.IsEnabled = true; }
    }

    private void CancelCard_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not GiftCard g) { Dialog.Info("İptal edilecek çeki listeden seçin.", "Hediye Çeki"); return; }
        if (g.Status != GiftCardStatus.Aktif) { Dialog.Warn($"Bu çek zaten {g.StatusText.ToLowerInvariant()}.", "Hediye Çeki"); return; }
        if (!Dialog.Confirm($"'{g.Code}' kodlu hediye çeki iptal edilecek (kalan bakiye {g.BalanceText} silinir). Devam edilsin mi?", "Hediye Çeki İptal")) return;
        try
        {
            _service.Cancel(g.Id, Session.CurrentUserId);
            Dialog.Success("Hediye çeki iptal edildi.", "Hediye Çeki");
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hediye Çeki"); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
