using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.AuditTrail;

/// <summary>Denetim İzi (Audit Trail) — kim-neyi-ne-zaman-değiştirdi geçmişi (before/after).
/// RAPOR deseni (filtre → sonuç tablo): tarih aralığı + kullanıcı + entityType.
/// HASSAS ekran → yalnız <see cref="Session.IsSysAdmin"/>. AuditService (Core) ham SQL ile okur.</summary>
public class IndexModel : PageModel
{
    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int UserId { get; set; }        // 0 = tümü
    [BindProperty(SupportsGet = true)] public string? EntityType { get; set; } // boş = tümü

    public List<SelectListItem> UserOptions { get; private set; } = new();
    public List<SelectListItem> EntityOptions { get; private set; } = new();

    // ── Sonuç ──
    public List<AuditLogEntry> Rows { get; private set; } = new();
    public string? SubTitle { get; private set; }
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (!Session.IsSysAdmin) return Forbid();   // hassas: yalnız SysAdmin
        Prepare();
        Build();
        return Page();
    }

    private void Prepare()
    {
        // Kullanıcı seçenekleri (sistem DB'sindeki tüm kullanıcılar)
        UserOptions = new List<SelectListItem> { new("Tüm Kullanıcılar", "0") };
        try
        {
            UserOptions.AddRange(new UserService().GetAll()
                .Select(u => new SelectListItem($"{u.FullName} ({u.Username})", u.Id.ToString())));
        }
        catch { /* kullanıcı listesi alınamazsa filtre yine çalışır */ }

        // EntityType seçenekleri (kayıtlı değerlerden)
        EntityOptions = new List<SelectListItem> { new("Tüm Varlıklar", "") };
        EntityOptions.AddRange(AuditService.DistinctEntityTypes()
            .Select(e => new SelectListItem(e, e)));

        From ??= DateTime.Today.AddDays(-7);
        To ??= DateTime.Today;
    }

    private void Build()
    {
        try
        {
            var from = (From ?? DateTime.Today).Date;
            var to = (To ?? DateTime.Today).Date;
            if (to < from) (from, to) = (to, from);
            int? uid = UserId > 0 ? UserId : null;
            var etype = string.IsNullOrWhiteSpace(EntityType) ? null : EntityType;

            Rows = AuditService.Query(from, to, uid, etype);

            var userText = uid is int u
                ? UserOptions.FirstOrDefault(o => o.Value == u.ToString())?.Text ?? $"Kullanıcı {u}"
                : "Tüm Kullanıcılar";
            var entText = etype ?? "Tüm Varlıklar";
            SubTitle = $"{userText} · {entText} · {from:dd.MM.yyyy} - {to:dd.MM.yyyy} · {Rows.Count} kayıt";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    /// <summary>UTC saklanan zamanı yerel saatle gösterir.</summary>
    public static string LocalTime(DateTime utc) =>
        utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>before→after JSON'unu ekranda okunur, kısaltılmış özete indirger.</summary>
    public static string Summarize(string? json, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(json)) return "—";
        string text;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var parts = doc.RootElement.EnumerateObject()
                    .Select(p => $"{p.Name}={Scalar(p.Value)}");
                text = string.Join(", ", parts);
            }
            else text = json;
        }
        catch { text = json; }
        return text.Length > max ? text.Substring(0, max) + "…" : text;
    }

    private static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null => "∅",
        JsonValueKind.Array => "[…]",
        JsonValueKind.Object => "{…}",
        _ => e.ToString()
    };
}
