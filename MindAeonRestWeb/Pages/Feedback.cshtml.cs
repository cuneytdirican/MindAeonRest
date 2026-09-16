using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindAeonRestWeb.Pages;

/// <summary>FAZ 5d — Geri bildirim / anket MÜŞTERİ sayfası (anonim, mobil-öncelikli, tam ekran). Müşteri anket
/// linkini (SMS) açınca <c>/feedback?t={token}</c> gelir. Sunucu token'ı DOĞRULAMAZ — yalnız kabuğu servis eder;
/// token istemci JS'ine verilir, tüm işlemler [AllowAnonymous] FeedbackController uçlarında ELLE doğrulanır.
/// HUNİ: yüksek puan → API Google yorum URL'i döndürür → istemci oraya yönlendirir; düşük puan yalnız kaydedilir.
/// Native alert/confirm YOK — durum/teşekkür sayfa içi custom bileşenle gösterilir.</summary>
public class FeedbackModel : PageModel
{
    /// <summary>URL'deki ?t= (anket token'ı). Boş olabilir; doğrulama istemci + FeedbackController tarafında yapılır.</summary>
    public string Token { get; private set; } = "";

    public void OnGet(string? t) => Token = t ?? "";
}
