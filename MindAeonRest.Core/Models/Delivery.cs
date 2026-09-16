namespace MindAeonRest.Models;

public enum DeliveryPlatform { Telefon = 0, GelAl = 1, Trendyol = 2, Getir = 3, YemekSepeti = 4, Migros = 5, Diger = 6 }

public enum DeliveryStatus { Alindi = 0, Hazirlaniyor = 1, YolaCikti = 2, TeslimEdildi = 3, Iptal = 4 }

public static class DeliveryEnumExtensions
{
    public static string ToTr(this DeliveryPlatform p) => p switch
    {
        DeliveryPlatform.Telefon => "Telefon",
        DeliveryPlatform.GelAl => "Gel-Al",
        DeliveryPlatform.Trendyol => "Trendyol",
        DeliveryPlatform.Getir => "Getir",
        DeliveryPlatform.YemekSepeti => "Yemeksepeti",
        DeliveryPlatform.Migros => "Migros",
        _ => "Diğer"
    };

    public static string ToTr(this DeliveryStatus s) => s switch
    {
        DeliveryStatus.Alindi => "Alındı",
        DeliveryStatus.Hazirlaniyor => "Hazırlanıyor",
        DeliveryStatus.YolaCikti => "Yola Çıktı",
        DeliveryStatus.TeslimEdildi => "Teslim Edildi",
        DeliveryStatus.Iptal => "İptal",
        _ => s.ToString()
    };
}

/// <summary>Yemek sitesi/paket platform entegrasyon ayarı — ŞUBE BAZLI.</summary>
public class DeliveryPlatformSetting : AuditableEntity
{
    public int BranchId { get; set; }
    public DeliveryPlatform Platform { get; set; }
    public string? StoreId { get; set; }      // mağaza/restoran id
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public bool IsEnabled { get; set; }
    /// <summary>Gelen sipariş (inbound) otomatik paket adisyonuna dönüştürülsün mü (webhook/polling).</summary>
    public bool IsInboundEnabled { get; set; }
    public string PlatformText => Platform.ToTr();
}

/// <summary>Online platformdan gelen sipariş — sağlayıcıya bağımsız NORMALİZE edilmiş biçim.
/// TrendyolClient/GetirClient/YemekSepetiClient webhook/polling payload'unu buna çevirir; DeliveryInboundService bunu içeri alır.</summary>
public class InboundOrder
{
    public DeliveryPlatform Platform { get; set; }
    public string ExternalOrderNo { get; set; } = string.Empty;   // platform sipariş no (idempotency anahtarı)
    public string? StoreId { get; set; }                          // platform mağaza id → yerel şube çözümü
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; }
    public string? Note { get; set; }
    public decimal Total { get; set; }
    public List<InboundOrderItem> Items { get; set; } = new();
    public string? RawJson { get; set; }                          // ham payload (denetim/yeniden işleme için saklanır)
    public bool IsSimulation { get; set; }                        // TEST/simülasyon kaynaklı mı
}

/// <summary>Gelen siparişin tek kalemi (platform ürün kodu/adı → yerel ProductId eşlenir).</summary>
public class InboundOrderItem
{
    public string? ExternalCode { get; set; }   // platform ürün kodu (öncelikli eşleme anahtarı)
    public string? Name { get; set; }           // platform ürün adı (ikincil eşleme)
    public decimal Qty { get; set; } = 1;
    // NULL = platform fiyat BELİRTMEDİ → katalogdan çözülür. Değer (0 DAHİL) = platformun GEÇERLİ fiyatı
    // (ör. promosyon 0 ₺) → aynen kullanılır, katalog fiyatı EZMEZ.  '> 0' sentinel'i BİLEREK kaldırıldı.
    public decimal? UnitPrice { get; set; }     // platformun bildirdiği birim fiyat (null = katalogdan çöz)
    public decimal? TaxRate { get; set; }       // platformun bildirdiği KDV oranı (null = katalogdan çöz)
    public string? Note { get; set; }
}

/// <summary>Dış sipariş no ↔ yerel Order eşlemesi (idempotency + durum senkron kaydı).</summary>
public class DeliveryInboundMap : AuditableEntity
{
    public DeliveryPlatform Platform { get; set; }
    public string ExternalOrderNo { get; set; } = string.Empty;
    public int LocalOrderId { get; set; }
    public DeliveryStatus Status { get; set; }
    public string? RawJson { get; set; }
}

/// <summary>Kurye (paket teslimat).</summary>
public class Courier : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
