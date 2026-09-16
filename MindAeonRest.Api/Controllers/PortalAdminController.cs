using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Api.Auth;
using MindAeonRest.Helpers;

namespace MindAeonRest.Api.Controllers;

/// <summary>FAZ 5a — Sadakat Portalı YÖNETİM ucu ([Authorize], /api → JWT). Restoran back-office'i buradan şubeye ait
/// kalıcı portal GİRİŞ bağlantısını (şirket+şube gömülü, uzun ömürlü token) alır → müşterilere paylaşılır / QR basılır.
/// Şirket kodu URL'de AÇIK geçmez (token imzalı) → enumerasyon yok. Public müşteri uçları PortalController'dadır.</summary>
[ApiController]
[Route("api/portal")]
[Authorize]
public class PortalAdminController(PortalTokenService tokens, IConfiguration config) : ControllerBase
{
    /// <summary>Şube için portal GİRİŞ bağlantısı üretir. branchId verilmezse oturumdaki şube kullanılır.
    /// Public taban adresi config Portal:PublicUrl (yoksa Public:OrderUrl komşuluğunda aynı origin /Portal).</summary>
    [HttpGet("link")]
    public IActionResult Link([FromQuery] int? branchId)
    {
        var company = Session.CurrentCompany;
        int bid = branchId is int b && b > 0 ? b : Session.CurrentBranch?.Id ?? 0;
        if (company is null || string.IsNullOrEmpty(company.DatabaseName) || bid <= 0)
            return BadRequest(new { message = "Şube bağlamı çözülemedi." });

        var token = tokens.CreateEntryToken(company.Code, company.DatabaseName, bid);

        // Hedef public adres: config ile override (ayrı host/PWA), yoksa aynı origin /Portal (QrCodes deseni).
        var baseUrl = (config["Portal:PublicUrl"] ?? "").TrimEnd('/');
        if (baseUrl.Length == 0) baseUrl = $"{Request.Scheme}://{Request.Host}/Portal";
        var url = $"{baseUrl}?t={Uri.EscapeDataString(token)}";

        return Ok(new { branchId = bid, token, url });
    }
}
