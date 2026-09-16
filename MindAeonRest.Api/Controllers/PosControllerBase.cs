using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;

namespace MindAeonRest.Api.Controllers;

/// <summary>Kimlik doğrulanmış POS controller'ları için ortak taban: bağlamdaki şube/kullanıcı/şirket.</summary>
[Authorize]
[ApiController]
public abstract class PosControllerBase : ControllerBase
{
    /// <summary>İstek bağlamındaki şube (JWT'den; patron için query ile geçersiz kılınabilir).</summary>
    protected int BranchId(int? q = null) => q ?? Session.CurrentBranch?.Id ?? 0;
    protected int? UserId => Session.CurrentUserId;
    protected string CompanyCode => Session.CurrentCompany?.Code ?? "";
}
