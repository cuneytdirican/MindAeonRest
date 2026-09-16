namespace MindAeonRest.Models;

/// <summary>Ürün kategorisi (şirket DB). Tek resim DB'de binary olarak tutulur (kiosk için).</summary>
public class ProductCategory : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public byte[]? Image { get; set; }
    public bool HasImage { get; set; }   // liste için (bytes yüklenmeden)
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ImageText => (Image is { Length: > 0 } || HasImage) ? "Var" : "Yok";
    public string Display => $"{Name} ({Code})";
}
