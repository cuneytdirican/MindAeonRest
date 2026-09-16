using System.Text.RegularExpressions;

namespace MindAeonRest.Helpers;

/// <summary>
/// Ortak CSV alan güvenliği (BULGU 11): CSV/formül (DDE) ENJEKSİYONU nötrleştirme + ayraç/tırnak/yeni-satır kaçışı.
/// Tüm CSV export'ları (muhasebe aktarım + rapor export'ları) bu tek yardımcıdan geçmeli.
/// </summary>
public static class CsvHelper
{
    // Excel/Logo/Mikro'da bu karakterlerle BAŞLAYAN hücre formül/komut olarak yürüyebilir (=HYPERLINK, =cmd|..., +/-/@/DDE).
    private static readonly char[] FormulaTriggers = { '=', '+', '-', '@', '\t', '\r' };

    // Saf sayı (opsiyonel işaret + rakamlar + . , ayraçları). Tutar/negatif net kolonları nötrleştirilMEZ ki
    // harici muhasebe programı bunları SAYISAL içe-aktarabilsin (aksi halde metne dönerdi = regresyon).
    private static readonly Regex PlainNumber = new(@"^[+-]?[0-9][0-9.,]*$", RegexOptions.Compiled);

    /// <summary>
    /// Kullanıcı-denetimli metni güvenli CSV hücresine çevirir:
    /// (1) formül tetikleyici karakterle başlıyorsa VE saf sayı değilse başına tek tırnak ('), SONRA
    /// (2) ayraç/tırnak/yeni-satır içeriyorsa tırnakla kaçış. Sıra önemli: önce nötrleştir, sonra kaçış.
    /// </summary>
    public static string Field(string? s, char separator = ';')
    {
        s ??= "";
        if (s.Length > 0 && Array.IndexOf(FormulaTriggers, s[0]) >= 0 && !PlainNumber.IsMatch(s))
            s = "'" + s;   // formül/DDE nötrleştirme — Excel/Logo/Mikro'da düz METİN olarak kalır
        if (s.IndexOf(separator) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
