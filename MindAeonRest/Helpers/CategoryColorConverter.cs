using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MindAeonRest.Helpers;

/// <summary>Kategori adını sabit canlı bir renk paletinden bir renge eşler (gruplar renkli/ayırt edilir).
/// Aynı ad → hep aynı renk; hem koyu hem açık temada okunur tonlar seçilmiştir.</summary>
public class CategoryColorConverter : IValueConverter
{
    private static readonly Color[] Palette =
    {
        (Color)ColorConverter.ConvertFromString("#4F9DDE")!, // mavi
        (Color)ColorConverter.ConvertFromString("#5DBB7E")!, // yeşil
        (Color)ColorConverter.ConvertFromString("#C77DD6")!, // mor
        (Color)ColorConverter.ConvertFromString("#E0A458")!, // amber
        (Color)ColorConverter.ConvertFromString("#E07A7A")!, // mercan
        (Color)ColorConverter.ConvertFromString("#46C9C3")!, // turkuaz
        (Color)ColorConverter.ConvertFromString("#E8923E")!, // turuncu
        (Color)ColorConverter.ConvertFromString("#8E97E8")!, // indigo
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        int h = 0;
        foreach (var ch in s) h = (h * 31 + ch) & 0x7fffffff;
        return new SolidColorBrush(Palette[h % Palette.Length]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
