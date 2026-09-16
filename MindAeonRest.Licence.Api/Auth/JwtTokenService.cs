using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Auth;

public class JwtTokenService(IConfiguration config)
{
    public const string ClaimKind = "kind";
    public const string ClaimDealer = "dealer";
    public const string ClaimBusiness = "business";

    public (string Token, DateTime ExpiresUtc) Create(ApplicationUser user, IList<string> roles)
    {
        var key = JwtKeyProvider.Resolve(config);
        var expires = DateTime.UtcNow.AddHours(int.TryParse(config["Jwt:ExpireHours"], out var h) ? h : 12);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("name", user.FullName ?? user.Email ?? ""),
            new(ClaimKind, ((int)user.Kind).ToString()),
        };
        if (user.DealerId is { } d) claims.Add(new(ClaimDealer, d.ToString()));
        if (user.BusinessId is { } b) claims.Add(new(ClaimBusiness, b.ToString()));
        foreach (var r in roles) claims.Add(new(ClaimTypes.Role, r));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
