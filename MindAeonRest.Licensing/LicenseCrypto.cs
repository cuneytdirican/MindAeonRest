using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MindAeonRest.Licensing;

/// <summary>Lisans token'ı imzalama (üretici) + doğrulama (müşteri). RSA-2048 dijital imza.
/// GENEL anahtar burada gömülü (müşteri doğrular); ÖZEL anahtar yalnız üretici araçta dosyadan yüklenir.</summary>
public static class LicenseCrypto
{
    // Genel anahtar (RSA public, XML'in Base64'ü). Müşteri yalnız DOĞRULAR, lisans üretemez.
    private const string PublicKeyXmlB64 =
        "PFJTQUtleVZhbHVlPjxNb2R1bHVzPjR5eGNzaTFsT2EvaHFsN3U5S0xMKzJscW82cXdleGFMbHprNzgxcnFtSzQwV2poOEEwOFZmY2xBM0lmQXdyMHJVd00zbjk5ZFhCcnVlVitRYVdIQnZhZmdOWUdNakxDZjEwYnZpUHZSTGFldVlVM1BuQTdGL3pTdk1vSURIRi9EdFNiQzlEWjFrU0hiYmtmSUFrT0E2ZzJsczg5ay81bFp6dmdOdHBpL0RiOGlTdVNkTFUxV1VMbFJkc1pmbFdqZ2pCbyttSEpPTHM3RGlWd3VmM2hLeFJ4ejRoaG43RUtVQTJyTW12NHJ5ZHlXMENROGt3QTFLYSt1YXNkdE9RRFJ4M3d1YzVWRWIyVEhLVzk0UEpHWXFCNWYvSDBCbFRmV1hOaWJOTDFmVjZteW0vZVVEdFRnTGlLbjA1WEZBZW9YQnJ5WEdBWWtJTHNoVjhVVmZKbzVkUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string PublicKeyXml => Encoding.UTF8.GetString(Convert.FromBase64String(PublicKeyXmlB64));

    // ---------- TALEP (müşteri → üretici), imzasız ----------
    public static string EncodeRequest(RequestPayload req) =>
        "MAR-REQ." + B64Url(JsonSerializer.SerializeToUtf8Bytes(req, Json));

    public static RequestPayload? DecodeRequest(string token)
    {
        try
        {
            token = (token ?? "").Trim();
            if (token.StartsWith("MAR-REQ.")) token = token["MAR-REQ.".Length..];
            return JsonSerializer.Deserialize<RequestPayload>(FromB64Url(token), Json);
        }
        catch { return null; }
    }

    // ---------- LİSANS (üretici → müşteri), imzalı ----------
    /// <summary>Üretici araçta: özel anahtarla imzalar. privateKeyXml dosyadan gelir.</summary>
    public static string SignLicense(LicensePayload lic, string privateKeyXml)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(lic, Json);
        using var rsa = RSA.Create();
        rsa.FromXmlString(privateKeyXml);
        var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return "MAR-LIC." + B64Url(data) + "." + B64Url(sig);
    }

    /// <summary>Müşteride: gömülü genel anahtarla imzayı doğrular. Geçerliyse payload döner.</summary>
    public static LicensePayload? VerifyLicense(string token)
    {
        try
        {
            token = (token ?? "").Trim();
            if (token.StartsWith("MAR-LIC.")) token = token["MAR-LIC.".Length..];
            var parts = token.Split('.');
            if (parts.Length != 2) return null;
            var data = FromB64Url(parts[0]);
            var sig = FromB64Url(parts[1]);
            using var rsa = RSA.Create();
            rsa.FromXmlString(PublicKeyXml);
            if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
            return JsonSerializer.Deserialize<LicensePayload>(data, Json);
        }
        catch { return null; }
    }

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
