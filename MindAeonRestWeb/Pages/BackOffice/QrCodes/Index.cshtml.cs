using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Api.Auth;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;
using QRCoder;

namespace MindAeonRestWeb.Pages.BackOffice.QrCodes;

/// <summary>Masa QR Kodları — her masa için imzalı, ŞUBE+MASA gömülü public token üretir ve QR (inline PNG) gösterir.
/// Müşteri QR'ı okutunca <c>{public-url}?t={token}</c> açılır → anonim menü/sipariş (Faz 2 PWA). Token kapsamı SABİT
/// (masa+şube) olduğundan müşteri başka masaya erişemez. Yalnız SysAdmin/Patron + Lokasyon lisansı.</summary>
public class IndexModel(PublicTokenService tokens, IConfiguration config) : PageModel
{
    private readonly ZoneService _zones = new();
    private readonly TableService _tables = new();

    public sealed record TableQr(int TableId, string Code, string? Name, string Url, string QrDataUri);
    public sealed record ZoneGroup(int ZoneId, string ZoneName, string? BranchName, List<TableQr> Tables);

    public List<ZoneGroup> Groups { get; private set; } = new();
    public List<(int Id, string Name)> Branches { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.LocationTables);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        var companyCode = Session.CurrentCompany?.Code ?? "";
        var dbName = Session.CurrentCompany?.DatabaseName ?? "";
        if (string.IsNullOrEmpty(dbName)) return Page();

        // QR'ın işaret ettiği public sipariş adresi: config ile override edilebilir (ayrı host/PWA), yoksa aynı origin /order.
        var baseUrl = (config["Public:OrderUrl"] ?? "").TrimEnd('/');
        if (baseUrl.Length == 0) baseUrl = $"{Request.Scheme}://{Request.Host}/order";

        var zones = _zones.GetAll().Where(z => !z.IsDeleted).ToList();
        Branches = zones.Where(z => !string.IsNullOrEmpty(z.BranchName))
            .GroupBy(z => z.BranchId)
            .Select(g => (g.Key, g.First().BranchName!))
            .OrderBy(b => b.Item2).ToList();

        if (BranchId is int bid) zones = zones.Where(z => z.BranchId == bid).ToList();

        foreach (var z in zones.OrderBy(z => z.Code))
        {
            var rows = new List<TableQr>();
            foreach (var t in _tables.GetByZone(z.Id).OrderBy(t => t.Code))
            {
                var token = tokens.CreateOrderToken(companyCode, dbName, z.BranchId, t.Id);
                var url = $"{baseUrl}?t={Uri.EscapeDataString(token)}";
                rows.Add(new TableQr(t.Id, t.Code, t.Name, url, BuildQr(url)));
            }
            if (rows.Count > 0) Groups.Add(new ZoneGroup(z.Id, z.Name, z.BranchName, rows));
        }
        return Page();
    }

    /// <summary>URL → inline PNG data URI (QRCoder, sunucu tarafı; harici CDN YOK — 2FA ekranıyla aynı desen).</summary>
    private static string BuildQr(string url)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(5);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}
