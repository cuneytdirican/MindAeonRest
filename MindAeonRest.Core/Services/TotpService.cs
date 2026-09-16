using System.Security.Cryptography;
using MindAeonRest.Helpers;

namespace MindAeonRest.Services;

/// <summary>
/// RFC 6238 TOTP (zaman-tabanlı tek-kullanımlık parola) — Google/Microsoft Authenticator ile uyumlu.
/// HMAC-SHA1, 30 sn adım, 6 hane. Doğrulamada ±1 pencere (saat kaymasına tolerans) sabit-zamanlı karşılaştırılır.
///
/// <para>Bu sınıf yalnız kriptografik hesaptır; secret'ı DÜZ tutar. Secret'ın <b>şifreli</b> saklanması ve
/// çözülmesi çağıran katmanın sorumluluğudur (web'de <c>IDataProtector</c> ile — bkz. TwoFactorManager).</para>
/// </summary>
public static class TotpService
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const int SecretBytes = 20;          // 160-bit — RFC 6238 önerisi (SHA-1)
    private const string Issuer = "MindAeonRest";

    /// <summary>Yeni rastgele TOTP secret (Base32, dolgusuz). Authenticator'a bu değer eklenir.</summary>
    public static string GenerateSecret()
        => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>Authenticator'ın okuyacağı <c>otpauth://</c> URI'si (QR içeriği). Etiket = "MindAeonRest:{account}".</summary>
    public static string BuildOtpAuthUri(string account, string secretBase32)
    {
        string label = Uri.EscapeDataString($"{Issuer}:{account}");
        string issuer = Uri.EscapeDataString(Issuer);
        string secret = secretBase32.Replace("=", "");
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    /// <summary>Girilen kodu, geçerli zaman penceresi ±<paramref name="window"/> için doğrular.
    /// Boş secret/kod → false. Karşılaştırma sabit-zamanlıdır (zamanlama sızıntısı yok).</summary>
    public static bool Verify(string? secretBase32, string? code, int window = 1)
        => MatchStep(secretBase32, code, window) is not null;

    /// <summary>Kodu doğrular ve EŞLEŞEN zaman adımını (Unix saniye / 30 sn) döner; eşleşme yoksa null.
    /// Çağıran bu adımı kısa süre saklayarak tek-kullanım (replay) koruması uygulayabilir (RFC 6238 §5.2):
    /// aynı kullanıcı+adım ikinci kez reddedilmelidir. Karşılaştırma sabit-zamanlıdır.</summary>
    public static long? MatchStep(string? secretBase32, string? code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (code.Length != Digits || !code.All(char.IsDigit)) return null;

        byte[] key = Base32.Decode(secretBase32);
        if (key.Length == 0) return null;

        long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / PeriodSeconds;
        for (int offset = -window; offset <= window; offset++)
        {
            long step = counter + offset;
            string candidate = Compute(key, step);
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(candidate),
                    System.Text.Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    /// <summary>HOTP (RFC 4226) — verilen sayaç için 6 haneli kod.</summary>
    private static string Compute(byte[] key, long counter)
    {
        byte[] counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes); // big-endian 8 bayt

        byte[] hash = HMACSHA1.HashData(key, counterBytes);
        int off = hash[^1] & 0x0F;
        int binary = ((hash[off] & 0x7F) << 24)
                   | ((hash[off + 1] & 0xFF) << 16)
                   | ((hash[off + 2] & 0xFF) << 8)
                   | (hash[off + 3] & 0xFF);
        int otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }
}
