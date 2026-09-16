namespace MindAeonRest.Models;

/// <summary>Fiyat tipi.</summary>
public enum PriceType
{
    Alis = 1,
    Satis = 2
}

public static class PriceTypeExtensions
{
    public static string ToTr(this PriceType t) => t switch
    {
        PriceType.Alis => "Alış",
        PriceType.Satis => "Satış",
        _ => t.ToString()
    };
}

/// <summary>
/// Ürün fiyatı (şube + döviz + tarih/SAAT aralıklı). Müşteri/grup/fiyat listesi opsiyonel
/// (boşsa genel fiyat). Restoran happy-hour gibi saat aralıkları için StartDateTime/EndDateTime.
/// </summary>
public class ProductPrice : AuditableEntity
{
    public int ProductId { get; set; }
    public int UnitId { get; set; }
    public string? UnitName { get; set; }
    public PriceType PriceType { get; set; } = PriceType.Satis;
    public decimal Price { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public DateTime? StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
    public int? CustomerId { get; set; }
    public int? CustomerGroupId { get; set; }
    public int? PriceListId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? ListPrice { get; set; }          // Liste fiyatı (satış fiyatından farklı olabilir)
    public bool IsTaxIncluded { get; set; } = true;  // Fiyat KDV dahil mi?

    public string? ProductCode { get; set; }         // gösterim (matris)
    public string? ProductName { get; set; }         // gösterim (matris)
    public string? PriceListName { get; set; }       // gösterim (matris)

    public string PriceTypeText => PriceType.ToTr();
    public string PriceText => $"{Price:N2} {CurrencyCode}";
    public string TaxInclText => IsTaxIncluded ? "Dahil" : "Hariç";
    public string ListPriceText => ListPrice is null or 0 ? "—" : $"{ListPrice:N2} {CurrencyCode}";
    public string PeriodText
    {
        get
        {
            if (StartDateTime is null && EndDateTime is null) return "Süresiz";
            static string F(DateTime d) => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd.MM.yy") : d.ToString("dd.MM.yy HH:mm");
            var s = StartDateTime is { } sd ? F(sd) : "…";
            var e = EndDateTime is { } ed ? F(ed) : "süresiz";
            return $"{s} → {e}";
        }
    }
    public string ScopeText => BranchName ?? "Tüm şubeler";
    public string ProductText => $"{ProductCode} · {ProductName}";
    public string BranchScopeText => BranchName ?? "Tüm Şubeler";
    public string ListScopeText => PriceListName ?? "Genel";
}
