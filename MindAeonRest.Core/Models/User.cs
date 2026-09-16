namespace MindAeonRest.Models;

public class User : AuditableEntity
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }                  // birincil/gösterim rolü
    public string RoleName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new(); // tüm roller (çoklu)
    public string RolesText => Roles.Count > 0 ? string.Join(", ", Roles) : RoleName;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>true ise kullanıcı (zayıf/varsayılan parola) ilk girişte parolasını DEĞİŞTİRMEK zorundadır.</summary>
    public bool MustChangePassword { get; set; }

    // Gösterim: kullanıcının seçili şirketteki şube erişim özeti
    public string? AccessSummary { get; set; }

    // Görüntüleme alanları (DataGrid için)
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string LastLoginText => LastLoginAt.HasValue
        ? LastLoginAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        : "—";
}
