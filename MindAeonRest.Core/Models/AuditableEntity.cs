namespace MindAeonRest.Models;

/// <summary>
/// Tüm kayıtların ortak denetim (audit) alanları.
/// Sistemde fiziksel silme YOKTUR; silme işlemi IsDeleted=1 ile yapılır.
/// </summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public int? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}
