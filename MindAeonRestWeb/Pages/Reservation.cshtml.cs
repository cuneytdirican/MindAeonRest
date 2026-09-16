using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRestWeb.Pages;

/// <summary>FAZ 5c — ONLINE REZERVASYON + BEKLEME LİSTESİ müşteri sayfası (anonim, mobil-öncelikli, tam ekran).
/// Müşteri restoranın rezervasyon linkini açınca <c>/Reservation?t={token}</c> gelir. Sunucu token'ı DOĞRULAMAZ —
/// yalnız kabuğu (SPA) servis eder; token istemci JS'ine verilir ve tüm işlemler (müsaitlik/oluştur/bekleme-listesi)
/// [AllowAnonymous] ReservationPublicController uçlarında ELLE doğrulanır (şube kapsamı sabit, IDOR imkânsız).
/// Uygun masa varsa rezervasyon açılır (onay SMS'i); yoksa müşteri bekleme listesine alınır. Native alert/confirm
/// YOK — durum/onay sayfa içi custom bileşenle gösterilir.</summary>
public class ReservationModel : PageModel
{
    /// <summary>URL'deki ?t= (rezervasyon token'ı). Boş olabilir; doğrulama istemci + Controller tarafında yapılır.</summary>
    public string Token { get; private set; } = "";

    public void OnGet(string? t) => Token = t ?? "";
}
