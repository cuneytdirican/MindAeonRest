namespace MindAeonRest.Models;

/// <summary>Müşteri grubu (şirket DB).</summary>
public class CustomerGroup : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
