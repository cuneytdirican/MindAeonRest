namespace MindAeonRest.Models;

/// <summary>
/// Ürün başına hazır açıklama/not (ör. "Soğan olmasın", "Sarımsak olmasın").
/// Sınırsız sayıda; KIOSK siparişinde seçilir, mutfakta görünür.
/// </summary>
public class ProductDescription : AuditableEntity
{
    public int ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
