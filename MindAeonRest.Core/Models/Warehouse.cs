namespace MindAeonRest.Models;

/// <summary>Depo (şirket DB). Code firma bazında unique; BranchId hangi şubeye ait.</summary>
public class Warehouse : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string? BranchName { get; set; }   // gösterim (sistem DB cross-db join)
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
