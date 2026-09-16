namespace MindAeonRest.Models;

/// <summary>Ürün resmi (şirket DB, binary). Ana resim kiosk ekranında gösterilir.</summary>
public class ProductImage : AuditableEntity
{
    public int ProductId { get; set; }
    public byte[] Image { get; set; } = Array.Empty<byte>();
    public bool IsMain { get; set; }
    public int SortOrder { get; set; }
}
