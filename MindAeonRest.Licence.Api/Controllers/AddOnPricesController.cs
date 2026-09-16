using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>Add-on fiyat kataloğu: satılan modül + ek kullanıcı/terminal/şube fiyatları + fiyat teklifi.</summary>
[ApiController]
[Route("api/v1/addon-prices")]
[Authorize(Roles = "Admin")]
public class AddOnPricesController(LicenceDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List([FromQuery] bool includeInactive = false)
    {
        var q = db.AddOnPrices.AsNoTracking().Where(x => includeInactive || x.IsActive);
        return Ok(await q.OrderBy(x => x.Kind).ThenBy(x => x.Name)
            .Select(x => new { x.Id, kind = (int)x.Kind, x.FeatureBit, x.Name, x.UnitPrice, x.CurrencyCode, x.IsActive })
            .ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddOnPriceReq req)
    {
        if (Validate(req) is { } err) return BadRequest(new { message = err });
        db.AddOnPrices.Add(new AddOnPrice
        {
            Kind = (AddOnKind)req.Kind, FeatureBit = req.FeatureBit, Name = req.Name.Trim(),
            UnitPrice = req.UnitPrice, CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "TRY" : req.CurrencyCode, IsActive = req.IsActive
        });
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AddOnPriceReq req)
    {
        if (Validate(req) is { } err) return BadRequest(new { message = err });
        var a = await db.AddOnPrices.FindAsync(id);
        if (a is null) return NotFound();
        a.Kind = (AddOnKind)req.Kind; a.FeatureBit = req.FeatureBit; a.Name = req.Name.Trim();
        a.UnitPrice = req.UnitPrice; a.CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "TRY" : req.CurrencyCode;
        a.IsActive = req.IsActive; a.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Add-on fiyat doğrulaması: Ad zorunlu, fiyat negatif olamaz, Kind geçerli,
    /// Module türünde geçerli bir FeatureBit (add-on modül) gerekir. Hata mesajı döner; geçerliyse null.</summary>
    private static string? Validate(AddOnPriceReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return "Ad zorunludur.";
        if (req.UnitPrice < 0)
            return "Birim fiyat negatif olamaz.";
        if (!Enum.IsDefined(typeof(AddOnKind), req.Kind))
            return "Geçersiz add-on türü (Kind).";

        var kind = (AddOnKind)req.Kind;
        if (kind == AddOnKind.Module)
        {
            // Modül kalemi mutlaka bir add-on modül özelliğine (FeatureBit) bağlı olmalı.
            if (req.FeatureBit == 0)
                return "Modül türü için FeatureBit zorunludur.";
            if (!PackageMatrix.AddOnModules.Contains((Feature)req.FeatureBit))
                return "Geçersiz modül özelliği (FeatureBit); satılabilir add-on modül değil.";
        }
        return null;
    }

    /// <summary>Seçilen add-on'lar + ekstra adetler için fiyat teklifi (add-on alt toplamı; paket taban fiyatı hariç).</summary>
    [HttpGet("quote")]
    public async Task<ActionResult<object>> Quote([FromQuery] long addOnFeatures = 0,
        [FromQuery] int extraUsers = 0, [FromQuery] int extraKiosks = 0, [FromQuery] int extraBranches = 0)
    {
        var prices = await db.AddOnPrices.AsNoTracking().Where(x => x.IsActive).ToListAsync();
        decimal total = 0; var lines = new List<object>();
        foreach (var f in PackageMatrix.AddOnModules)
        {
            if ((addOnFeatures & (long)f) == 0) continue;
            var p = prices.FirstOrDefault(x => x.Kind == AddOnKind.Module && x.FeatureBit == (long)f);
            var amt = p?.UnitPrice ?? 0; total += amt;
            lines.Add(new { type = "module", name = PackageMatrix.FeatureName(f), qty = 1, unit = amt, amount = amt });
        }
        void Unit(AddOnKind k, int qty, string label)
        {
            if (qty <= 0) return;
            var p = prices.FirstOrDefault(x => x.Kind == k);
            var unit = p?.UnitPrice ?? 0; var amt = unit * qty; total += amt;
            lines.Add(new { type = k.ToString().ToLowerInvariant(), name = label, qty, unit, amount = amt });
        }
        Unit(AddOnKind.User, extraUsers, "Ek Kullanıcı");
        Unit(AddOnKind.Kiosk, extraKiosks, "Ek Terminal");
        Unit(AddOnKind.Branch, extraBranches, "Ek Şube");
        var cur = prices.FirstOrDefault()?.CurrencyCode ?? "TRY";
        return Ok(new { total, currencyCode = cur, lines });
    }
}

public record AddOnPriceReq(int Kind, long FeatureBit, string Name, decimal UnitPrice, string? CurrencyCode, bool IsActive = true);
