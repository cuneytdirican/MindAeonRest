namespace MindAeonRest.Models;

/// <summary>Fiyat listesi (şirket DB).</summary>
public class PriceList : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
