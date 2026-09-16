using System.Text;

namespace MindAeonRest.Helpers;

/// <summary>
/// RFC 4648 Base32 kodlama/çözme (TOTP secret'ları için; Authenticator uygulamalarının beklediği alfabe).
/// Dolgu ('=') opsiyonel — çözerken yok sayılır; kodlarken üretilmez (secret alanı için gereksiz).
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Baytları Base32 metne çevirir (dolgusuz, büyük harf).</summary>
    public static string Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bitsLeft = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            sb.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    /// <summary>Base32 metni baytlara çevirir. Boşluk/dolgu yok sayılır; geçersiz karakter atlanır.</summary>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();
        var bytes = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (char raw in input)
        {
            char c = char.ToUpperInvariant(raw);
            if (c == '=' || c == ' ' || c == '-') continue;
            int val = Alphabet.IndexOf(c);
            if (val < 0) continue; // geçersiz karakteri yok say
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
