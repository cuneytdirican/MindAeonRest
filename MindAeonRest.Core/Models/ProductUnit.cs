namespace MindAeonRest.Models;

/// <summary>Ürün birimi (ana birim + alternatif birimler, çevrim katsayıları ve ölçüler).</summary>
public class ProductUnit : AuditableEntity
{
    public int ProductId { get; set; }
    public int UnitId { get; set; }          // sistem DB MainUnits.Id
    public string? UnitName { get; set; }    // gösterim (cross-db join)
    public bool IsMainUnit { get; set; }
    public decimal ConversionFactor1 { get; set; } = 1;
    public decimal ConversionFactor2 { get; set; } = 1;
    public decimal? Width { get; set; }
    public decimal? Length { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }

    public string IsMainText => IsMainUnit ? "Ana" : "";
    public string ConvText => $"{ConversionFactor1:0.##} / {ConversionFactor2:0.##}";
}
