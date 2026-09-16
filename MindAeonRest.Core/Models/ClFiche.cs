namespace MindAeonRest.Models;

/// <summary>Ödeme kanalı (araç tipi). Ayrı "ödeme tipi" tablosu yok; araç=kanal.
/// 0-3 mevcut; 4+ V2 ile eklendi. Cari Hesap = AcikHesap(3), Yemek Kartı = YemekCeki(2) ile aynı muhasebe.</summary>
public enum PaymentKind
{
    Nakit = 0, KrediKarti = 1, YemekCeki = 2, AcikHesap = 3,
    BankaKarti = 4, Temassiz = 5, QrOdeme = 6, MobilOdeme = 7,
    HavaleEft = 8, OnlineOdeme = 9, HediyeCeki = 10, PuanSadakat = 11
}

public static class PaymentKindExtensions
{
    public static string ToTr(this PaymentKind k) => k switch
    {
        PaymentKind.Nakit => "Nakit",
        PaymentKind.KrediKarti => "Kredi Kartı",
        PaymentKind.YemekCeki => "Yemek Çeki",
        PaymentKind.AcikHesap => "Açık Hesap",
        PaymentKind.BankaKarti => "Banka Kartı",
        PaymentKind.Temassiz => "Temassız",
        PaymentKind.QrOdeme => "QR Ödeme",
        PaymentKind.MobilOdeme => "Mobil Ödeme",
        PaymentKind.HavaleEft => "Havale / EFT",
        PaymentKind.OnlineOdeme => "Online Ödeme",
        PaymentKind.HediyeCeki => "Hediye Çeki",
        PaymentKind.PuanSadakat => "Puan / Sadakat",
        _ => k.ToString()
    };

    /// <summary>Kart-türevi yöntemler → POS cihazı hareketine yazılır.</summary>
    public static bool IsCardType(this PaymentKind k) => k is PaymentKind.KrediKarti
        or PaymentKind.BankaKarti or PaymentKind.Temassiz or PaymentKind.QrOdeme
        or PaymentKind.MobilOdeme or PaymentKind.OnlineOdeme;

    /// <summary>Bir araç/hesap (kasa/POS/banka/yemek çeki/hediye çeki) seçilmesi ZORUNLU olan yöntemler.
    /// Araç seçilmeden tahsilat alınırsa para enstrüman defterine yazılmaz → parasal açık; bu yüzden fail-closed.
    /// Açık Hesap (cari) ve Puan/Sadakat (müşteri) araç gerektirmez.</summary>
    public static bool RequiresInstrument(this PaymentKind k) =>
        k == PaymentKind.Nakit || k.IsCardType() || k == PaymentKind.YemekCeki
        || k == PaymentKind.HavaleEft || k == PaymentKind.HediyeCeki;
}

/// <summary>Cari fiş (adisyon kapanışında kesilen mali belge). Satır detayı Order'da (referans).</summary>
public class ClFiche : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public DateTime FicheDate { get; set; }
    /// <summary>Fişin kesildiği KIOSK vardiyası (geriye uyumlu nullable). Vardiya raporu bu alana göre süzer.</summary>
    public int? ShiftId { get; set; }

    public string TotalText => $"{Total:N2} ₺";
    public string PaidText => $"{Paid:N2} ₺";
}

/// <summary>Cari fiş KDV kırılım satırı (oran bazında matrah/KDV/tutar).</summary>
public class ClFicheLine
{
    public int Id { get; set; }
    public int ClFicheId { get; set; }
    public decimal TaxRate { get; set; }
    public decimal Matrah { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string RateText => $"%{TaxRate:0.##}";
}

/// <summary>Ödeme hareketi (fişe bağlı). Araç: Kasa/POS/Yemek Çek Hesabı.</summary>
public class PaymentLine : AuditableEntity
{
    public int ClFicheId { get; set; }
    public PaymentKind Kind { get; set; }
    public int? InstrumentId { get; set; }    // CashRegister/Pos/MealVoucherAccount Id
    public int CurrencyId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PayDate { get; set; }

    public string KindText => Kind.ToTr();
    public string AmountText => $"{Amount:N2} ₺";
}
