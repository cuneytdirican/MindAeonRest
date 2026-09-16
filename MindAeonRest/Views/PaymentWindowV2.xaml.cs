using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>V2 Kasiyer Ödeme istasyonu: sol = ödeme bekleyen masalar (aramasız), orta = adisyon, sağ = ödeme merkezi (12 yöntem + bölme).
/// Tema-bağımsız (DynamicResource) + ekran içi tema değiştirme.</summary>
public partial class PaymentWindowV2 : Window
{
    private readonly OrderService _orders = new();
    private readonly CustomerService _customers = new();
    private readonly int _branchId;
    private readonly int _cur;
    private readonly int? _kioskId;
    private System.Windows.Threading.DispatcherTimer? _clock;

    private readonly ObservableCollection<PaymentDisplay> _payments = new();
    private int? _selectedOrderId;
    private Order? _order;
    private Order? _lastSettledOrder;   // ödeme tamamlandıktan sonra fiş için tutulan referans
    private ClFiche? _lastFiche;        // SettleLines'tan dönen son fiş
    private ReceiptData? _lastReceiptSnapshot;  // settle-ÖNCESİ yakalanan fiş verisi (ürünler + ödeme dökümü)
    private decimal _orderTotal, _orderPaid;
    private int _coverCount = 1;
    private string _entered = "0";
    private PaymentKind _kind = PaymentKind.Nakit;
    private int? _overrideCustomer;
    private string _filter = "all";
    private GiftCard? _giftCard;          // Hediye çeki için sorgulanan/çözülen çek
    private LoyaltySettings? _loyalty;    // Sadakat ayarları (program oranları)
    private decimal _loyaltyMaxAmount;    // Seçili müşterinin puanından harcanabilecek azami ₺

    public bool Paid { get; private set; }

    public PaymentWindowV2(Order? order = null, int? kioskId = null)
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? order?.BranchId ?? 0;
        _kioskId = kioskId;
        _cur = new CurrencyService().GetDefaultId() ?? 0;
        ShopText.Text = $"{Session.CurrentCompany?.Name} · {Session.CurrentBranch?.Name}";
        CashierText.Text = $"Kasa {Session.CurrentKioskNo} · {Session.CurrentUser?.FullName}";
        AddedHost.ItemsSource = _payments;

        BuildSteps(2);
        BuildMethods();
        BuildNumpad();

