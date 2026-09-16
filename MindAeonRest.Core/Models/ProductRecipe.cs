namespace MindAeonRest.Models;

/// <summary>
/// Menü/reçete bileşeni. Menü de bir Product'tır; bileşenleri başka stok kartlarıdır
/// (ör. Hamburger Menü = Hamburger + Patates + Cola). Sınırsız bileşen.
/// </summary>
public class ProductRecipe : AuditableEntity
{
    public int MenuProductId { get; set; }
    public int ComponentProductId { get; set; }
    public string? ComponentName { get; set; }   // gösterim
    public decimal Quantity { get; set; } = 1;
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }         // gösterim

    public string QuantityText => $"{Quantity:0.##} {UnitName}".Trim();
}
