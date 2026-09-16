using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRestWeb.Pages;

/// <summary>QR masadan-sipariş MÜŞTERİ sayfası (anonim, tam ekran, mobil-öncelikli). Müşteri masasındaki
/// QR'ı okutunca <c>/order?t={token}</c> açılır. Sunucu token'ı DOĞRULAMAZ — yalnız kabuğu (SPA) servis eder;
/// token istemci JS'ine verilir ve tüm veri/işlemler [AllowAnonymous] PublicController uçlarında (menü/sipariş/görsel)
/// token ELLE doğrulanarak yürür. Böylece şube+masa kapsamı sabittir, fiyat/sipariş SUNUCUDA doğrulanır (IDOR/fiyat
/// manipülasyonu imkânsız). Token boşsa sayfa "geçersiz bağlantı" ekranını gösterir (client tarafında).</summary>
public class OrderModel : PageModel
{
    /// <summary>URL'deki ?t= (public token). Boş olabilir; doğrulama istemci + PublicController tarafında yapılır.</summary>
    public string Token { get; private set; } = "";

    public void OnGet(string? t) => Token = t ?? "";
}
