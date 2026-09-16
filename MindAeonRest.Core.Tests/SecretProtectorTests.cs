using MindAeonRest.Helpers;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// SecretProtector'ın Protect/Unprotect AES yolu, aktif şirket VERİTABANINDAN (dbo.AppSecretKeys)
/// anahtar okur → DB'siz test EDİLEMEZ. Burada yalnız DB'ye DOKUNMAYAN yollar test edilir:
/// biçim/ön-ek tespiti (v2:/enc:v1:), boş/null davranışı ve DB'ye erişmeden erken dönen hata yolları.
/// Not: gerçek Protect→Unprotect round-trip'i (AES-GCM anahtarı gerektirir) entegrasyon testine bırakıldı.
/// </summary>
public class SecretProtectorTests
{
    // ---- Protect: DB'ye dokunmadan erken dönen yollar ----

    [Fact]
    public void Protect_ReturnsNull_ForNull()
    {
        Assert.Null(SecretProtector.Protect(null));
    }

    [Fact]
    public void Protect_ReturnsEmpty_ForEmpty()
    {
        Assert.Equal(string.Empty, SecretProtector.Protect(string.Empty));
    }

    [Fact]
    public void Protect_ReturnsSameValue_WhenAlreadyV2Encrypted()
    {
        const string already = "v2:ABCDEFGabcdefg==";
        // Zaten şifreli → GetKey() (DB) ÇAĞRILMADAN aynen dönmeli.
        Assert.Same(already, SecretProtector.Protect(already));
    }

    [Fact]
    public void Protect_ReturnsSameValue_WhenAlreadyV1Encrypted()
    {
        const string already = "enc:v1:ABCDEFGabcdefg==";
        Assert.Same(already, SecretProtector.Protect(already));
    }

    // ---- Unprotect: DB'ye dokunmadan erken dönen yollar ----

    [Fact]
    public void Unprotect_ReturnsNull_ForNull()
    {
        Assert.Null(SecretProtector.Unprotect(null));
    }

    [Fact]
    public void Unprotect_ReturnsEmpty_ForEmpty()
    {
        Assert.Equal(string.Empty, SecretProtector.Unprotect(string.Empty));
    }

    [Fact]
    public void Unprotect_ReturnsPlaintextAsIs_WhenNoPrefix()
    {
        // Ön ek yok → eski DÜZ kayıt kabul edilir, aynen döner (DB'ye dokunmaz).
        const string plain = "duz-metin-secret";
        Assert.Equal(plain, SecretProtector.Unprotect(plain));
    }

    [Fact]
    public void Unprotect_ReturnsNull_ForV2WithTooShortPayload()
    {
        // "AAAA" base64 = 3 bayt < IvSize(12)+TagSize(16) → GetKey()'e (DB) ULAŞMADAN null döner.
        Assert.Null(SecretProtector.Unprotect("v2:AAAA"));
    }

    [Fact]
    public void Unprotect_ReturnsNull_ForV2WithInvalidBase64()
    {
        // Geçersiz base64 → FormatException yakalanır, GetKey()'e (DB) ULAŞMADAN null döner.
        Assert.Null(SecretProtector.Unprotect("v2:!!!not-base64!!!"));
    }

    [Fact]
    public void Unprotect_ReturnsNull_ForV1WithInvalidBase64()
    {
        // enc:v1: yolu DPAPI (DB'siz) kullanır; geçersiz base64 → null.
        Assert.Null(SecretProtector.Unprotect("enc:v1:!!!not-base64!!!"));
    }
}
