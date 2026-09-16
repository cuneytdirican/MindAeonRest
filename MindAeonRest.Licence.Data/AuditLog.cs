using System.ComponentModel.DataAnnotations;

namespace MindAeonRest.Licence.Data;

/// <summary>Değişiklik denetim kaydı — kritik alanlar (marj, fiyat, lisans durumu) ve genel CRUD izi.</summary>
public class AuditLog
{
    public long Id { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    [MaxLength(40)] public string Action { get; set; } = "";        // Added / Modified / Deleted
    [MaxLength(150)] public string EntityType { get; set; } = "";
    [MaxLength(100)] public string? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    [MaxLength(64)] public string? Ip { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// İstek bağlamından (HttpContext) okunan kiracı bilgisi. Data katmanı arabirimi tanımlar;
/// Api katmanı (TenantContext) HttpContext'ten doldurur ve Scoped olarak DI'ya kaydeder.
/// </summary>
public interface ITenantInfo
{
    bool IsAdmin { get; }
    int? DealerId { get; }
    int? BusinessId { get; }
    /// <summary>Denetim kaydı için aktif kullanıcı kimliği.</summary>
    string? UserId { get; }
    /// <summary>Denetim kaydı için istemci IP'si.</summary>
    string? Ip { get; }
}
