namespace MindAeonRest.Models;

/// <summary>
/// Bir kullanıcının hangi şirket/şubeye giriş yapabileceğini tutar.
/// BranchId NULL ise kullanıcı o şirketin tüm şubelerine girebilir.
/// SysAdmin bu tabloya bakılmaksızın her yere girebilir.
/// </summary>
public class UserCompanyAccess : AuditableEntity
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
}
