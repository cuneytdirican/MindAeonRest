using System.Globalization;

namespace MindAeonRestWeb.Infrastructure;

/// <summary>
/// Kültürden BAĞIMSIZ, sağlam sayı ayrıştırma — fiyat/adet/miktar/iskonto/tutar gibi TÜM sayısal girişler için
/// tek doğru kaynak. Sorun: sayfa tr-TR olsa da HTML <c>type="number"</c> değeri InvariantCulture ('.' ondalık)
/// gönderir; kullanıcı ise elle "125,50" ya da "1.250,50" (tr) yazabilir. Tek bir kültürle ayrıştırmak bir
/// tarafı bozar (125.0000 → 1.250.000 ya da 1.250 → 1,25).
///
/// Kural: "SON gelen ayraç ONDALIKTIR, diğeri binliktir".
///  "125"        → 125         "125.0000" → 125        (type=number, tek nokta = ondalık)
///  "125,50"     → 125.50      "1.250,50" → 1250.50    (tr: nokta binlik, virgül ondalık)
///  "1,250.50"   → 1250.50     (en: virgül binlik, nokta ondalık)
/// Normalizasyondan sonra hiç binlik ayraç kalmaz; InvariantCulture ile ayrıştırılır.
/// </summary>
public static class Num
{
    /// <summary>Ham girdiyi InvariantCulture ondalık biçimine indirger (binlik ayraçları atar). Boşsa null.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace(" ", "").Replace(" ", "").Replace("₺", "").Replace("%", "");
        if (s.Length == 0) return null;

        bool hasDot = s.IndexOf('.') >= 0, hasComma = s.IndexOf(',') >= 0;
        if (hasDot && hasComma)
        {
            // İki ayraç: son gelen ondalık, diğeri binlik → binliği at, ondalığı '.' yap.
            if (s.LastIndexOf(',') > s.LastIndexOf('.')) s = s.Replace(".", "").Replace(',', '.');
            else s = s.Replace(",", "");
        }
        else if (hasComma)
        {
            // Yalnız virgül → tr ondalık.
            s = s.Replace(',', '.');
        }
        // yalnız nokta veya ayraçsız → zaten invariant ondalık (type=number) gibi.
        return s;
    }

    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        var s = Normalize(raw);
        return s is not null && decimal.TryParse(
            s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDouble(string? raw, out double value)
    {
        value = 0d;
        var s = Normalize(raw);
        return s is not null && double.TryParse(
            s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Geçerliyse değeri, değilse 0 döndürür (mevcut "hata = 0" davranışını koruyan yerler için).</summary>
    public static decimal ParseOrZero(string? raw) => TryParseDecimal(raw, out var v) ? v : 0m;
}
