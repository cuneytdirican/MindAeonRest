namespace MindAeonRest.Models;

public class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CanUseAi { get; set; }

    // Görüntüleme alanı (DataGrid için)
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string AiText => CanUseAi ? "✓ AI Yetkili" : "—";
}
