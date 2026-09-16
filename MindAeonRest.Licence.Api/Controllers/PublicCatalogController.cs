using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// ANONİM public fiyat/katalog ucu — tanıtım (marketing) sitesi tüketir.
/// Üretici LİSTE fiyatını (marjsız BasePrice) döner; bayi marjı UYGULANMAZ.
/// Auth YOK; kötüye kullanıma karşı Program.cs'te IP başına ayrı oran sınırı vardır.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public")]
public class PublicCatalogController(LicenceDbContext db) : ControllerBase
{
    /// <summary>Tanıtım sitesi fiyatlandırması: paketler + limitler + özellikler + add-on'lar.</summary>
    [HttpGet("pricing")]
    public async Task<ActionResult<PublicPricingResp>> Pricing()
    {
        // Aktif taban fiyatları (paket × süre) — marjsız liste fiyatı.
        var prices = await db.PackagePrices.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Package, x.Duration, x.BasePrice, x.CurrencyCode })
            .ToListAsync();

        var packages = new List<PublicPackageDto>();
        foreach (var p in Enum.GetValues<LicensePackage>())
        {
            var monthly = prices.FirstOrDefault(x => x.Package == (int)p && x.Duration == DurationKind.Monthly);
            var yearly = prices.FirstOrDefault(x => x.Package == (int)p && x.Duration == DurationKind.Yearly);

            // Para birimi: mevcut bir fiyat satırından; hiç yoksa "TRY".
            string currency = monthly?.CurrencyCode ?? yearly?.CurrencyCode ?? "TRY";

            // Özellik bit maskesinden Türkçe okunur etiketler.
            var features = FeatureLabels(PackageMatrix.Features(p));

            packages.Add(new PublicPackageDto(
                Code: p.ToString(),
                Name: PackageMatrix.Name(p),
                Monthly: monthly?.BasePrice,
                Yearly: yearly?.BasePrice,
                Currency: currency,
                MaxUsers: PackageMatrix.MaxUsers(p),
                MaxKiosks: PackageMatrix.MaxKiosks(p),
                MaxBranches: PackageMatrix.MaxBranches(p),
                Features: features));
        }

        var addons = await db.AddOnPrices.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Kind).ThenBy(x => x.Name)
            .Select(x => new PublicAddOnDto(x.Name, x.Kind.ToString(), x.UnitPrice, x.CurrencyCode))
            .ToListAsync();

        return Ok(new PublicPricingResp("TRY", packages, addons));
    }

    /// <summary>Feature bit maskesini Türkçe okunur etiket listesine çevirir (PackageMatrix.FeatureName eşlemesi).</summary>
    private static List<string> FeatureLabels(Feature set)
    {
        var labels = new List<string>();
        foreach (var f in Enum.GetValues<Feature>())
        {
            if (f == Feature.None) continue;
            if ((set & f) == f)
                labels.Add(PackageMatrix.FeatureName(f));
        }
        return labels;
    }
}

/// <summary>Public fiyatlandırma yanıtı (tanıtım sitesi bu şekle göre parse eder).</summary>
public record PublicPricingResp(
    string Currency,
    IReadOnlyList<PublicPackageDto> Packages,
    IReadOnlyList<PublicAddOnDto> Addons);

public record PublicPackageDto(
    string Code,
    string Name,
    decimal? Monthly,
    decimal? Yearly,
    string Currency,
    int MaxUsers,
    int MaxKiosks,
    int MaxBranches,
    IReadOnlyList<string> Features);

public record PublicAddOnDto(
    string Name,
    string Kind,
    decimal UnitPrice,
    string Currency);
