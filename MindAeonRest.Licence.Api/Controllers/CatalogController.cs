using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>Lisans kataloğu: paketler + satılabilir add-on modüller (portal lisans-üretim ekranı kullanır).</summary>
[ApiController]
[Route("api/v1/catalog")]
[Authorize(Roles = "Admin")]
public class CatalogController : ControllerBase
{
    /// <summary>Ayrı satılabilen add-on modüller (bit + ad).</summary>
    [HttpGet("addons")]
    public ActionResult<IEnumerable<object>> AddOns() =>
        Ok(PackageMatrix.AddOnModules.Select(f => new { bit = (long)f, name = PackageMatrix.FeatureName(f) }));

    /// <summary>Paketler + taban limitleri (kullanıcı/terminal/şube).</summary>
    [HttpGet("packages")]
    public ActionResult<IEnumerable<object>> Packages() =>
        Ok(Enum.GetValues<LicensePackage>().Select(p => new
        {
            value = (int)p,
            name = PackageMatrix.Name(p),
            maxUsers = PackageMatrix.MaxUsers(p),
            maxKiosks = PackageMatrix.MaxKiosks(p),
            maxBranches = PackageMatrix.MaxBranches(p),
            baseFeatures = (long)PackageMatrix.Features(p),
            describe = PackageMatrix.Describe(p)
        }));
}
