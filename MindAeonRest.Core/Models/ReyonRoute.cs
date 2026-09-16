namespace MindAeonRest.Models;

/// <summary>
/// Ürün→Reyon yönlendirme kuralı. En özelden genele çözülür:
/// (zone+ürün) > (ürün) > (zone+kategori) > (kategori) > (zone varsayılan).
/// Örn: Beach'ten verilen Pizza, Otel'deki Pizza reyonuna düşer.
/// </summary>
public class ReyonRoute : AuditableEntity
{
    public int? SourceZoneId { get; set; }     // null = tüm lokasyonlar
    public string? SourceZoneName { get; set; }
    public int? ProductId { get; set; }        // null = ürün kuralı değil
    public string? ProductName { get; set; }
    public int? CategoryId { get; set; }       // null = kategori kuralı değil
    public string? CategoryName { get; set; }
    public int ReyonId { get; set; }
    public string? ReyonName { get; set; }

    public string ScopeText =>
        (SourceZoneName is null ? "Tüm lokasyonlar" : SourceZoneName) + " · " +
        (ProductName is not null ? $"Ürün: {ProductName}" : CategoryName is not null ? $"Kategori: {CategoryName}" : "Genel");
    public string Display => $"{ScopeText} → {ReyonName}";
}
