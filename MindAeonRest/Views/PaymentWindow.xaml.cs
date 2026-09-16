using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PaymentWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly CustomerService _customers = new();
    private readonly Order _order;
    private readonly int _branchId;
    private readonly int _cur;

    private readonly ObservableCollection<PayLineVM> _payLines = new();
    private readonly ObservableCollection<PaymentDisplay> _payments = new();
    private PaymentKind _kind = PaymentKind.Nakit;
    private int? _overrideCustomer;
    private readonly int? _kioskId;   // KIOSK bağlamı: yalnız o KIOSK'un kasaları; null=şube kasaları
    private GiftCard? _giftCard;          // Hediye çeki için sorgulanan/çözülen çek
    private LoyaltySettings? _loyalty;    // Sadakat ayarları (program oranları)
    private decimal _loyaltyMaxAmount;    // Seçili müşterinin puanından harcanabilecek azami ₺

    public bool Paid { get; private set; }

    public PaymentWindow(Order order, int? kioskId = null)
    {
        InitializeComponent();
        _order = order;
        _kioskId = kioskId;
        _branchId = order.BranchId;
        _cur = new CurrencyService().GetDefaultId() ?? 0;
        TableText.Text = string.IsNullOrWhiteSpace(order.TableCodes) ? order.Code : order.TableCodes;

        // TÜM üst-satırlar (özet/kişi hesabı için): iptal ve ikram hariç. Ödenebilir = henüz tahsil edilmemiş.
        var allLines = _orders.GetLines(order.Id)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat)
            .ToList();
        _orderTotal = allLines.Sum(l => l.LineTotal);
        _orderPaid = allLines.Where(l => l.SettledClFicheId != null).Sum(l => l.LineTotal);

        foreach (var l in allLines.Where(l => l.SettledClFicheId == null))
        {
            var vm = new PayLineVM(l);
            vm.PropertyChanged += (_, _) => Recalc();
            _payLines.Add(vm);
        }

        // Satırları kişiye (koltuk) göre grupla
        var view = new ListCollectionView(_payLines);
        view.GroupDescriptions.Add(new PropertyGroupDescription("Line.SeatText"));
        LinesList.ItemsSource = view;
        PaymentsList.ItemsSource = _payments;
        CustomerPicker.Configure(() => _customers.GetAll().Where(c => c.Code != "PA"), AddCustomer, null);

        BuildSeatChips();
        BuildPaymentBreakdown(order.Id);

        Loaded += (_, _) => { SelectMethod(PaymentKind.Nakit); Recalc(); };
    }

    private readonly decimal _orderTotal;
    private readonly decimal _orderPaid;
    private readonly List<Button> _seatChips = new();

    // Kişi (koltuk) çipleri: "Tümü" + her kişi; çipte o kişinin ödenecek tutarı yazar
    private void BuildSeatChips()
    {
        SeatChips.Children.Clear();
        _seatChips.Clear();
        AddChip("Tümü", -1);
        foreach (var seat in _payLines.Select(v => v.Line.SeatNo).Distinct().OrderBy(s => s == 0 ? int.MaxValue : s))
        {
            decimal amt = _payLines.Where(v => v.Line.SeatNo == seat).Sum(v => v.Line.LineTotal);
            string label = seat == 0 ? "Ortak" : $"{seat}. Kişi";
            AddChip($"{label}\n₺{amt:N2}", seat);
        }
        HighlightChip(-1);
    }

    private void AddChip(string text, int seat)
    {
        var b = new Button
        {
            Content = text, Tag = seat, Margin = new Thickness(0, 0, 6, 6),
            MinWidth = 84, Height = 46, Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("PosTextBrush"),
            Background = (Brush)FindResource("PosSubtleBrush")
        };
        b.Click += (_, _) => SelectSeat(seat);
        _seatChips.Add(b);
        SeatChips.Children.Add(b);
    }

    private void SelectSeat(int seat)
    {
        foreach (var v in _payLines) v.IsSelected = seat == -1 || v.Line.SeatNo == seat;
        HighlightChip(seat);
        Recalc();
    }

    private void HighlightChip(int seat)
    {
        var active = (Brush)FindResource("PosAccentBrush");
        var idle = (Brush)FindResource("PosSubtleBrush");
        foreach (var c in _seatChips)
        {
            bool on = (int)c.Tag! == seat;
            c.Background = on ? active : idle;
            c.Foreground = on ? Brushes.White : (Brush)FindResource("PosTextBrush");
        }
    }

    // Adisyonda şimdiye dek YAPILAN ödemelerin tip bazında dökümü (nasıl ödendi)
    private void BuildPaymentBreakdown(int orderId)
    {
        var sums = new Dictionary<PaymentKind, decimal>();
        foreach (var f in _orders.GetOrderFiches(orderId))
            foreach (var pl in _orders.GetPaymentLines(f.Id))
                sums[pl.Kind] = (sums.TryGetValue(pl.Kind, out var s) ? s : 0) + pl.Amount;

        BreakdownText.Text = sums.Count == 0
            ? "Yapılan ödeme: yok (ilk ödeme)"
            : "Yapılan ödemeler: " + string.Join("  ·  ", sums.Select(kv => $"{kv.Key.ToTr()} ₺{kv.Value:N2}"));
    }

    // Kapsamlı (KIOSK/şube) araçları döner; hiç yoksa bloklamamak için tümüne düşer
    private static List<ComboItem> Scoped<T>(List<T> all, System.Func<T, bool> scope, System.Func<T, ComboItem> sel)
    {
        var filtered = all.Where(scope).Select(sel).ToList();
        return filtered.Count > 0 ? filtered : all.Select(sel).ToList();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private decimal SelectedTotal => _payLines.Where(v => v.IsSelected).Sum(v => v.Line.LineTotal);
    private decimal PaidSoFar => _payments.Sum(p => p.Amount);
    private decimal Remaining => SelectedTotal - PaidSoFar;

    private void Amount_Changed(object sender, TextChangedEventArgs e) => Recalc();

    private void Recalc()
    {
        SelectedTotalText.Text = $"₺{SelectedTotal:N2}";
        // Tüm adisyon özeti (kişi ödedikçe değişmez; pencere tahsil sonrası kapanır)
        OrderTotalText.Text = $"₺{_orderTotal:N2}";
        OrderPaidText.Text = $"₺{_orderPaid:N2}";
        OrderRemainingText.Text = $"₺{_orderTotal - _orderPaid:N2}";
        // KALAN, o anda yazılan tutarı da düşerek gösterilir; fazla girilirse EKSİ (kırmızı) görünür
        decimal typed = 0;
        if (AmountBox != null) decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out typed);
        decimal rem = Remaining - typed;
        RemainingText.Text = rem >= 0 ? $"₺{rem:N2}" : $"-₺{Math.Abs(rem):N2}";
        RemainingText.Foreground = rem < -0.005m ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35))
            : rem > 0.005m ? (Brush)FindResource("PosTextBrush")
            : (Brush)FindResource("PosGreenBrush");
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool all = _payLines.All(v => v.IsSelected);
        foreach (var v in _payLines) v.IsSelected = !all;
        HighlightChip(!all ? -1 : -2);   // hepsi seçildi→"Tümü" vurgulu; hiçbiri→vurgu yok
    }

    // ===== Yöntem =====
    private void Method_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out var k)) SelectMethod((PaymentKind)k);
    }

    // Tüm yöntem butonları (Tag = (int)PaymentKind) — aktif olan vurgulanır
    private List<Button> MethodButtons => new()
    {
        MNakit, MKart, MBanka, MTemassiz, MQr, MMobil, MOnline,
        MCek, MHavale, MHediye, MPuan, MAcik
    };

    private void SelectMethod(PaymentKind kind)
    {
        _kind = kind;
        var active = (Brush)FindResource("PosAccentBrush");
        var idle = (Brush)FindResource("PosSubtleBrush");
        var aw = Brushes.White; var it = (Brush)FindResource("PosTextBrush");
        foreach (var b in MethodButtons)
        {
            bool on = int.TryParse(b.Tag?.ToString(), out var t) && (PaymentKind)t == kind;
            b.Background = on ? active : idle;
            b.Foreground = on ? aw : it;
        }

        // Müşteri seçimi: Cari (açık hesap) VEYA Puan/Sadakat (zorunlu, PA olamaz)
        bool needsCustomer = kind == PaymentKind.AcikHesap || kind == PaymentKind.PuanSadakat;

        // Yanlış müşteriye yazma engeli: müşteriye-bağlı OLMAYAN yönteme geçildiyse ve eklenmiş
        // ödemeler arasında Cari/Puan satırı yoksa, daha önce seçilmiş müşteri override'ını bırak.
        if (!needsCustomer && !_payments.Any(p => p.Kind == PaymentKind.AcikHesap || p.Kind == PaymentKind.PuanSadakat))
            _overrideCustomer = null;

        bool loyalty = kind == PaymentKind.PuanSadakat;
        bool gift = kind == PaymentKind.HediyeCeki;
        // Araç gerektiren yöntemler: Nakit (kasa), kart tipleri/Online (POS), Yemek Çeki (hesap), Havale/EFT (banka hesabı).
        bool needsInstrument = kind == PaymentKind.Nakit || kind == PaymentKind.YemekCeki || kind.IsCardType() || kind == PaymentKind.HavaleEft;

        CustomerPanel.Visibility = needsCustomer ? Visibility.Visible : Visibility.Collapsed;
        GiftPanel.Visibility = gift ? Visibility.Visible : Visibility.Collapsed;
        InstrumentCombo.Visibility = needsInstrument ? Visibility.Visible : Visibility.Collapsed;
        InstrLabel.Visibility = needsInstrument ? Visibility.Visible : Visibility.Collapsed;
        LoyaltyText.Visibility = loyalty ? Visibility.Visible : Visibility.Collapsed;
        CustomerLabel.Text = loyalty ? "MÜŞTERİ (puan/sadakat için zorunlu, PA olamaz)" : "MÜŞTERİ (açık hesap için zorunlu)";

        // Yöntem değişince hediye çeki/puan bağlamını sıfırla
        _giftCard = null; GiftBalanceText.Text = "";
        _loyaltyMaxAmount = 0;

        CustomerPicker.SelectionChanged -= Loyalty_CustomerChanged;
        if (loyalty)
        {
            CustomerPicker.SelectionChanged += Loyalty_CustomerChanged;
            _loyalty = new LoyaltyService().GetSettings();
            LoyaltyText.Text = _loyalty.IsActive
                ? "Puan/sadakat ödemesi için müşteri seçin (PA olamaz)."
                : "⚠ Sadakat programı aktif değil. Puanla ödeme alınamaz.";
        }

        if (needsInstrument)
        {
            // Kapsam: KIOSK bağlamında SADECE o KIOSK'un kasaları; aksi halde ŞUBE kasaları. (Boşsa şube/aktif tümüne düş.)
            bool Ok(FinanceScope sc, int? kid) => _kioskId is int k ? (sc == FinanceScope.Kiosk && kid == k) : (sc == FinanceScope.Sube);
            List<ComboItem> items;
            if (kind == PaymentKind.Nakit)
                items = Scoped(new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name));
            else if (kind.IsCardType())
                items = Scoped(new PosService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name));
            else if (kind == PaymentKind.HavaleEft)
                items = new BankAccountService().GetAll().Where(x => x.IsActive).Select(x => new ComboItem(x.Id, $"{x.Name} ({x.CurrencyCode})")).ToList();
            else // YemekCeki
                items = Scoped(new MealVoucherAccountService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name));

            InstrumentCombo.ItemsSource = items;
            if (items.Count > 0) InstrumentCombo.SelectedIndex = 0;
        }
        else
        {
            InstrumentCombo.ItemsSource = null;
        }
    }

    /// <summary>Puan/Sadakat müşterisi seçilince bakiye ve azami ₺ karşılığını gösterir.</summary>
    private void Loyalty_CustomerChanged(object? sender, SelectionChangedEventArgs e)
    {
        _loyaltyMaxAmount = 0;
        _loyalty ??= new LoyaltyService().GetSettings();
        if (!_loyalty.IsActive) { LoyaltyText.Text = "⚠ Sadakat programı aktif değil. Puanla ödeme alınamaz."; return; }
        if (CustomerPicker.SelectedValue is not int cid) { LoyaltyText.Text = "Puan/sadakat ödemesi için müşteri seçin (PA olamaz)."; return; }
        try
        {
            decimal points = new LoyaltyService().GetBalance(cid);
            _loyaltyMaxAmount = Math.Round(points * _loyalty.RedeemRate, 2);
            LoyaltyText.Text = $"Puan bakiyesi: {points:N0} puan · ₺ karşılığı: ₺{_loyaltyMaxAmount:N2} (1 puan = ₺{_loyalty.RedeemRate:N2})";
        }
        catch (Exception ex) { LoyaltyText.Text = ex.Message; }
    }

    /// <summary>Hediye çeki kodunu sorgular, bakiye/durum gösterir; ödeme için çözer.</summary>
    private void GiftQuery_Click(object sender, RoutedEventArgs e)
    {
        _giftCard = null;
        string code = (GiftCodeBox.Text ?? "").Trim();
        if (code.Length == 0) { GiftBalanceText.Text = "Hediye çeki kodu girin."; return; }
        try
        {
            var card = new GiftCardService().GetByCode(code);
            if (card is null) { GiftBalanceText.Text = "⚠ Hediye çeki bulunamadı."; return; }
            if (card.Status != GiftCardStatus.Aktif) { GiftBalanceText.Text = $"⚠ Hediye çeki aktif değil ({card.StatusText})."; return; }
            if (card.Balance <= 0) { GiftBalanceText.Text = "⚠ Hediye çeki bakiyesi yok."; return; }
            _giftCard = card;
            GiftBalanceText.Text = $"✓ Bakiye: {card.BalanceText}" + (card.CustomerName is null ? "" : $" · {card.CustomerName}");
        }
        catch (Exception ex) { GiftBalanceText.Text = ex.Message; }
    }

    // ===== Tutar / keypad =====
    private void Key_Click(object sender, RoutedEventArgs e)
    {
        string k = (sender as Button)?.Content?.ToString() ?? "";
        if (k == "C") { AmountBox.Text = "0"; return; }
        string cur = AmountBox.Text == "0" ? "" : AmountBox.Text;
        AmountBox.Text = cur + k;
    }

    private void FillRemaining_Click(object sender, RoutedEventArgs e)
    {
        decimal rem = Remaining;
        AmountBox.Text = (rem > 0 ? rem : 0).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void AddPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        { Dialog.Info("Geçerli bir tutar girin.", "Uyarı"); return; }

        // FAZLA ÖDEME ALINAMAZ: girilen tutar kalandan fazlaysa uyar + kalan kadarına çek
        decimal rem = Remaining;
        if (rem <= 0.005m)
        { Dialog.Warn("Bu hesap zaten tamamen ödendi. Daha fazla ödeme alınamaz.", "Fazla Ödeme"); return; }
        if (amount > rem + 0.005m)
        {
            Dialog.Warn($"Fazla ödeme alınamaz!\n\nKalan tutar: ₺{rem:N2}\nGirdiğiniz: ₺{amount:N2}\n\nTutar kalan kadar (₺{rem:N2}) olarak ayarlandı. (Nakit para üstünü fişe yansıtmadan elden veriniz.)", "Fazla Ödeme");
            amount = rem;
            AmountBox.Text = amount.ToString("0.##", CultureInfo.InvariantCulture);
        }

        int? instrId = null; string instrName = "";
        if (_kind == PaymentKind.AcikHesap)
        {
            if (CustomerPicker.SelectedValue is not int cid) { Dialog.Info("Açık hesap için müşteri seçin (PA olamaz).", "Uyarı"); return; }
            _overrideCustomer = cid;
            instrName = "Cari";
        }
        else if (_kind == PaymentKind.PuanSadakat)
        {
            _loyalty ??= new LoyaltyService().GetSettings();
            if (!_loyalty.IsActive) { Dialog.Warn("Sadakat programı aktif değil. Puanla ödeme alınamaz.", "Puan/Sadakat"); return; }
            if (CustomerPicker.SelectedValue is not int cid) { Dialog.Info("Puan/sadakat için müşteri seçin (PA olamaz).", "Uyarı"); return; }
            if (_loyaltyMaxAmount <= 0) { Dialog.Warn("Bu müşterinin harcanabilir puanı yok.", "Puan/Sadakat"); return; }
            // Çoklu puan satırı bakiyeyi aşmasın: azami ₺'den önceden eklenmiş puan ödemelerinin toplamını düş
            decimal loyaltyRemaining = _loyaltyMaxAmount - _payments.Where(p => p.Kind == PaymentKind.PuanSadakat).Sum(p => p.Amount);
            if (loyaltyRemaining <= 0.005m) { Dialog.Warn("Bu müşterinin harcanabilir puanı kalmadı.", "Puan/Sadakat"); return; }
            if (amount > loyaltyRemaining + 0.005m) { amount = loyaltyRemaining; AmountBox.Text = amount.ToString("0.##", CultureInfo.InvariantCulture); }
            _overrideCustomer = cid;   // PuanSadakat InstrumentId=null; müşteri = fiş müşterisi
            instrName = "Puan";
        }
        else if (_kind == PaymentKind.HediyeCeki)
        {
            if (_giftCard is null) { Dialog.Info("Önce hediye çeki kodunu 'Sorgula' ile doğrulayın.", "Hediye Çeki"); return; }
            if (_giftCard.Status != GiftCardStatus.Aktif || _giftCard.Balance <= 0) { Dialog.Warn("Hediye çeki aktif değil ya da bakiyesi yok.", "Hediye Çeki"); return; }
            // Çoklu hediye çeki satırı bakiyeyi aşmasın: çek bakiyesinden aynı çek (Id) için eklenenleri düş
            decimal giftRemaining = _giftCard.Balance - _payments.Where(p => p.Kind == PaymentKind.HediyeCeki && p.InstrumentId == _giftCard.Id).Sum(p => p.Amount);
            if (giftRemaining <= 0.005m) { Dialog.Warn("Bu hediye çekinin kalan bakiyesi yok.", "Hediye Çeki"); return; }
            if (amount > giftRemaining + 0.005m) { amount = giftRemaining; AmountBox.Text = amount.ToString("0.##", CultureInfo.InvariantCulture); }
            instrId = _giftCard.Id;
            instrName = _giftCard.Code;
        }
        else if (_kind == PaymentKind.Nakit || _kind == PaymentKind.YemekCeki || _kind.IsCardType() || _kind == PaymentKind.HavaleEft)
        {
            // Araç gerektiren yöntemler: kasa / POS / yemek çeki hesabı / banka hesabı (Havale/EFT)
            if (InstrumentCombo.SelectedValue is not int iid) { Dialog.Info("Araç (kasa/POS/banka/hesap) seçin.", "Uyarı"); return; }
            instrId = iid;
            instrName = (InstrumentCombo.SelectedItem as ComboItem)?.Text ?? "";

            // Kart tipleri → ÖKC/POS cihazı (lisans yoksa manuel onay)
            if (_kind.IsCardType())
            {
                var res = PosDeviceFactory.Current.Charge(amount, _cur);
                if (!res.Approved) { Dialog.Warn($"POS onayı alınamadı: {res.Message}", "POS"); return; }
            }
        }

        _payments.Add(new PaymentDisplay { Kind = _kind, InstrumentId = instrId, InstrumentName = instrName, Amount = amount });
        AmountBox.Text = "0";
        Recalc();
    }

    private void RemovePayment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PaymentDisplay p) { _payments.Remove(p); Recalc(); }
    }

    private void PrintAccount_Click(object sender, RoutedEventArgs e)
    {
        try { new HesapFisiWindow(_order) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hesap Fişi"); }
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("Ödeme alma yetkiniz yok (yalnızca Kasiyer/Yönetici)."); return; }
        var selectedIds = _payLines.Where(v => v.IsSelected).Select(v => v.Line.Id).ToList();
        if (selectedIds.Count == 0) { Dialog.Info("Ödenecek satır seçin.", "Uyarı"); return; }
        if (_payments.Count == 0) { Dialog.Info("En az bir ödeme ekleyin.", "Uyarı"); return; }

        // Eksik ödemeyle masa kapatılmasın: ya tam tahsil ya da Açık Hesap (cari) ile kalan işlensin
        decimal selTotal = _payLines.Where(v => v.IsSelected).Sum(v => v.Line.LineTotal);
        decimal paid = _payments.Sum(p => p.Amount);
        bool hasAcik = _payments.Any(p => p.Kind == PaymentKind.AcikHesap);
        if (paid + 0.005m < selTotal && !hasAcik)
        {
            Dialog.Warn($"Seçili tutar ₺{selTotal:N2}, toplanan ₺{paid:N2}. Kalan ₺{selTotal - paid:N2} var.\n\n" +
                        "Masayı kapatmak için ya kalanı tahsil edin ya da kalanı 'Açık Hesap' ile bir müşteriye işleyin.", "Eksik Ödeme");
            return;
        }

        var lines = _payments.Select(p => new PaymentLine { Kind = p.Kind, InstrumentId = p.InstrumentId, CurrencyId = _cur, Amount = p.Amount }).ToList();
        // Override yalnızca gerçekten override gerektiren (Cari/Puan) ödeme kaldıysa geçilir; aksi halde fiş kendi müşterisine yazılır.
        bool needsOverride = _payments.Any(p => p.Kind == PaymentKind.AcikHesap || p.Kind == PaymentKind.PuanSadakat);
        try
        {
            _orders.SettleLines(_order.Id, selectedIds, lines, needsOverride ? _overrideCustomer : null, Session.CurrentUserId);
            Paid = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Back_Click(object sender, RoutedEventArgs e) { DialogResult = Paid; Close(); }
}

public class PayLineVM : INotifyPropertyChanged
{
    public OrderLine Line { get; }
    private bool _selected = true;
    public bool IsSelected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    public PayLineVM(OrderLine line) => Line = line;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class PaymentDisplay
{
    public PaymentKind Kind { get; set; }
    public int? InstrumentId { get; set; }
    public string InstrumentName { get; set; } = "";
    public decimal Amount { get; set; }
    public string KindText => Kind.ToTr();
    public string AmountText => $"₺{Amount:N2}";
}
