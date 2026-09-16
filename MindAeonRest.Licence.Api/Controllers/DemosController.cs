using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Demo kurulum kayıtları. Admin tüm bayilerin demolarını yönetir;
/// Bayi SADECE kendi tenant'ındaki demoları görür/oluşturur (UI değil API'de sahiplikle zorlanır).
/// </summary>
[ApiController]
[Route("api/v1/demos")]
[Authorize(Roles = "Admin,Dealer")]
public class DemosController(LicenceDbContext db) : ControllerBase
{
    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>JWT'deki bayi kimliği (varsa).</summary>
    private int? CurrentDealerId =>
        int.TryParse(User.FindFirst(JwtTokenService.ClaimDealer)?.Value, out var d) ? d : null;

    /// <summary>Bayi/masaüstü demo kaydı oluştur. Bayi kendi tenant'ına yazar; Admin DealerId verebilir.</summary>
    [HttpPost]
    public async Task<ActionResult<DemoResp>> Create([FromBody] CreateDemoReq req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyCode))
            return BadRequest(new { message = "Şirket kodu zorunludur." });

        // Sahiplik: Bayi kendi DealerId'sini kullanmak zorunda; Admin serbest.
        int? dealerId = IsAdmin ? req.DealerId : CurrentDealerId;
        if (!IsAdmin && dealerId is null)
            return Forbid();

        var demo = new DemoInstall
        {
            DealerId = dealerId,
            CompanyCode = req.CompanyCode.Trim(),
            BusinessTitle = req.BusinessTitle,
            HardwareId = req.HardwareId,
            ContactName = req.ContactName,
            Phone = req.Phone,
            Email = req.Email,
            StartedUtc = DateTime.UtcNow,
            Converted = false,
            CreatedByUserId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
        };
        db.DemoInstalls.Add(demo);
        await db.SaveChangesAsync();

        return Ok(Map(demo));
    }

    /// <summary>Demo listesi: Admin tümünü, Bayi sadece kendi tenant'ını görür.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DemoResp>>> List()
    {
        var q = db.DemoInstalls.Where(d => !d.IsDeleted);
        if (!IsAdmin)
        {
            var dealerId = CurrentDealerId;
            if (dealerId is null) return Forbid();
            q = q.Where(d => d.DealerId == dealerId);
        }

        var rows = await q.OrderByDescending(d => d.StartedUtc).ToListAsync();
        return Ok(rows.Select(Map).ToList());
    }

    /// <summary>Tek demo (sahiplik denetimli).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DemoResp>> Get(int id)
    {
        var demo = await db.DemoInstalls.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (demo is null) return NotFound(new { message = "Demo bulunamadı." });
        if (!IsAdmin && demo.DealerId != CurrentDealerId) return Forbid();
        return Ok(Map(demo));
    }

    /// <summary>Demo'yu lisansa dönüştürüldü olarak işaretle (sahiplik denetimli).</summary>
    [HttpPost("{id:int}/convert")]
    public async Task<ActionResult<DemoResp>> Convert(int id, [FromBody] ConvertDemoReq req)
    {
        var demo = await db.DemoInstalls.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        if (demo is null) return NotFound(new { message = "Demo bulunamadı." });
        if (!IsAdmin && demo.DealerId != CurrentDealerId) return Forbid();

        demo.Converted = true;
        demo.LicenseId = req.LicenseId;
        demo.UpdatedUtc = DateTime.UtcNow;
        demo.UpdatedByUserId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        await db.SaveChangesAsync();

        return Ok(Map(demo));
    }

    private static DemoResp Map(DemoInstall d) => new(
        d.Id, d.DealerId, d.CompanyCode, d.BusinessTitle, d.HardwareId,
        d.ContactName, d.Phone, d.Email, d.StartedUtc, d.Converted, d.LicenseId);
}
