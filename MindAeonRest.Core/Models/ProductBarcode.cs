namespace MindAeonRest.Models;

/// <summary>Ürün barkodu (birim bazlı). Parti/seri/renk-beden takipleri ileride eklenecek.</summary>
public class ProductBarcode : AuditableEntity
{
    public int ProductId { get; set; }
    public int UnitId { get; set; }          // hangi birime ait (MainUnits.Id)
    public string? UnitName { get; set; }    // gösterim
    public string Barcode { get; set; } = string.Empty;
}
