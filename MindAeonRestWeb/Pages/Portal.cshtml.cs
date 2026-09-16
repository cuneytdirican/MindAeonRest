using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRestWeb.Pages;

/// <summary>FAZ 5a MÜŞTERİ SADAKAT PORTALI sayfası (anonim, tam ekran, mobil-öncelikli). Müşteri restoranın verdiği
/// /Portal?t={girisToken} bağlantısını açar → telefon+OTP ile giriş → puan bakiyesi / sipariş geçmişi / yeniden-sipariş.
/// Sunucu token'ı DOĞRULAMAZ; yalnız kabuğu (SPA) servis eder — GİRİŞ token'ı istemci JS'ine verilir ve tüm veri/işlemler
/// [AllowAnonymous] PortalController uçlarında token ELLE doğrulanarak yürür. OTP sonrası müşteri-scope oturum token'ı
/// üretilir → müşteri yalnız KENDİ kartını görür (IDOR imkânsız). Token boşsa istemci "geçersiz bağlantı" ekranı gösterir.</summary>
public class PortalModel : PageModel
{
    /// <summary>URL'deki ?t= (GİRİŞ token'ı). Boş olabilir; doğrulama istemci + PortalController tarafında yapılır.</summary>
    public string Token { get; private set; } = "";

    public void OnGet(string? t) => Token = t ?? "";
}
