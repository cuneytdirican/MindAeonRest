namespace MindAeonRest.Models;

/// <summary>Ürün varyant/seçeneği. GroupName ile gruplanır (POS'ta her gruptan bir seçim sorulur).
/// Örn. Grup "Boyut" → Küçük(0)/Orta(+5)/Büyük(+10); Grup "Sıcaklık" → Sıcak(0)/Soğuk(0).</summary>
public class ProductOption : AuditableEntity
{
    public int ProductId { get; set; }
    public string GroupName { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceDelta { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }

    public string DeltaText => PriceDelta == 0 ? "—" : (PriceDelta > 0 ? $"+{PriceDelta:N2}" : $"{PriceDelta:N2}");
    public string DefaultText => IsDefault ? "✓" : "";
    public string Display => PriceDelta == 0 ? Name : $"{Name} ({DeltaText})";
}
