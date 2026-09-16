namespace MindAeonRest.Models;

public enum OrderStatus { Acik = 0, Kapandi = 1, Iptal = 2 }

public enum OrderType { MasaServis = 0, Paket = 1, TakeAway = 2, Kiosk = 3 }

// Hazirlaniyor=5 sona eklendi (mevcut int değerleri korunur). KDS V2 kanban: Mutfakta(1)=Bekleyen → Hazirlaniyor(5) → Hazir(2) → Teslim(3).
public enum OrderLineStatus { Beklemede = 0, MutfagaGonderildi = 1, Hazir = 2, Teslim = 3, Iptal = 4, Hazirlaniyor = 5 }

public static class OrderEnumExtensions
{
    public static string ToTr(this OrderStatus s) => s switch
    {
        OrderStatus.Acik => "Açık",
        OrderStatus.Kapandi => "Kapandı",
        OrderStatus.Iptal => "İptal",
        _ => s.ToString()
    };

    public static string ToTr(this OrderLineStatus s) => s switch
    {
        OrderLineStatus.Beklemede => "Beklemede",
        OrderLineStatus.MutfagaGonderildi => "Mutfakta",
        OrderLineStatus.Hazirlaniyor => "Hazırlanıyor",
        OrderLineStatus.Hazir => "Hazır",
        OrderLineStatus.Teslim => "Teslim",
        OrderLineStatus.Iptal => "İptal",
        _ => s.ToString()
    };
}

/// <summary>Adisyon (açık sipariş belgesi). Operasyonel kaynak; stok/cari fişler bundan türetilir.</summary>
public class Order : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int ZoneId { get; set; }
    public string? ZoneName { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Acik;
    public OrderType OrderType { get; set; } = OrderType.MasaServis;
    public int CoverCount { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? ReservationId { get; set; }
    public string? Note { get; set; }
    public string? AllergyNote { get; set; }   // V2: alerji uyarısı (KDS/ödeme'de kırmızı rozet)
    public bool Priority { get; set; }          // ÖNCELİK (KDS rozeti)
    public bool IsVip { get; set; }             // VIP (KDS rozeti)
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Teslimat (paket / take-away)
    public DeliveryPlatform Platform { get; set; }
    public DeliveryStatus DeliveryStatus { get; set; }
    public int? CourierId { get; set; }
    public string? CourierName { get; set; }
    public string? DeliveryName { get; set; }
    public string? DeliveryPhone { get; set; }
    public string? DeliveryAddress { get; set; }
    public string PlatformText => Platform.ToTr();
    public string DeliveryStatusText => DeliveryStatus.ToTr();

    // gösterim
    public List<int> TableIds { get; set; } = new();
    public string? TableCodes { get; set; }
    public decimal Total { get; set; }
    public string StatusText => Status.ToTr();
    public string TotalText => $"{Total:N2} ₺";
    public string Summary => $"{Code} · {TableCodes} · {CustomerName} · {Total:N2} ₺";
}

/// <summary>Adisyon satırı. Satılan ürünün kimliği + fiyatı + mutfak yönlendirme/durum burada.</summary>
public class OrderLine : AuditableEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int? ReyonId { get; set; }
    public string? ReyonName { get; set; }
    public int SeatNo { get; set; }              // kişi no (0 = ortak)
    public int? ParentLineId { get; set; }       // menü alt satırı
    public decimal Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public OrderLineStatus LineStatus { get; set; } = OrderLineStatus.Beklemede;
    public string? KitchenNote { get; set; }
    public int PrintCount { get; set; }
    public DateTime? LastPrintedAt { get; set; }
    public bool IsTreat { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? CampaignId { get; set; }         // bu satırın iskonto/ikramını yazan kampanya (elle iskonto → null)
    public int? SettledClFicheId { get; set; }
    public decimal RefundedQty { get; set; }

    // gösterim
    public string? Descriptions { get; set; }    // ProductDescriptions (mutfak için)
    public decimal RefundableQty => Qty - RefundedQty;
    public bool IsComponent => ParentLineId.HasValue;
    public decimal LineTotal => IsComponent || IsTreat ? 0 : (Qty * UnitPrice - DiscountAmount);
    public string LineTotalText => $"{LineTotal:N2} ₺";
    public string StatusText => LineStatus.ToTr();
    public string DurumText => IsDeleted ? "İptal / Silinmiş" : LineStatus.ToTr();
    public string QtyText => $"{Qty:0.##}";
    public string SeatText => SeatNo > 0 ? $"{SeatNo}. Kişi" : "Ortak";
}
