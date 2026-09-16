using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRestWeb.Auth;

namespace MindAeonRestWeb.Pages;

/// <summary>Yönetim panelinden POS ekranlarına (Adisyon/Mutfak/Kasiyer/Patron) SSO ile geçiş:
/// mevcut web oturumundaki kullanıcı için JWT üretir, PWA'ya URL fragment'ıyla devreder (tekrar giriş yok).
/// mode = waiter | kitchen | cashier | patron | kiosk | display (PWA açılış ekranı; kiosk = herkese açık self-servis; display = salt-okunur müşteri ekranı).</summary>
public class PosHandoffModel(IConfiguration config, PwaTokenIssuer pwaToken) : PageModel
{
    public string Target { get; private set; } = "";

    public IActionResult OnGet(string? mode)
    {
        // ApiBaseUrl boşsa aynı origin (/app) — birleşik deploy varsayılanı; localhost:5000 kırılgan varsayılanı kaldırıldı.
        var pwa = (config["ApiBaseUrl"] ?? "").TrimEnd('/') + "/app";
        mode = (mode ?? "").Trim().ToLowerInvariant();
        if (mode is not ("waiter" or "kitchen" or "cashier" or "patron" or "kiosk" or "display")) mode = "";
        // KIOSK: web sanal kioskudur; tek-terminal deploy'da PosKioskNo ile gerçek kiosk verilebilir (varsayılan 1).
        int kioskNo = int.TryParse(config["PosKioskNo"], out var k) && k > 0 ? k : 1;

        try
        {
            var token = pwaToken.Create(
                Session.CurrentUserId ?? 0,
                Session.CurrentUser?.FullName ?? "",
                Session.CurrentCompany?.Code ?? "",
                Session.CurrentCompany?.DatabaseName ?? "",
                Session.CurrentBranch?.Id,
                Session.CurrentRoles,
                kioskNo,
                (long)(Session.CurrentLicense?.Features ?? 0),
                Session.CurrentLicense?.Package.ToString() ?? "Lite",
                // Session.CurrentLicense middleware'de cookie claim'lerinden gerçek limitlerle kurulur (FIX C3) → PWA'ya doğru taşınır.
                Session.CurrentLicense?.MaxUsers ?? 0,
                Session.CurrentLicense?.MaxBranches ?? 0,
                Session.CurrentLicense?.MaxKiosks ?? 0,
                Session.CurrentLicense?.ExpiresUtc);

            var frag = "#token=" + Uri.EscapeDataString(token);
            if (mode.Length > 0) frag += "&mode=" + mode;
            Target = pwa + frag;
        }
        catch
        {
            Target = pwa;   // anahtar yoksa PWA kendi girişini yapar
        }
        return Page();
    }
}
