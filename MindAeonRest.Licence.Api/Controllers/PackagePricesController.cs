using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Paket × süre taban fiyat matrisi.
/// CRUD yalnız Admin'e açık. Bayi/Müşteri yalnız "list" (marj uygulanmış görünüm) okuyabilir.
/// </summary>
[ApiController]
[Route("api/v1/package-prices")]
[Authorize]
public class PackagePricesController(LicenceDbContext db) : ControllerBase
{
    private static string PackageName(int package)
    {
        var p = (LicensePackage)package;
        return Enum.IsDefined(typeof(LicensePackage), p) ? PackageMatrix.Name(p) : $"#{package}";
    }

    private static PackagePriceResp ToAdminResp(PackagePrice p) => new(
        p.Id, p.Package, PackageName(p.Package), p.Duration, p.BasePrice, p.CurrencyCode, p.IsActive, p.CreatedUtc, p.UpdatedUtc);

    private string? CurrentUserId =>
        User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

    private int? CurrentDealerId =>
        int.TryParse(User.FindFirst(JwtTokenService.ClaimDealer)?.Value, out var d) ? d : null;

    // ----------------------------------------------------------------- ADMIN CRUD

    /// <summary>ADMIN: tüm fiyat matrisi (pasifler dahil).</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<PackagePriceResp>>> List([FromQuery] bool includeInactive = true)
    {
        var q = db.Set<PackagePrice>().AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(x => x.IsActive);
        var items = await q
            .OrderBy(x => x.Package).ThenBy(x => x.Duration)
            .Select(x => ToAdminResp(x))
            .ToListAsync();
        return Ok(items);
    }

    /// <summary>ADMIN: tek fiyat satırı.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackagePriceResp>> Get(int id)
    {
        var p = await db.Set<PackagePrice>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound(new { message = "Fiyat satırı bulunamadı." });
        return Ok(ToAdminResp(p));
    }

    /// <summary>ADMIN: yeni paket×süre fiyatı. Aynı (paket,süre) aktif satır varsa reddeder.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackagePriceResp>> Create([FromBody] PackagePriceUpsertReq req)
    {
        if (!Enum.IsDefined(typeof(LicensePackage), req.Package))
            return BadRequest(new { message = "Geçersiz paket." });
        if (!Enum.IsDefined(typeof(DurationKind), req.Duration))
            return BadRequest(new { message = "Geçersiz süre." });
        if (req.BasePrice < 0)
            return BadRequest(new { message = "Taban fiyat negatif olamaz." });

        bool dup = await db.Set<PackagePrice>()
            .AnyAsync(x => x.Package == req.Package && x.Duration == req.Duration);
        if (dup) return Conflict(new { message = "Bu paket ve süre için zaten bir fiyat satırı var." });

        var entity = new PackagePrice
        {
            Package = req.Package,
            Duration = req.Duration,
            BasePrice = req.BasePrice,
            CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "TRY" : req.CurrencyCode.Trim().ToUpperInvariant(),
            IsActive = req.IsActive,
            CreatedByUserId = CurrentUserId
        };
        db.Set<PackagePrice>().Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToAdminResp(entity));
    }

    /// <summary>ADMIN: fiyat satırını güncelle.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackagePriceResp>> Update(int id, [FromBody] PackagePriceUpsertReq req)
    {
        if (!Enum.IsDefined(typeof(LicensePackage), req.Package))
            return BadRequest(new { message = "Geçersiz paket." });
        if (!Enum.IsDefined(typeof(DurationKind), req.Duration))
            return BadRequest(new { message = "Geçersiz süre." });
        if (req.BasePrice < 0)
            return BadRequest(new { message = "Taban fiyat negatif olamaz." });

        var entity = await db.Set<PackagePrice>().FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound(new { message = "Fiyat satırı bulunamadı." });

        bool dup = await db.Set<PackagePrice>()
            .AnyAsync(x => x.Id != id && x.Package == req.Package && x.Duration == req.Duration);
        if (dup) return Conflict(new { message = "Bu paket ve süre için zaten başka bir fiyat satırı var." });

        entity.Package = req.Package;
        entity.Duration = req.Duration;
        entity.BasePrice = req.BasePrice;
        entity.CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "TRY" : req.CurrencyCode.Trim().ToUpperInvariant();
        entity.IsActive = req.IsActive;
        entity.UpdatedUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = CurrentUserId;
        await db.SaveChangesAsync();
        return Ok(ToAdminResp(entity));
    }

    /// <summary>ADMIN: fiyat satırını sil (soft-delete: pasifleştirir + IsDeleted).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Set<PackagePrice>().FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null) return NotFound(new { message = "Fiyat satırı bulunamadı." });

        entity.IsActive = false;
        entity.IsDeleted = true;
        entity.DeletedUtc = DateTime.UtcNow;
        entity.UpdatedUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = CurrentUserId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ----------------------------------------------------------- BAYI / MÜŞTERİ OKUMA

    /// <summary>
    /// Bayi/Müşteri: satışa açık (aktif) fiyat listesi. Bayi token'ında dealer claim varsa,
    /// bayinin MarginPercent değeri taban fiyata uygulanır (CustomerPaysUs senaryosunda müşteri görünümü).
    /// Admin de bu ucu çağırabilir (taban fiyat dahil döner).
    /// </summary>
    [HttpGet("catalog")]
    [Authorize(Roles = "Admin,Dealer,Customer")]
    public async Task<ActionResult<IReadOnlyList<PackagePricePublicResp>>> Catalog()
    {
        bool isAdmin = User.IsInRole("Admin");

        // Marj kaynağı: bayi ise kendi marjı; müşteri ise (varsa) işletmesine bağlı bayi marjı.
        decimal margin = 0m;
        var dealerId = CurrentDealerId;
        if (dealerId is { } did)
        {
            margin = await db.Dealers.AsNoTracking()
                .Where(d => d.Id == did)
                .Select(d => d.MarginPercent)
                .FirstOrDefaultAsync();
        }
        else if (User.IsInRole("Customer"))
        {
            var businessId = int.TryParse(User.FindFirst(JwtTokenService.ClaimBusiness)?.Value, out var b) ? (int?)b : null;
            if (businessId is { } bid)
            {
                margin = await db.Businesses.AsNoTracking()
                    .Where(x => x.Id == bid && x.DealerId != null)
                    .Select(x => x.Dealer!.MarginPercent)
                    .FirstOrDefaultAsync();
            }
        }

        var rows = await db.Set<PackagePrice>().AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Package).ThenBy(x => x.Duration)
            .Select(x => new { x.Package, x.Duration, x.BasePrice, x.CurrencyCode })
            .ToListAsync();

        var result = rows.Select(x =>
        {
            decimal price = decimal.Round(x.BasePrice * (1 + margin / 100m), 2, MidpointRounding.AwayFromZero);
            return new PackagePricePublicResp(
                x.Package,
                PackageName(x.Package),
                x.Duration,
                price,
                x.CurrencyCode,
                margin,
                isAdmin ? x.BasePrice : null);
        }).ToList();

        return Ok(result);
    }
}
