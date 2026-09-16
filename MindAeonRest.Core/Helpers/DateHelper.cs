using System.Globalization;

namespace MindAeonRest.Helpers;

/// <summary>gg.aa.yyyy biçiminde tarih ayrıştırma/biçimlendirme.</summary>
public static class DateHelper
{
    public const string Format = "dd.MM.yyyy";
    private static readonly CultureInfo Tr = new("tr-TR");

    public const string DateTimeFormat = "dd.MM.yyyy HH:mm";

    public static bool TryParse(string? text, out DateTime date)
        => DateTime.TryParseExact((text ?? string.Empty).Trim(), Format, Tr, DateTimeStyles.None, out date);

    public static string ToText(DateTime? date) => date?.ToString(Format) ?? string.Empty;

    /// <summary>"gg.aa.yyyy SS:dd" veya yalnızca "gg.aa.yyyy" (saat 00:00) kabul eder.</summary>
    public static bool TryParseDateTime(string? text, out DateTime value)
    {
        text = (text ?? string.Empty).Trim();
        if (DateTime.TryParseExact(text, DateTimeFormat, Tr, DateTimeStyles.None, out value)) return true;
        if (DateTime.TryParseExact(text, Format, Tr, DateTimeStyles.None, out value)) return true;
        return false;
    }

    public static string ToDateTimeText(DateTime? value) => value?.ToString(DateTimeFormat) ?? string.Empty;
}
