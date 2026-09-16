using System.Security.Claims;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Tenant;

/// <summary>
/// İstek bağlamından (HttpContext / JWT claim'leri) kiracı bilgisini doldurur.
/// Scoped kaydedilir; DbContext (query filter) ve denetim interceptor'ı bunu okur.
/// </summary>
public sealed class TenantContext : ITenantInfo
{
    public bool IsAdmin { get; }
    public int? DealerId { get; }
    public int? BusinessId { get; }
    public string? UserId { get; }
    public string? Ip { get; }

    public TenantContext(IHttpContextAccessor accessor)
    {
        var http = accessor.HttpContext;
        var user = http?.User;

        Ip = http?.Connection?.RemoteIpAddress?.ToString();

        if (user?.Identity?.IsAuthenticated != true)
        {
            // Kimliksiz istek (ping vb.) — hiçbir kiracı verisine erişemez (Admin değil).
            return;
        }

        UserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        IsAdmin = user.IsInRole("Admin")
                  || user.FindFirstValue(JwtTokenService.ClaimKind) == ((int)UserKind.Admin).ToString();

        if (int.TryParse(user.FindFirstValue(JwtTokenService.ClaimDealer), out var d)) DealerId = d;
        if (int.TryParse(user.FindFirstValue(JwtTokenService.ClaimBusiness), out var bz)) BusinessId = bz;
    }
}