        Loaded += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            _clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
            _clock.Start();
            LoadUnpaid();
            if (order is not null) SelectOrder(order.Id);
            SelectMethod(PaymentKind.Nakit);
            UpdateActionButtons();   // başlangıçta (masa seçili değilse) butonları doğru duruma getir
        };
        Closed += (_, _) => _clock?.Stop();
    }

    private Brush Res(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    // ===== Üst adım şeridi =====
    private void BuildSteps(int active)
    {
        string[] steps = { "Masa Seç", "Adisyon Kontrol", "Ödeme Al", "Fiş / Fatura", "Kapat" };
        StepsHost.Children.Clear();
        for (int i = 0; i < steps.Length; i++)
        {
            bool on = (i + 1) <= active;
            var b = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(3, 0, 3, 0),
                Background = on ? Res("AccentBrush") : Res("SurfaceAltBrush")
            };
            b.Child = new TextBlock
            {
                Text = $"{i + 1}  {steps[i]}", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = on ? Brushes.White : Res("TextMutedBrush")
            };
            StepsHost.Children.Add(b);
        }
    }

    // ===== Ödeme bekleyen masalar =====
    private void LoadUnpaid()
    {
        try
        {
            decimal openTotal = 0;
            var rows = _orders.GetUnpaidTables(_branchId);
            string q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();

            // Filtre tanımları:
            //  "all"  = tüm açık masalar (Status=0 olan her şey)
            //  "open" = hiç ödeme alınmamış (Paid<=0) ve hesap istenmemiş — saf açık masa
            //  "pay"  = ödeme bekleyen: hesap istendi (HasBillRequest) VEYA kısmi ödeme var (IsPartial / Remaining>0 ama Paid>0)
            bool WaitingPayment(UnpaidTable r) => r.HasBillRequest || r.IsPartial;
            bool PureOpen(UnpaidTable r) => r.Paid <= 0.005m && !r.HasBillRequest;

            var filtered = rows.Where(r =>
            {
                openTotal += r.Remaining;   // ciro: tüm açık masaların kalanı (filtreden bağımsız)
                if (_filter == "open" && !PureOpen(r)) return false;
                if (_filter == "pay" && !WaitingPayment(r)) return false;
                if (q.Length > 0 && !($"{r.TableCodes} {r.OrderCode} {r.WaiterName} {r.ZoneName} {r.DeliveryName} {r.ChannelText}".ToLowerInvariant().Contains(q))) return false;
                return true;
            }).Select(r => new UnpaidVM(r, () => SelectOrder(r.OrderId), Res)).ToList();

            UnpaidHost.ItemsSource = filtered;
            UnpaidCount.Text = $"{filtered.Count}/{rows.Count} sipariş · {rows.Count(WaitingPayment)} ödeme bekliyor";
            CiroText.Text = $"₺{openTotal:N2}";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Ödeme"); }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => LoadUnpaid();

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = (sender as FrameworkElement)?.Tag?.ToString() ?? "all";
        foreach (var (btn, tag) in new[] { (FltAll, "all"), (FltOpen, "open"), (FltPay, "pay") })
        {
            bool on = tag == _filter;
            btn.Background = on ? Res("AccentBrush") : Res("SurfaceAltBrush");
            btn.Foreground = on ? Brushes.White : Res("TextMutedBrush");
        }
        LoadUnpaid();
    }

    // ===== Adisyon (orta) =====
    private void SelectOrder(int orderId)
    {
        _order = _orders.GetById(orderId);
        if (_order is null) return;
        _selectedOrderId = orderId;
        _coverCount = Math.Max(1, _order.CoverCount);
        // Masaya bağlı → "Masa X"; masasız (paket/gel-al/self-servis/kiosk) → teslim adı ?? adisyon kodu
        bool nonTable = _order.OrderType != OrderType.MasaServis || string.IsNullOrWhiteSpace(_order.TableCodes);
        string head = !string.IsNullOrWhiteSpace(_order.TableCodes) ? $"Masa {_order.TableCodes}"
            : !string.IsNullOrWhiteSpace(_order.DeliveryName) ? _order.DeliveryName!
            : _order.Code;
        OrderTitle.Text = head
            + (_order.IsVip ? "  ⭐VIP" : "") + (_order.Priority ? "  ⏫ÖNCELİK" : "");
        // Masasız siparişte kanal etiketini (Paket / Gel-Al / Kiosk) öne al ki kasiyer ne ödediğini bilsin
        string channel = _order.OrderType switch { OrderType.Paket => "📦 Paket", OrderType.TakeAway => "🛍 Gel-Al / Self-Servis", OrderType.Kiosk => "🖥 Kiosk", _ => "" };
        OrderSub.Text = (nonTable && channel.Length > 0 ? $"{channel} · " : "")
            + $"{_order.ZoneName} · {_coverCount} kişi · {_order.CustomerName}";
        OrderOpened.Text = $"Açılış {_order.OpenedAt:HH:mm}";
        AllergyBanner.Visibility = string.IsNullOrWhiteSpace(_order.AllergyNote) ? Visibility.Collapsed : Visibility.Visible;
        AllergyText.Text = $"⚠ {_order.AllergyNote}";

        var lines = _orders.GetLines(orderId)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat).ToList();
        LineHost.ItemsSource = lines.Select(l => new PayLineRow(l, Res)).ToList();
        int adet = (int)lines.Sum(l => l.Qty);
        LinesHeader.Text = $"ADİSYON · {lines.Count} KALEM";
        LinesCount.Text = $"{adet} adet ürün";

        _orderTotal = lines.Sum(l => l.LineTotal);
        _orderPaid = lines.Where(l => l.SettledClFicheId != null).Sum(l => l.LineTotal);
        _payments.Clear();
        _entered = "0";
        _overrideCustomer = null;
        _lastSettledOrder = null;   // yeni masa seçildi, önceki ödeme/fiş referansını bırak
        _lastFiche = null;
        _lastReceiptSnapshot = null;
        Recalc();
        UpdateActionButtons();
        BuildSteps(3);
    }

    /// <summary>Aksiyon butonlarını (Ödemeyi Al / Fiş / Beklet / İptal) duruma göre etkin/pasif yapar.</summary>
    private void UpdateActionButtons()
    {
        bool hasOpenOrder = _order is not null;                 // üzerinde çalışılan açık masa var mı
        bool justPaid = _order is null && _lastSettledOrder is not null;  // ödeme alındı, masa kapandı

        // Ödemeyi Al: yalnızca açık masa varken
        CompleteBtn.IsEnabled = hasOpenOrder;
        // Fiş: açık masa varken (önizleme) veya ödeme tamamlanıp masa kapandıktan sonra
        ReceiptBtn.IsEnabled = hasOpenOrder || justPaid;
        // Beklet / İptal: yalnızca üzerinde işlem yapılabilecek açık masa varken anlamlı
        HoldBtn.IsEnabled = hasOpenOrder;
        CancelBtn.IsEnabled = hasOpenOrder;

        HoldBtn.Opacity = HoldBtn.IsEnabled ? 1 : 0.45;
        CancelBtn.Opacity = CancelBtn.IsEnabled ? 1 : 0.45;
        CompleteBtn.Opacity = CompleteBtn.IsEnabled ? 1 : 0.45;
        ReceiptBtn.Opacity = ReceiptBtn.IsEnabled ? 1 : 0.45;
    }

    private decimal Remaining => _orderTotal - _orderPaid - _payments.Sum(p => p.Amount);

    private void Recalc()
    {
        RemainingBig.Text = $"₺{Math.Max(0, Remaining):N2}";
        TotalSmall.Text = $"Toplam ₺{_orderTotal:N2}";
        PaidSmall.Text = $"Ödenen ₺{_orderPaid + _payments.Sum(p => p.Amount):N2}";
        AmountText.Text = $"₺{_entered}";
        UpdateActionButtons();
    }

    // ===== Ödeme yöntemleri =====
    private void BuildMethods()
    {
        var defs = new (PaymentKind Kind, string Label)[]
        {
            (PaymentKind.Nakit, "💵 Nakit"), (PaymentKind.KrediKarti, "💳 Kredi Kartı"), (PaymentKind.BankaKarti, "🏦 Banka Kartı"),
            (PaymentKind.Temassiz, "📶 Temassız"), (PaymentKind.QrOdeme, "🔳 QR Ödeme"), (PaymentKind.MobilOdeme, "📱 Mobil Ödeme"),
            (PaymentKind.HavaleEft, "🏛 Havale/EFT"), (PaymentKind.AcikHesap, "🧾 Cari Hesap"), (PaymentKind.YemekCeki, "🍽 Yemek Kartı"),
            (PaymentKind.OnlineOdeme, "🌐 Online"), (PaymentKind.HediyeCeki, "🎁 Hediye Çeki"), (PaymentKind.PuanSadakat, "⭐ Puan/Sadakat"),
        };
        MethodHost.ItemsSource = defs.Select(d => new PayMethodVM(d.Kind, d.Label, Res)).ToList();
    }

    private void Method_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PayMethodVM vm) SelectMethod(vm.Kind);
    }

    private void SelectMethod(PaymentKind kind)
    {
        _kind = kind;
        foreach (var m in (MethodHost.ItemsSource as IEnumerable<PayMethodVM>) ?? Enumerable.Empty<PayMethodVM>())
            m.SetActive(m.Kind == kind);
        MethodHost.Items.Refresh();

        // Müşteri seçimi: Cari (açık hesap) VEYA Puan/Sadakat (zorunlu, PA olamaz)
        bool needsCustomer = kind == PaymentKind.AcikHesap || kind == PaymentKind.PuanSadakat;

        // Yanlış müşteriye yazma engeli: müşteriye-bağlı OLMAYAN yönteme geçildiyse ve eklenmiş
        // ödemeler arasında Cari/Puan satırı yoksa, daha önce seçilmiş müşteri override'ını bırak.
        if (!needsCustomer && !_payments.Any(p => p.Kind == PaymentKind.AcikHesap || p.Kind == PaymentKind.PuanSadakat))
            _overrideCustomer = null;
        // Enstrüman/ComboBox: Nakit→kasa, kart-türevi (Online dahil)→POS, Yemek→yemek hesabı, Havale/EFT→banka hesabı
        bool needsInstrument = kind == PaymentKind.Nakit || kind.IsCardType() || kind == PaymentKind.YemekCeki || kind == PaymentKind.HavaleEft;
        bool gift = kind == PaymentKind.HediyeCeki;
        bool loyalty = kind == PaymentKind.PuanSadakat;

        CustomerPicker.Visibility = needsCustomer ? Visibility.Visible : Visibility.Collapsed;
        InstrumentCombo.Visibility = needsInstrument ? Visibility.Visible : Visibility.Collapsed;
        GiftPanel.Visibility = gift ? Visibility.Visible : Visibility.Collapsed;
        LoyaltyText.Visibility = loyalty ? Visibility.Visible : Visibility.Collapsed;

        // Yöntem değişince hediye çeki/puan bağlamını sıfırla
        _giftCard = null; GiftBalanceText.Text = "";
        _loyaltyMaxAmount = 0;

        CustomerPicker.SelectionChanged -= Loyalty_CustomerChanged;
        if (needsCustomer)
        {
            CustomerPicker.Configure(() => _customers.GetAll().Where(c => c.Code != "PA"), AddCustomer, null);
            if (loyalty)
            {
                CustomerPicker.SelectionChanged += Loyalty_CustomerChanged;
                _loyalty = new LoyaltyService().GetSettings();
                if (!_loyalty.IsActive)
                    LoyaltyText.Text = "⚠ Sadakat programı aktif değil. Puanla ödeme alınamaz.";
                else
                    LoyaltyText.Text = "Puan/sadakat ödemesi için müşteri seçin (PA olamaz).";
            }
        }
        else if (needsInstrument)
        {
            bool Ok(FinanceScope sc, int? kid) => _kioskId is int k ? (sc == FinanceScope.Kiosk && kid == k) : (sc == FinanceScope.Sube);
            List<ComboItem> items = kind switch
            {
                PaymentKind.Nakit => Scoped(new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name)),
                PaymentKind.YemekCeki => Scoped(new MealVoucherAccountService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name)),
                // Havale/EFT → banka hesabı (firma geneli, şube filtresi yok)
                PaymentKind.HavaleEft => new BankAccountService().GetAll().Where(x => x.IsActive).Select(x => new ComboItem(x.Id, $"{x.Name} ({x.CurrencyCode})")).ToList(),
                _ => Scoped(new PosService().GetAll().Where(x => x.IsActive && x.BranchId == _branchId).ToList(), x => Ok(x.Scope, x.KioskId), x => new ComboItem(x.Id, x.Name)),
            };
            InstrumentCombo.ItemsSource = items;
            if (items.Count > 0) InstrumentCombo.SelectedIndex = 0;
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

    private static List<ComboItem> Scoped<T>(List<T> all, Func<T, bool> scope, Func<T, ComboItem> sel)
    {
        var f = all.Where(scope).Select(sel).ToList();
        return f.Count > 0 ? f : all.Select(sel).ToList();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    // ===== Tutar / numpad / bölme =====
    private void BuildNumpad()
    {
        foreach (var key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "C" })
        {
            var b = new Button
            {
                Content = key, Margin = new Thickness(3), Height = 46, Cursor = Cursors.Hand, FontSize = 17, FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0), Background = Res("SurfaceAltBrush"), Foreground = Res("TextBrush"), Tag = key
            };
            b.Click += Key_Click;
            Numpad.Children.Add(b);
        }
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        string k = (sender as Button)?.Tag?.ToString() ?? "";
        if (k == "C") _entered = "0";
        else if (k == ".") { if (!_entered.Contains('.')) _entered += "."; }
        else _entered = _entered == "0" ? k : _entered + k;
        AmountText.Text = $"₺{_entered}";
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        string tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "all";
        decimal rem = Remaining;
        decimal amt = tag switch
        {
            "all" => rem,
            "cover" => _coverCount > 0 ? Math.Round(rem / _coverCount, 2) : rem,
            _ => int.TryParse(tag, out var n) && n > 0 ? Math.Round(rem / n, 2) : rem
        };
        _entered = (amt > 0 ? amt : 0).ToString("0.##", CultureInfo.InvariantCulture);
        AmountText.Text = $"₺{_entered}";
    }

    private void AddPayment_Internal()
    {
        if (!decimal.TryParse(_entered.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0) return;
        decimal rem = Remaining;
        if (rem <= 0.005m) { Dialog.Warn("Bu hesap zaten ödendi.", "Ödeme"); return; }
        if (amount > rem + 0.005m) { amount = rem; }

        int? instrId = null;
        if (_kind == PaymentKind.AcikHesap)
        {
            if (CustomerPicker.SelectedValue is not int cid) { Dialog.Info("Cari hesap için müşteri seçin (PA olamaz).", "Ödeme"); return; }
            _overrideCustomer = cid;
        }
        else if (_kind == PaymentKind.PuanSadakat)
        {
            _loyalty ??= new LoyaltyService().GetSettings();
            if (!_loyalty.IsActive) { Dialog.Warn("Sadakat programı aktif değil. Puanla ödeme alınamaz.", "Puan/Sadakat"); return; }
            if (CustomerPicker.SelectedValue is not int cid) { Dialog.Info("Puan/sadakat için müşteri seçin (PA olamaz).", "Ödeme"); return; }
            if (_loyaltyMaxAmount <= 0) { Dialog.Warn("Bu müşterinin harcanabilir puanı yok.", "Puan/Sadakat"); return; }
            // Çoklu puan satırı bakiyeyi aşmasın: azami ₺'den önceden eklenmiş puan ödemelerinin toplamını düş
            decimal loyaltyRemaining = _loyaltyMaxAmount - _payments.Where(p => p.Kind == PaymentKind.PuanSadakat).Sum(p => p.Amount);
            if (loyaltyRemaining <= 0.005m) { Dialog.Warn("Bu müşterinin harcanabilir puanı kalmadı.", "Puan/Sadakat"); return; }
            if (amount > loyaltyRemaining + 0.005m) { amount = loyaltyRemaining; }
            _overrideCustomer = cid;   // PuanSadakat InstrumentId=null; müşteri = fiş müşterisi
        }
        else if (_kind == PaymentKind.HediyeCeki)
        {
            if (_giftCard is null) { Dialog.Info("Önce hediye çeki kodunu 'Sorgula' ile doğrulayın.", "Hediye Çeki"); return; }
            if (_giftCard.Status != GiftCardStatus.Aktif || _giftCard.Balance <= 0) { Dialog.Warn("Hediye çeki aktif değil ya da bakiyesi yok.", "Hediye Çeki"); return; }
            // Çoklu hediye çeki satırı bakiyeyi aşmasın: çek bakiyesinden aynı çek (Id) için eklenenleri düş
            decimal giftRemaining = _giftCard.Balance - _payments.Where(p => p.Kind == PaymentKind.HediyeCeki && p.InstrumentId == _giftCard.Id).Sum(p => p.Amount);
            if (giftRemaining <= 0.005m) { Dialog.Warn("Bu hediye çekinin kalan bakiyesi yok.", "Hediye Çeki"); return; }
            if (amount > giftRemaining + 0.005m) { amount = giftRemaining; }
            instrId = _giftCard.Id;
        }
        else if (_kind == PaymentKind.Nakit || _kind.IsCardType() || _kind == PaymentKind.YemekCeki || _kind == PaymentKind.HavaleEft)
        {
            if (InstrumentCombo.SelectedValue is not int iid) { Dialog.Info("Araç (kasa/POS/banka/hesap) seçin.", "Ödeme"); return; }
            instrId = iid;
            if (_kind.IsCardType())
            {
                var res = PosDeviceFactory.Current.Charge(amount, _cur);
                if (!res.Approved) { Dialog.Warn($"POS onayı alınamadı: {res.Message}", "POS"); return; }
            }
        }
        _payments.Add(new PaymentDisplay { Kind = _kind, InstrumentId = instrId, Amount = amount });
        _entered = "0";
        Recalc();
    }

    /// <summary>"Ödeme Ekle": girilen tutarı seçili yöntemle _payments'a ekler (karma/kısmi ödeme).</summary>
    private void AddPayment_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { Dialog.Info("Önce masa seçin.", "Ödeme"); return; }
        AddPayment_Internal();
    }

    // ===== Aksiyonlar =====
    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { Dialog.Info("Önce masa seçin.", "Ödeme"); return; }
        if (!Permissions.Payment) { Dialog.Warn("Ödeme alma yetkiniz yok."); return; }
        // Girilen tutar varsa önce onu ekle
        if (decimal.TryParse(_entered.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var pend) && pend > 0)
            AddPayment_Internal();
        if (_payments.Count == 0) { Dialog.Info("En az bir ödeme ekleyin.", "Ödeme"); return; }

        var openIds = _orders.GetLines(_order.Id)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat && l.SettledClFicheId == null)
            .Select(l => l.Id).ToList();
        decimal collected = _payments.Sum(p => p.Amount);
        bool hasCari = _payments.Any(p => p.Kind == PaymentKind.AcikHesap);
        decimal remaining = _orderTotal - _orderPaid;
        if (collected + 0.005m < remaining && !hasCari)
        {
            Dialog.Warn($"Kalan ₺{remaining:N2}, toplanan ₺{collected:N2}.\nKalanı tahsil edin ya da 'Cari Hesap' ile işleyin.", "Eksik Ödeme");
            return;
        }
        var lines = _payments.Select(p => new PaymentLine { Kind = p.Kind, InstrumentId = p.InstrumentId, CurrencyId = _cur, Amount = p.Amount }).ToList();
        try
        {
            var settledOrder = _order;   // ödeme sonrası fiş için referansı koru

            // Fişi settle-ÖNCESİ yakala: tüm açık satırlar henüz SettledClFicheId==null olduğundan tam dolu gelir.
            // Böylece fiş, satırların settle-sonrası durumundan ve includeSettled bayrağından tamamen bağımsız olur.
            var receiptSnapshot = new ReceiptService().BuildAccountReceipt(_order, includeSettled: false);
            foreach (var p in _payments)
                receiptSnapshot.Payments.Add(new ReceiptPayment { Label = p.Kind.ToTr(), Amount = p.Amount });
            receiptSnapshot.Paid = collected;
            receiptSnapshot.Change = Math.Max(0, collected - remaining);

            var fiche = _orders.SettleLines(_order.Id, openIds, lines, _overrideCustomer, Session.CurrentUserId);
            Paid = true;
            // Fiş için ödeme sonrası referansları sakla (masa kapansa bile "Fiş" basılabilsin)
            _lastSettledOrder = settledOrder;
            _lastReceiptSnapshot = receiptSnapshot;
            _lastFiche = fiche ?? _orders.GetOrderFiches(settledOrder!.Id).LastOrDefault();
            _order = null; _selectedOrderId = null;
            LineHost.ItemsSource = null; OrderTitle.Text = "Masa seçin"; OrderSub.Text = ""; OrderOpened.Text = "";
            AllergyBanner.Visibility = Visibility.Collapsed; LinesHeader.Text = "ADİSYON"; LinesCount.Text = "";
            _payments.Clear(); _orderTotal = 0; _orderPaid = 0; _entered = "0"; Recalc();
            LoadUnpaid();
            BuildSteps(4);   // "Fiş / Fatura" adımı: artık fiş basılabilir
            UpdateActionButtons();

            string ficheInfo = _lastFiche is not null ? $" (Fiş No: {_lastFiche.Code})" : "";
            if (Dialog.Confirm($"Ödeme alındı{ficheInfo}. Fiş yazdırmak ister misiniz?", "Ödeme"))
                PrintReceiptFor(_lastSettledOrder!, _lastReceiptSnapshot!);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Ödeme"); }
    }

    private void Receipt_Click(object sender, RoutedEventArgs e)
    {
        // Ödeme öncesi: seçili açık masanın önizlemesi (borç) — canlı okuma (masa açık, doğru).
        if (_order is not null) { PrintReceiptFor(_order, includeSettled: false); return; }
        // Ödeme sonrası: settle-öncesi yakalanan snapshot'tan bas (ürünler + ödeme dökümü garanti).
        if (_lastSettledOrder is not null && _lastReceiptSnapshot is not null) { PrintReceiptFor(_lastSettledOrder, _lastReceiptSnapshot); return; }
        Dialog.Info("Önce masa seçin ya da ödeme alın.", "Fiş");
    }

    /// <summary>Verilen sipariş için hesap fişi penceresini (önizleme + yazdırma) açar.
    /// includeSettled=true ödeme tamamlandıktan sonra ödenmiş satırları da fişe alır.</summary>
    private void PrintReceiptFor(Order order, bool includeSettled)
    {
        try
        {
            // Ödeme sonrası en güncel sipariş kaydını çek (Status/CustomerId değişmiş olabilir)
            var fresh = _orders.GetById(order.Id) ?? order;
            new HesapFisiWindow(fresh, includeSettled) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Fiş"); }
    }

    /// <summary>Ödeme-sonrası fiş: settle-öncesi yakalanan hazır <see cref="ReceiptData"/> ile açar.
    /// Canlı yeniden okuma yapılmaz → ürünler + ödeme dökümü her zaman tam basılır.
    /// <paramref name="order"/> yalnız yazıcı seçimi için verilir.</summary>
    private void PrintReceiptFor(Order order, ReceiptData snapshot)
    {
        try
        {
            new HesapFisiWindow(order, snapshot) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Fiş"); }
    }

    // "Beklet": girilen/eklenen ödemeleri temizler, masayı AÇIK bırakır (DB'ye dokunmaz) ve
    // kasiyeri masa listesine döndürür. Henüz tahsil edilmiş hiçbir şey yoktur — sadece taslak temizlenir.
    private void Hold_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { Dialog.Info("Bekletilecek açık masa yok.", "Beklet"); return; }
        if (_payments.Count > 0 || _entered != "0")
        {
            if (!Dialog.Confirm("Girilen ödemeler temizlenip masa açık bırakılacak. Devam edilsin mi?", "Beklet")) return;
        }
        string tbl = string.IsNullOrWhiteSpace(_order.TableCodes) ? _order.Code : $"Masa {_order.TableCodes}";
        _payments.Clear(); _entered = "0"; _overrideCustomer = null;
        // masayı bırak, listeye dön
        _order = null; _selectedOrderId = null;
        LineHost.ItemsSource = null; OrderTitle.Text = "Masa seçin"; OrderSub.Text = ""; OrderOpened.Text = "";
        AllergyBanner.Visibility = Visibility.Collapsed; LinesHeader.Text = "ADİSYON"; LinesCount.Text = "";
        _orderTotal = 0; _orderPaid = 0;
        Recalc(); LoadUnpaid(); BuildSteps(2);
        Dialog.Info($"{tbl} açık bırakıldı, ödeme taslağı temizlendi.", "Beklet");
    }

    // "İptal": bu ödeme oturumunu iptal eder — eklenmiş (henüz tahsil edilmemiş) ödeme taslağını
    // temizler ve seçimi kaldırır. Tahsil tamamlanmadıysa DB'ye dokunmaz; tamamlandıysa zaten pasiftir.
    private void ClearPay_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { Dialog.Info("İptal edilecek açık ödeme yok.", "İptal"); return; }
        if (_payments.Count > 0 || _entered != "0")
        {
            if (!Dialog.Confirm("Bu ödeme işlemi iptal edilecek; eklenen ödemeler silinecek (masa açık kalır). Devam edilsin mi?", "İptal"))
                return;
        }
        _payments.Clear(); _entered = "0"; _overrideCustomer = null;
        Recalc();
        Dialog.Info("Ödeme işlemi iptal edildi. Masa açık ve seçili kaldı.", "İptal");
    }
    private void RemovePayment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PaymentDisplay p) { _payments.Remove(p); Recalc(); }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var opts = ThemeManager.Options;
        int idx = opts.FindIndex(o => o.Value == ThemeManager.Current);
        ThemeManager.ChangeAndSave(opts[(idx + 1) % opts.Count].Value);
        BuildSteps(_order is null ? 2 : 3);
        BuildMethods(); SelectMethod(_kind);
        Filter_Click(_filter == "open" ? FltOpen : _filter == "pay" ? FltPay : FltAll, new RoutedEventArgs());
        foreach (Button b in Numpad.Children) { b.Background = Res("SurfaceAltBrush"); b.Foreground = Res("TextBrush"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = Paid; Close(); }
}

