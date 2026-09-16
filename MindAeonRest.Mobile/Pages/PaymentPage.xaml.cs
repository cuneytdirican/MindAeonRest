using System.Collections.ObjectModel;
using System.Globalization;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public class PaymentRow
{
    public int Kind { get; set; }
    public int? InstrumentId { get; set; }
    public string InstrumentName { get; set; } = "";
    public decimal Amount { get; set; }
    public string KindText => PaymentPage.KindName(Kind);
}

public partial class PaymentPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly int _orderId;
    private readonly decimal _total;
    private readonly int _coverCount;
    private readonly Action _onPaid;
    private readonly ObservableCollection<PaymentRow> _payments = new();

    // PaymentKind 0-11 (backend ile birebir)
    private int _kind = 0;
    private int? _overrideCustomer;     // Açık Hesap(3) + Puan(11)
    private GiftCardDto? _giftCard;      // Hediye Çeki(10)
    private LoyaltyBalanceDto? _loyalty; // Puan(11)
    private decimal _loyaltyMax;         // puan × redeemRate (₺ cinsi tavan)

    // (Kind, Etiket) — telefon/tablet düzeni için kısa etiketler
    private static readonly (int Kind, string Label)[] Methods =
    {
        (0,  "Nakit"),
        (1,  "Kart"),
        (4,  "Banka Kartı"),
        (5,  "Temassız"),
        (6,  "QR Ödeme"),
        (7,  "Mobil Ödeme"),
        (8,  "Havale/EFT"),
        (9,  "Online"),
        (2,  "Yemek Çeki"),
        (3,  "Açık Hesap"),
        (10, "Hediye Çeki"),
        (11, "Puan"),
    };

    // Enstrüman çözümü için kind kümeleri
    private static bool IsCardKind(int k) => k is 1 or 4 or 5 or 6 or 7 or 9; // kart-türevleri + Online
    private static bool UsesInstrumentPicker(int k) => IsCardKind(k) || k == 2 /*Yemek*/ || k == 8 /*Havale=banka*/;
    private static bool UsesCustomerPicker(int k) => k == 3 /*Cari*/ || k == 11 /*Puan*/;

    public static string KindName(int k) => k switch
    {
        1 => "Kredi Kartı", 2 => "Yemek Çeki", 3 => "Açık Hesap",
        4 => "Banka Kartı", 5 => "Temassız", 6 => "QR Ödeme", 7 => "Mobil Ödeme",
        8 => "Havale/EFT", 9 => "Online Ödeme", 10 => "Hediye Çeki", 11 => "Puan/Sadakat",
        _ => "Nakit"
    };

    public PaymentPage(ApiClient api, int orderId, decimal total, string tableName, Action onPaid, int coverCount = 1)
    {
        InitializeComponent();
        _api = api; _orderId = orderId; _total = total; _onPaid = onPaid;
        _coverCount = coverCount > 0 ? coverCount : 1;
        TitleLabel.Text = $"Ödeme · {tableName}";
        PaymentsView.ItemsSource = _payments;
        TotalLbl.Text = $"₺{_total:N2}";
        BuildMethodButtons();
        Recalc();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SelectMethod(0);
    }

    private decimal Paid => _payments.Sum(p => p.Amount);
    private decimal Remaining => _total - Paid;

    private void Recalc()
    {
        PaidLbl.Text = $"₺{Paid:N2}";
        RemainingLbl.Text = $"₺{Remaining:N2}";
    }

    // ===== Yöntem butonları =====
    private readonly Dictionary<int, Button> _methodButtons = new();

    private void BuildMethodButtons()
    {
        MethodPanel.Children.Clear();
        _methodButtons.Clear();
        foreach (var (kind, label) in Methods)
        {
            var btn = new Button
            {
                Text = label,
                CommandParameter = kind,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 10,
                HeightRequest = 46,
                WidthRequest = 100,
                Margin = new Thickness(3),
                Padding = new Thickness(2, 0),
            };
            btn.Clicked += Method_Clicked;
            _methodButtons[kind] = btn;
            MethodPanel.Children.Add(btn);
        }
        StyleMethodButtons();
    }

    private void StyleMethodButtons()
    {
        var on = GetColor("Accent", "#E91E63");
        var onText = Colors.White;
        var off = GetColor("ButtonColor", "#EAECF1");
        var offText = GetColor("TextColor", "#2B2B3A");
        foreach (var (kind, btn) in _methodButtons)
        {
            bool sel = kind == _kind;
            btn.BackgroundColor = sel ? on : off;
            btn.TextColor = sel ? onText : offText;
        }
    }

    private Color GetColor(string key, string fallback)
    {
        if (Resources.TryGetValue(key, out var v) && v is Color c) return c;
        if (Application.Current?.Resources.TryGetValue(key, out var av) == true && av is Color ac) return ac;
        return Color.FromArgb(fallback);
    }

    private async void Method_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is int k) await SelectMethod(k);
    }

    private async Task SelectMethod(int kind)
    {
        _kind = kind;
        StyleMethodButtons();
        Err(null);

        // Panel görünürlükleri
        bool picker = UsesInstrumentPicker(kind) || UsesCustomerPicker(kind);
        InstrPanel.IsVisible = picker;
        GiftPanel.IsVisible = kind == 10;
        LoyaltyInfoLbl.IsVisible = kind == 11;

        // Yöntem değişince geçici durumları sıfırla
        _giftCard = null; GiftInfoLbl.Text = "";
        if (kind != 11) { _loyalty = null; _loyaltyMax = 0; }
        if (kind != 3 && kind != 11) _overrideCustomer = null;

        try
        {
            if (UsesCustomerPicker(kind))
            {
                InstrLabel.Text = kind == 11 ? "MÜŞTERİ (PUAN)" : "CARİ (AÇIK HESAP)";
                var custs = await _api.CustomersAsync();
                InstrPicker.ItemsSource = custs;
            }
            else if (UsesInstrumentPicker(kind))
            {
                InstrLabel.Text = kind == 8 ? "BANKA (HAVALE/EFT)" : kind == 2 ? "YEMEK ÇEKİ" : "KASA / POS / HESAP";
                var ins = await _api.InstrumentsAsync(kind);
                InstrPicker.ItemsSource = ins;
            }
            else
            {
                InstrPicker.ItemsSource = null; // Nakit(0) ve Hediye(10) — picker yok
            }

            if (((System.Collections.IList?)InstrPicker.ItemsSource)?.Count > 0)
                InstrPicker.SelectedIndex = 0;
        }
        catch (Exception ex) { Err(ex.Message); }
    }

    // Müşteri (Puan) seçilince bakiyeyi çek
    private async void InstrPicker_Changed(object? sender, EventArgs e)
    {
        if (_kind != 11) return;
        if (InstrPicker.SelectedItem is not CustomerDto c) { LoyaltyInfoLbl.Text = ""; return; }
        try
        {
            _loyalty = await _api.LoyaltyBalanceAsync(c.Id);
            if (!_loyalty.IsActive)
            {
                _loyaltyMax = 0;
                LoyaltyInfoLbl.Text = $"{c.Name}: puan programı pasif — kullanılamaz.";
                return;
            }
            _loyaltyMax = _loyalty.Points * _loyalty.RedeemRate;
            LoyaltyInfoLbl.Text = $"{c.Name}: {_loyalty.Points:N0} puan ≈ ₺{_loyaltyMax:N2} (1 puan = ₺{_loyalty.RedeemRate:N2})";
        }
        catch (Exception ex) { _loyalty = null; _loyaltyMax = 0; Err(ex.Message); }
    }

    // Hediye çeki sorgula
    private async void QueryGift_Clicked(object? sender, EventArgs e)
    {
        Err(null);
        var code = (GiftCodeEntry.Text ?? "").Trim();
        if (string.IsNullOrEmpty(code)) { Err("Hediye çeki kodu girin."); return; }
        try
        {
            _giftCard = await _api.GiftCardByCodeAsync(code);
            if (_giftCard.Status != 0 /* GiftCardStatus.Aktif=0 */ || _giftCard.Balance <= 0)
            {
                GiftInfoLbl.Text = _giftCard.Status != 0 ? "Çek aktif değil." : "Çek bakiyesi yok.";
                _giftCard = null;
                return;
            }
            GiftInfoLbl.Text = $"Bakiye: ₺{_giftCard.Balance:N2}";
        }
        catch (Exception ex) { _giftCard = null; GiftInfoLbl.Text = ""; Err(ex.Message); }
    }

    // ===== Numpad =====
    private void Numpad_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string key) return;
        var cur = AmountEntry.Text ?? "";
        if (cur == "0" && key != "." && key != "back") cur = "";

        switch (key)
        {
            case "back":
                cur = cur.Length > 0 ? cur[..^1] : "";
                break;
            case ".":
                if (!cur.Contains('.')) cur = cur.Length == 0 ? "0." : cur + ".";
                break;
            default:
                // en fazla 2 ondalık
                var dot = cur.IndexOf('.');
                if (dot >= 0 && cur.Length - dot > 2) return;
                cur += key;
                break;
        }
        AmountEntry.Text = cur.Length == 0 ? "0" : cur;
    }

    // ===== Bölme =====
    private async void Split_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string mode) return;
        decimal rem = Remaining; if (rem < 0) rem = 0;
        decimal val;
        switch (mode)
        {
            case "full": val = _total; break;          // tüm toplam
            case "rest": val = rem; break;             // kalan
            case "2": case "3": case "4":
                var n = int.Parse(mode);
                val = Math.Round(_total / n, 2, MidpointRounding.AwayFromZero);
                break;
            case "cover":
                int cover = _coverCount;
                if (cover <= 1)
                {
                    var inp = await DisplayPromptAsync("Kişi Başı", "Kişi sayısı:", "Tamam", "İptal",
                        keyboard: Keyboard.Numeric, initialValue: _coverCount.ToString());
                    if (!int.TryParse(inp, out cover) || cover <= 0) return;
                }
                val = Math.Round(_total / cover, 2, MidpointRounding.AwayFromZero);
                break;
            default: return;
        }
        if (val > rem) val = rem; // kalanı asla aşma
        AmountEntry.Text = val.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void FillRemaining_Clicked(object sender, EventArgs e)
    {
        var rem = Remaining;
        AmountEntry.Text = (rem > 0 ? rem : 0).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void AddPayment_Clicked(object sender, EventArgs e)
    {
        Err(null);
        if (!decimal.TryParse((AmountEntry.Text ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        { Err("Geçerli bir tutar girin."); return; }

        decimal rem = Remaining;
        if (rem <= 0.005m) { Err("Bu hesap zaten tamamen ödendi."); return; }
        if (amount > rem + 0.005m) { amount = rem; AmountEntry.Text = amount.ToString("0.##", CultureInfo.InvariantCulture); }

        int? instrId = null; string instrName = "";

        if (UsesCustomerPicker(_kind))
        {
            if (InstrPicker.SelectedItem is not CustomerDto c)
            { Err(_kind == 11 ? "Puan için müşteri seçin." : "Açık hesap için cari seçin."); return; }
            _overrideCustomer = c.Id; instrName = c.Name;

            if (_kind == 11)
            {
                if (_loyalty is null || !_loyalty.IsActive) { Err("Bu müşterinin puan programı pasif."); return; }
                if (amount > _loyaltyMax + 0.005m)
                { Err($"Puan yetersiz: en fazla ₺{_loyaltyMax:N2} kullanılabilir."); return; }
                instrName = $"{c.Name} (puan)";
            }
        }
        else if (_kind == 10) // Hediye çeki
        {
            if (_giftCard is null) { Err("Önce hediye çekini sorgulayın."); return; }
            if (amount > _giftCard.Balance + 0.005m)
            { Err($"Çek bakiyesi yetersiz: en fazla ₺{_giftCard.Balance:N2}."); return; }
            instrId = _giftCard.Id;
            instrName = $"Hediye Çeki ({_giftCard.Code ?? GiftCodeEntry.Text})";
        }
        else if (UsesInstrumentPicker(_kind))
        {
            if (InstrPicker.SelectedItem is not InstrumentDto inst) { Err("Kasa/POS/hesap seçin."); return; }
            instrId = inst.Id; instrName = $"{inst.Name} ({inst.Scope})";
        }
        else // Nakit(0)
        {
            instrName = "Nakit";
        }

        _payments.Add(new PaymentRow { Kind = _kind, InstrumentId = instrId, InstrumentName = instrName, Amount = amount });
        AmountEntry.Text = "0";
        Recalc();
    }

    private void RemovePayment_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is PaymentRow row)
        {
            _payments.Remove(row);
            // Cari/puan referansı kalmadıysa override'ı temizle
            if (!_payments.Any(p => p.Kind == 3 || p.Kind == 11)) _overrideCustomer = null;
            Recalc();
        }
    }

    private async void Complete_Clicked(object sender, EventArgs e)
    {
        Err(null);
        if (_payments.Count == 0) { Err("En az bir ödeme ekleyin."); return; }
        bool hasAcik = _payments.Any(p => p.Kind == 3);
        if (Paid + 0.005m < _total && !hasAcik)
        { Err($"Eksik ödeme: kalan ₺{Remaining:N2}. Ya tahsil edin ya da 'Açık Hesap' ile cariye işleyin."); return; }

        var lines = _payments.Select(p => new PaymentLineDto(p.Kind, p.Amount, p.InstrumentId, 0)).ToList();
        CompleteBtn.IsEnabled = false;
        try
        {
            await _api.PostAsync($"/api/orders/{_orderId}/settle", new SettleRequest(null, lines, _overrideCustomer));
            await DisplayAlert("Ödeme", "Adisyon kapatıldı.", "Tamam");
            _onPaid();   // çağıran (AdisyonPage) panoya döner
        }
        catch (Exception ex) { Err(ex.Message); CompleteBtn.IsEnabled = true; }
    }

    private void Err(string? m) { ErrorLbl.Text = m ?? ""; ErrorLbl.IsVisible = !string.IsNullOrEmpty(m); }
    private async void Back_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();
}
