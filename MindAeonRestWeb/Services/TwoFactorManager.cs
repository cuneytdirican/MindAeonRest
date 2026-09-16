using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Services;

/// <summary>
/// 2FA (TOTP) orkestrasyonu — web katmanı. Üç parçayı birleştirir:
///   • <see cref="TotpService"/> (RFC 6238 hesap, Core) — secret üret/URI/doğrula.
///   • <see cref="TwoFactorService"/> (SİSTEM DB erişimi, Core) — kolonları opak (şifreli) taşır.
///   • <see cref="IDataProtector"/> — secret'ı MASTER-seviye anahtarla şifreler/çözer.
///
/// <para><b>Neden IDataProtector (SecretProtector değil):</b> Kullanıcılar SİSTEM/master DB'sindedir; oysa
/// <c>SecretProtector</c> anahtarını AKTİF ŞİRKET DB'sinden (<c>Session.CurrentCompany</c>) okur — bu login
/// challenge anında henüz YOK ve master kullanıcı tek şirkete bağlı değildir. Web zaten kalıcı DataProtection
/// (App_Data/keys, sabit ApplicationName) yapılandırıyor → aynı süreç hem kayıt hem login'de aynı keyring'i görür;
/// app-pool geri dönüşümünde kırılmaz. Böylece ek config sırrı/anahtar dağıtımı gerekmez.</para>
///
/// <para><b>Kaba-kuvvet koruması:</b> login kod denemeleri kullanıcı bazlı sayaçla sınırlanır (IP bazlı
/// "login" rate-limit'e EK). Tüm hatalar tek-tip (enumerasyona kapalı).</para>
/// </summary>
public class TwoFactorManager(IDataProtectionProvider dp, IMemoryCache cache)
{
    private readonly IDataProtector _protector = dp.CreateProtector("MindAeonRest.TwoFactor.v1");
    private readonly TwoFactorService _store = new();

    private const int MaxCodeAttempts = 5;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);
    // Tek-kullanım penceresi: kod ±1 adım (2×30 sn) + küçük tolerans kadar kabul edilir; o süre boyunca replay'i engelle.
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromSeconds(90);

    public bool IsEnabled(int userId) => _store.IsEnabled(userId);

    // ===== Kayıt (enrollment) =====

    /// <summary>Yeni secret + QR içeriği üretir. HENÜZ KAYDETMEZ — çağıran, şifreli secret'ı onaya kadar
    /// (TempData/DataProtection) taşır; kullanıcı bir kod doğrulayınca <see cref="ConfirmEnrollment"/> ile yazılır.</summary>
    public (string SecretBase32, string ProtectedSecret, string OtpAuthUri) BeginEnrollment(string account)
    {
        string secret = TotpService.GenerateSecret();
        string uri = TotpService.BuildOtpAuthUri(account, secret);
        return (secret, _protector.Protect(secret), uri);
    }

    /// <summary>Bekleyen (şifreli) secret'tan QR içeriğini yeniden kurar — kayıt onayı hatalı kod ile
    /// başarısız olduğunda ekranı yeniden çizmek için. Çözülemezse null (yeni kayıt başlatılmalı).</summary>
    public (string SecretBase32, string OtpAuthUri)? DescribeProtected(string protectedSecret, string account)
    {
        string? secret = TryUnprotect(protectedSecret);
        return secret is null ? null : (secret, TotpService.BuildOtpAuthUri(account, secret));
    }

    /// <summary>Kayıt onayı: kullanıcının girdiği kodu bekleyen (henüz kaydedilmemiş) secret'a karşı doğrular;
    /// geçerliyse şifreli secret'ı yazıp 2FA'yı etkinleştirir. Dönüş: doğrulama başarılı mı.</summary>
    public bool ConfirmEnrollment(int userId, string protectedPendingSecret, string? code)
    {
        string? secret = TryUnprotect(protectedPendingSecret);
        if (secret is null || !TotpService.Verify(secret, code)) return false;
        _store.EnableWithSecret(userId, protectedPendingSecret, userId);
        return true;
    }

    /// <summary>2FA'yı devre dışı bırakır — YALNIZ geçerli bir kod ile (etkin kullanıcının kendisi).
    /// <see cref="VerifyLogin"/> ile: oturum çerezi ele geçirilse bile kod denemeleri kullanıcı bazlı 5/10dk
    /// kilidiyle sınırlanır (kaba-kuvvetle 2FA kapatma engellenir).</summary>
    public bool Disable(int userId, string? code)
    {
        if (!VerifyLogin(userId, code)) return false;
        _store.Disable(userId, userId);
        return true;
    }

    // ===== Login challenge =====

    /// <summary>Login TOTP doğrulaması — kaba-kuvvete karşı kullanıcı bazlı deneme kilidiyle.
    /// Kilitliyse ya da kod hatalıysa false (çağıran tek-tip hata gösterir).</summary>
    public bool VerifyLogin(int userId, string? code)
    {
        string key = $"2fa:attempts:{userId}";
        if (cache.TryGetValue<int>(key, out var fails) && fails >= MaxCodeAttempts)
            return false;

        if (VerifyCode(userId, code))
        {
            cache.Remove(key);
            return true;
        }

        int n = (cache.TryGetValue<int>(key, out var f) ? f : 0) + 1;
        cache.Set(key, n, AttemptWindow);
        return false;
    }

    /// <summary>Kayıtlı (şifreli) secret'ı çözüp kodu doğrular. Deneme sayacı TUTMAZ (VerifyLogin/Disable sarmalar);
    /// ancak tek-kullanım (replay) korumasını UYGULAR: aynı kullanıcı+zaman-adımı ikinci kez reddedilir.</summary>
    private bool VerifyCode(int userId, string? code)
    {
        var (enabled, prot) = _store.Get(userId);
        if (!enabled || string.IsNullOrEmpty(prot)) return false;
        string? secret = TryUnprotect(prot);
        if (secret is null) return false;

        long? step = TotpService.MatchStep(secret, code);
        if (step is null) return false;

        // RFC 6238 §5.2 tek-kullanım: geçerli kod (~90 sn boyunca kabul edilebilen zaman-adımı) yalnız BİR kez
        // kullanılabilir; aynı userId+step ikinci doğrulama denemesinde reddedilir (replay engellenir).
        string usedKey = $"2fa:used:{userId}:{step.Value}";
        if (cache.TryGetValue(usedKey, out _)) return false;
        cache.Set(usedKey, true, ReplayWindow);
        return true;
    }

    private string? TryUnprotect(string protectedValue)
    {
        try { return _protector.Unprotect(protectedValue); }
        catch { return null; }   // anahtar döndü/veri bozuldu → doğrulama başarısız (fail-closed)
    }
}