// ===== VM'ler =====
public class UnpaidVM
{
    public int OrderId { get; }
    public string Title { get; }
    public string Sub { get; }
    public string TotalText { get; }
    public string StatusText { get; }
    public string StatusBg { get; }
    public string RemainingInfo { get; }
    public string RowBg { get; }
    public string BorderCol { get; }
    public ICommand SelectCmd { get; }
    // Masasız (paket/gel-al/self-servis/kiosk) siparişleri kasiyerin ayırt edip ödeyebilmesi için kanal rozeti
    public bool ShowChannel { get; }
    public string ChannelText { get; }

    public UnpaidVM(UnpaidTable r, Action select, Func<string, Brush> res)
    {
        OrderId = r.OrderId;
        // Masaya bağlıysa "Masa X"; değilse teslim adı/adisyon kodu (DisplayLabel).
        Title = r.IsNonTable ? r.DisplayLabel : $"Masa {r.TableCodes}";
        ShowChannel = r.IsNonTable;
        ChannelText = r.ChannelText;
        Sub = $"{r.ZoneName} · {r.WaiterName} · 👤{r.CoverCount} · {r.ElapsedText}";
        TotalText = r.TotalText;
        StatusText = r.StatusText;
        StatusBg = r.IsPartial ? "#E9A23B" : (r.HasBillRequest ? "#EF4444" : "#10B981");
        RemainingInfo = r.IsPartial ? $"₺{r.Remaining:N2} kalan" : "";
        RowBg = "#11808080";
        BorderCol = r.IsPartial ? "#E9A23B" : (r.HasBillRequest ? "#EF4444" : "#22808080");
        SelectCmd = new RelayCommand(select);
    }
}

