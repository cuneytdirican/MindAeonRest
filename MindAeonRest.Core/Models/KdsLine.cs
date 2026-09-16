namespace MindAeonRest.Models;

/// <summary>Mutfak ekranı (KDS) için bir hazırlık satırı (FİYATSIZ).</summary>
public class KdsLine
{
    public int LineId { get; set; }
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string? TableCodes { get; set; }
    public string? WaiterName { get; set; }
    public string? ProductName { get; set; }
    public decimal Qty { get; set; } = 1;
    public int SeatNo { get; set; }
    public string? KitchenNote { get; set; }
    public int? ReyonId { get; set; }
    public string? ReyonName { get; set; }
    public StationType StationType { get; set; } = StationType.Mutfak;   // V2: Mutfak ↔ Bar/İçecek
    public OrderLineStatus LineStatus { get; set; }
    public DateTime? SentAt { get; set; }
    public int PrintCount { get; set; }
    // V2 ek alanlar (kart başlığı/rozetler)
    public string? ZoneName { get; set; }
    public int CoverCount { get; set; }
    public string? OrderNote { get; set; }
    public string? AllergyNote { get; set; }
    public bool IsSpicy { get; set; }     // ACI (ürün)
    public bool Priority { get; set; }    // ÖNCELİK (sipariş)
    public bool IsVip { get; set; }       // VIP (sipariş)
    public OrderType OrderType { get; set; } = OrderType.MasaServis;
    public DeliveryPlatform Platform { get; set; }
    public List<KdsChild> Children { get; set; } = new();

    // Sipariş tipi etiketi/grubu (KDS kartı + filtre): Masa / Gel-Al / Paket / Online / Kiosk
    public bool IsTakeaway => OrderType == OrderType.TakeAway;
    public bool IsKiosk => OrderType == OrderType.Kiosk;
    public bool IsOnline => OrderType == OrderType.Paket && Platform is DeliveryPlatform.Trendyol
        or DeliveryPlatform.Getir or DeliveryPlatform.YemekSepeti or DeliveryPlatform.Migros;
    public bool IsPackage => OrderType == OrderType.Paket && !IsOnline;   // telefon/diğer = klasik paket
    public bool IsNonTable => OrderType != OrderType.MasaServis;
    public string TypeLabel => OrderType switch
    {
        OrderType.TakeAway => "GEL-AL",
        OrderType.Kiosk => "KIOSK",
        OrderType.Paket => IsOnline ? "ONLINE" : "PAKET",
        _ => "MASA"
    };

    public string QtyText => $"{Qty:0.##}x";
    public string SeatText => SeatNo > 0 ? $"{SeatNo}. Kişi" : "Ortak";
    public string StatusText => LineStatus.ToTr();
    public bool IsReady => LineStatus == OrderLineStatus.Hazir;
    public bool IsPreparing => LineStatus == OrderLineStatus.Hazirlaniyor;
    public bool IsBeverage => StationType == StationType.Bar;
    public bool HasAllergy => !string.IsNullOrWhiteSpace(AllergyNote);
    public string StatusColor => LineStatus switch
    {
        OrderLineStatus.Hazir => "#17B26A",
        OrderLineStatus.Hazirlaniyor => "#3B82F6",
        OrderLineStatus.Teslim => "#6B7280",
        _ => "#E9A23B"
    };
    public bool HasNote => !string.IsNullOrWhiteSpace(KitchenNote);
    public bool RePrinted => PrintCount > 1;

    /// <summary>Mutfağa gönderildiğinden bu yana geçen dakika.</summary>
    public int ElapsedMinutes
    {
        get
        {
            if (!SentAt.HasValue) return 0;
            var m = (int)(DateTime.UtcNow - SentAt.Value).TotalMinutes;
            return m < 0 ? 0 : m;
        }
    }
    public bool IsCritical => ElapsedMinutes >= 20;        // KRİTİK gecikme eşiği
    public bool IsCooling => IsReady && ElapsedMinutes >= 10;  // hazır ama alınmadı (soğuyor)
    public string ElapsedText
    {
        get
        {
            if (!SentAt.HasValue) return "";
            var s = DateTime.UtcNow - SentAt.Value;
            if (s.TotalMinutes < 0) s = TimeSpan.Zero;
            return s.TotalHours >= 1 ? $"{(int)s.TotalHours}sa {s.Minutes}dk" : $"{s.Minutes} dk";
        }
    }
}

public class KdsChild
{
    public string? ProductName { get; set; }
    public decimal Qty { get; set; }
    public string? KitchenNote { get; set; }
    public string QtyText => $"{Qty:0.##}x";
    public bool HasNote => !string.IsNullOrWhiteSpace(KitchenNote);
}
