using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Puan/Sadakat: müşteri bakiye, ekstre, program ayarları (oranlar).</summary>
[Route("api/loyalty")]
[Authorize(Roles = "SysAdmin,Kasiyer,Garson")]   // bakiye/ekstre tüm servis personeli; ayarlar yalnız SysAdmin (aşağıda)
public class LoyaltyController : PosControllerBase
{
    private readonly LoyaltyService _svc = new();

    /// <summary>Müşteri puan bakiyesi + program ayarları (ödeme ekranı için).</summary>
    [HttpGet("balance/{customerId:int}")]
    public ActionResult<object> Balance(int customerId)
    {
        var s = _svc.GetSettings();
        return Ok(new { customerId, points = _svc.GetBalance(customerId), s.IsActive, s.RedeemRate, s.EarnRate });
    }

    /// <summary>Müşteri puan hareketleri (ekstre).</summary>
    [HttpGet("movements/{customerId:int}")]
    public ActionResult<IEnumerable<LoyaltyMovement>> Movements(int customerId) => Ok(_svc.GetMovements(customerId));

    /// <summary>Sadakat programı ayarları.</summary>
    [HttpGet("settings")]
    [Authorize(Roles = "SysAdmin")]
    public ActionResult<LoyaltySettings> GetSettings() => Ok(_svc.GetSettings());

    /// <summary>Sadakat programı ayarlarını kaydet.</summary>
    [HttpPost("settings")]
    [Authorize(Roles = "SysAdmin")]
    public IActionResult SaveSettings([FromBody] LoyaltySettingsDto req)
    {
        try { _svc.SaveSettings(new LoyaltySettings { IsActive = req.IsActive, EarnRate = req.EarnRate, RedeemRate = req.RedeemRate }, UserId); return Ok(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