public class PayLineRow
{
    public string QtyText { get; }
    public string Name { get; }
    public string StatusText { get; }
    public string StatusBg { get; }
    public string Mods { get; }
    public bool HasMods { get; }
    public string LineTotalText { get; }
    public string UnitText { get; }

    public PayLineRow(OrderLine l, Func<string, Brush> res)
    {
        QtyText = $"{l.Qty:0.##}x";
        Name = l.ProductName ?? "";
        StatusText = l.LineStatus.ToTr();
        StatusBg = l.LineStatus switch
        {
            OrderLineStatus.Teslim => "#10B981",
            OrderLineStatus.Hazir => "#17B26A",
            OrderLineStatus.Hazirlaniyor => "#3B82F6",
            OrderLineStatus.MutfagaGonderildi => "#E9A23B",
            _ => "#6B7280"
        };
        Mods = string.IsNullOrWhiteSpace(l.KitchenNote) ? "" : $"+ {l.KitchenNote}";
        HasMods = !string.IsNullOrWhiteSpace(l.KitchenNote);
        LineTotalText = $"₺{l.LineTotal:N2}";
        UnitText = $"₺{l.UnitPrice:N2} / adet";
    }
}

public class PayMethodVM
{
    public PaymentKind Kind { get; }
    public string Label { get; }
    private readonly Func<string, Brush> _res;
    public Brush Bg { get; private set; }
    public Brush Fg { get; private set; }

    public PayMethodVM(PaymentKind kind, string label, Func<string, Brush> res)
    {
        Kind = kind; Label = label; _res = res;
        Bg = res("SurfaceAltBrush"); Fg = res("TextBrush");
    }
    public void SetActive(bool on)
    {
        Bg = on ? _res("AccentBrush") : _res("SurfaceAltBrush");
        Fg = on ? Brushes.White : _res("TextBrush");
    }
}

public class RelayCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}
