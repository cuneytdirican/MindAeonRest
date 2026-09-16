using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services.Ai;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages.BackOffice;

/// <summary>
/// Back-office AI Chat widget'ının sunucu ucu (tüm sayfalarda çalışan yüzen widget → buraya POST eder).
/// Neden Razor Page (API değil): /api uçları back-office cookie'sini KABUL ETMEZ (JWT-only) ve tenant kurulmaz.
/// Bu handler cookie alanındadır → AuthorizeFolder("/") cookie-auth'u, WebTenantMiddleware Session+CompanyDb'yi,
/// antiforgery (RequestVerificationToken header) doğrulamayı otomatik sağlar.
/// Yetki: Permissions.Ai (rol CanUseAi; SysAdmin bypass) + Feature.AiHub lisansı. İkisi de savunma-derinliği için
/// hem burada hem AiHubService.Ask içinde denetlenir.
/// </summary>
[EnableRateLimiting("ai")]
public class AiAskModel : PageModel
{
    /// <summary>İstemci gövdesi: soru + sayfa bağlamı (yalnız etiket/alan ADLARI — KVKK/enumerasyon: DEĞER GÖNDERİLMEZ).</summary>
    public sealed class AskRequest
    {
        public string? Question { get; set; }
        public ClientContext? Context { get; set; }
    }

    public sealed class ClientContext
    {
        public string? Path { get; set; }
        public string? Title { get; set; }
        public string? NavLabel { get; set; }
        public List<string>? Fields { get; set; }
    }

    private static bool CanAccess => Permissions.Ai && Session.LicenseHas(Feature.AiHub);

    /// <summary>Soruyu bağlamla zenginleştirip AiHubService.Ask'a iletir; { text, table } JSON döndürür.</summary>
    public IActionResult OnPostAsk([FromBody] AskRequest? req)
    {
        // (a) YETKİ — yetkisiz/lisanssız 403 (native mesaj sızdırmadan).
        if (!CanAccess)
            return new JsonResult(new { error = "forbidden" }) { StatusCode = 403 };

        // (b) Soru
        var question = (req?.Question ?? "").Trim();
        if (question.Length == 0)
            return new JsonResult(new { error = "Lütfen bir soru yazın." });

        // (c) Sunucu-taraf bağlam zenginleştirme: istemci path'ini kanonik nav öğesine eşle → Section/Label/Help.
        var ctx = req?.Context;
        var clientPath = (ctx?.Path ?? "").Trim();
        string? section = null, label = null, help = null;
        if (clientPath.Length > 0)
        {
            foreach (var s in BackOfficeNav.All)
            {
                var item = s.Items.FirstOrDefault(i => i.Url is not null &&
                    string.Equals(i.Url.Split('?')[0], clientPath, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    section = s.Title;
                    label = item.Label;
                    help = item.Help;
                    break;
                }
            }
        }

        var parts = new List<string>();
        if (section is not null && label is not null)
            parts.Add($"Ekran: {section} › {label}.");
        else if (!string.IsNullOrWhiteSpace(ctx?.Title))
            parts.Add($"Ekran: {ctx!.Title!.Trim()}.");
        if (!string.IsNullOrWhiteSpace(help))
            parts.Add($"Açıklama: {help!.Trim()}.");
        var fields = ctx?.Fields?
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Take(40)
            .ToList();
        if (fields is { Count: > 0 })
            parts.Add($"Formdaki alanlar: {string.Join(", ", fields)}.");
        if (clientPath.Length > 0)
            parts.Add($"Yol: {clientPath}.");
        var pageContext = parts.Count > 0 ? string.Join(" ", parts) : null;

        // (d) AI çağrısı (in-process, salt-okuma deterministik araçlar).
        try
        {
            AiAnswer ans = new AiHubService().Ask(question, pageContext);
            return new JsonResult(new { text = ans.Text, table = BuildTable(ans.Table) });
        }
        catch (Exception ex)
        {
            // Native/sağlayıcı mesajını sızdırma; iz bırak, kullanıcıya generic Türkçe mesaj.
            System.Diagnostics.Debug.WriteLine($"[AiAsk] Ask başarısız: {ex.Message}");
            return new JsonResult(new { error = "Yanıt üretilemedi. Lütfen tekrar deneyin." });
        }
    }

    /// <summary>ReportTable'ı istemci render'ı için { columns, rows, footer } JSON'a çevirir (hücreler tr-TR biçimli).</summary>
    private static object? BuildTable(ReportTable? t)
    {
        if (t is not { Rows.Count: > 0, Columns.Count: > 0 }) return null;
        var tr = CultureInfo.GetCultureInfo("tr-TR");

        string Cell(object? v, ReportColumn c)
        {
            if (v is null) return "";
            if (c.Numeric && v is IFormattable f) return f.ToString(c.Effective, tr);
            return v.ToString() ?? "";
        }

        string[] Row(object?[] r) =>
            Enumerable.Range(0, t.Columns.Count)
                .Select(i => Cell(i < r.Length ? r[i] : null, t.Columns[i]))
                .ToArray();

        return new
        {
            columns = t.Columns.Select(c => new { header = c.Header, numeric = c.Numeric }).ToArray(),
            rows = t.Rows.Select(Row).ToArray(),
            footer = t.Footer is { } ft ? Row(ft) : null
        };
    }
}
