using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(UserManager<ApplicationUser> users, JwtTokenService jwt) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResp>> Login([FromBody] LoginReq req)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, req.Password))
            return Unauthorized(new { message = "E-posta veya parola hatalı." });

        var roles = await users.GetRolesAsync(user);
        var (token, exp) = jwt.Create(user, roles);
        return Ok(new LoginResp(token, exp, roles.FirstOrDefault() ?? "", user.FullName ?? "", user.Kind.ToString()));
    }
}
