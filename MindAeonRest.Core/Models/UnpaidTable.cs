namespace MindAeonRest.Models;

/// <summary>Hesabı alınmamış (açık / kısmi / ödeme bekleyen) masa özeti — kasiyer/garson aramadan görsün.</summary>
public class UnpaidTable
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string? TableCodes { get; set; }
    public string? ZoneName { get; set; }
    public string? WaiterName { get; set; }
    public int CoverCount { get; set; }
    public DateTime OpenedAt { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public bool HasBillRequest { get; set; }   // "Hesap istiyor" servis isteği var mı
    public int OrderType { get; set; }          // 0=MasaServis, 1=Paket, 2=TakeAway/Gel-Al/Kiosk-self-servis, 3=Kiosk
    public string? DeliveryName { get; set; }   // paket/gel-al/self-servis siparişte teslim/müşteri adı (masa yerine gösterilir)

    public decimal Remaining => Total - Paid;
    public bool IsPartial => Paid > 0 && Remaining > 0;
    /// <summary>Masaya bağlı DEĞİL (paket/gel-al/self-servis kiosk) → masa listesinde görünmez; kanal etiketiyle gösterilmeli.</summary>
    public bool IsNonTable => OrderType != 0 || string.IsNullOrEmpty(TableCodes);
    /// <summary>Kanal etiketi: kasiyerin masasız siparişi (paket/gel-al/self-servis) ayırt edip ödeyebilmesi için.</summary>
    public string ChannelText => OrderType switch { 1 => "Paket", 2 => "Gel-Al / Self-Servis", 3 => "Kiosk", _ => "Masa" };
    /// <summary>Ekranda gösterilecek ana etiket: masa kodu yoksa teslim adı, o da yoksa adisyon kodu.</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(TableCodes) ? TableCodes! :
        !string.IsNullOrWhiteSpace(DeliveryName) ? DeliveryName! : OrderCode;
    public string StatusText => IsPartial ? "Kısmi Ödendi" : (HasBillRequest ? "Ödeme Bekliyor" : "Açık");
    public string TotalText => $"{Total:N2} ₺";
    public string RemainingText => $"{Remaining:N2} ₺";

    public string ElapsedText
    {
        get
        {
            var s = DateTime.UtcNow - OpenedAt;
            if (s.TotalMinutes < 0) s = TimeSpan.Zero;
            return s.TotalHours >= 1 ? $"{(int)s.TotalHours} sa {s.Minutes} dk" : $"{s.Minutes} dk";
        }
    }
}
