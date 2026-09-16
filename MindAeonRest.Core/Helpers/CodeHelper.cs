using System.Text;

namespace MindAeonRest.Helpers;

/// <summary>Şirket kodu gibi alanları normalleştirir: BÜYÜK harf, boşluksuz, Türkçe karaktersiz, yalnızca A-Z/0-9.</summary>
public static class CodeHelper
{
    private static readonly Dictionary<char, char> TrMap = new()
    {
        ['ç'] = 'C', ['Ç'] = 'C',
        ['ğ'] = 'G', ['Ğ'] = 'G',
        ['ı'] = 'I', ['İ'] = 'I',
        ['ö'] = 'O', ['Ö'] = 'O',
        ['ş'] = 'S', ['Ş'] = 'S',
        ['ü'] = 'U', ['Ü'] = 'U',
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char ch in input.Trim())
        {
            char c = TrMap.TryGetValue(ch, out char mapped) ? mapped : char.ToUpperInvariant(ch);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Normalize edilmiş kod geçerli mi? (en az 1 karakter, yalnızca A-Z/0-9)</summary>
    public static bool IsValid(string? input)
    {
        string code = Normalize(input);
        return code.Length > 0;
    }
}
