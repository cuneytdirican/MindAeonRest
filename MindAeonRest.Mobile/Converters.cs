using System.Globalization;

namespace MindAeonRest.Mobile;

/// <summary>Masa durumu (0 boş,1 dolu,2 rezerve) → kart arka plan rengi (POS açık tema).</summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int s = value is int i ? i : 0;
        return s switch
        {
            1 => Color.FromArgb("#E91E63"),  // dolu — pembe
            2 => Color.FromArgb("#6A1B9A"),  // rezerve — mor
            _ => Color.FromArgb("#E4E7EC")   // boş — açık gri
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Masa kartı metin rengi: boş masada koyu, dolu/rezervede beyaz.</summary>
public class StatusToTextColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is int i && i != 0) ? Colors.White : Color.FromArgb("#2B2B3A");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>String boş değilse true (görünürlük için).</summary>
public class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Gönderim zamanından (UTC) bu yana geçen süreye göre renk: ≤5dk yeşil, ≤10dk amber, >10dk kırmızı.</summary>
public class WaitColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime sent) return Color.FromArgb("#17B26A");
        var min = (DateTime.UtcNow - sent).TotalMinutes;
        return min <= 5 ? Color.FromArgb("#17B26A") : min <= 10 ? Color.FromArgb("#F0A500") : Color.FromArgb("#E5484D");
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Gönderim zamanından bu yana dakika metni (ör. "7 dk").</summary>
public class WaitTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime sent) return "";
        var min = (int)Math.Max(0, (DateTime.UtcNow - sent).TotalMinutes);
        return $"{min} dk";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tutar > 0 ise görünür metin, değilse boş.</summary>
public class AmountTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal a = value is decimal d ? d : 0m;
        return a > 0 ? $"₺{a:N2}" : "";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Satır durumu (0..4) → Türkçe metin.</summary>
public class LineStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int s = value is int i ? i : 0;
        return s switch { 1 => "Mutfakta", 2 => "✓ Hazır", 3 => "Teslim", 4 => "İptal", _ => "Bekliyor" };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Satır durumu → renk (hazır=yeşil, mutfakta=pembe, bekliyor=gri).</summary>
public class LineStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int s = value is int i ? i : 0;
        return s switch { 2 => Color.FromArgb("#17B26A"), 1 => Color.FromArgb("#E91E63"), _ => Color.FromArgb("#9AA0AB") };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
