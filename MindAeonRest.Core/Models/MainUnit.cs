namespace MindAeonRest.Models;

/// <summary>Ana birim (Adet, Kg, Litre...). Sistem DB'sinde tutulur, şirketlerce ortak kullanılır.</summary>
public class MainUnit : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
