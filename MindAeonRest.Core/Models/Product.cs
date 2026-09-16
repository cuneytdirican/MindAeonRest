namespace MindAeonRest.Models;

/// <summary>Stok kartı (şirket DB). Menüler de birer Product'tır (reçeteleri ileride eklenecek).</summary>
public class Product : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? MainUnitId { get; set; }
    public int? SalesTaxGroupId { get; set; }
    public int? PurchaseTaxGroupId { get; set; }
    public int? ReturnTaxGroupId { get; set; }
    public TrackingType TrackingType { get; set; } = TrackingType.Yok;
    public bool IsActive { get; set; } = true;
    public bool IsSpicy { get; set; }   // ACI — KDS'te acı rozeti

    // FAZ 1a Maliyet Motoru: ağırlıklı-ortalama alış maliyeti + son alış maliyeti (alış evrağı POST edilince güncellenir).
    public decimal AvgCost { get; set; }
    public decimal LastCost { get; set; }

    // Gösterim (join)
    public string? MainUnitName { get; set; }
    public string? SalesTaxName { get; set; }
    public string? CategoryNames { get; set; }   // ürün listesinde kategori sütunu
    public bool HasImage { get; set; }            // liste için (bytes yüklenmeden ana resim var mı)

    // Düzenleme yardımcıları (kolon değil)
    public List<int> CategoryIds { get; set; } = new();

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string TrackingTypeText => TrackingType.ToTr();
}
