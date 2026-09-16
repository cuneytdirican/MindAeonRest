using System.Windows;

namespace MindAeonRest.Helpers;

public enum AppTheme { Dark, Light, Ocean }

public record ThemeOption(AppTheme Value, string Label);

/// <summary>
/// Uygulama temasını yönetir. Palet sözlüğü App.Resources.MergedDictionaries[0]'da tutulur;
/// tema değişince [0] değiştirilir ve pencere yeniden oluşturulur (StaticResource'lar tazelensin).
/// Tercih login.json içinde saklanır.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<AppTheme, (string Uri, string Label)> Map = new()
    {
        [AppTheme.Dark]  = ("Themes/DarkTheme.xaml",  "Koyu"),
        [AppTheme.Light] = ("Themes/LightTheme.xaml", "Açık"),
        [AppTheme.Ocean] = ("Themes/OceanTheme.xaml", "Okyanus"),
    };

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static List<ThemeOption> Options =>
        Map.Select(kv => new ThemeOption(kv.Key, kv.Value.Label)).ToList();

    // Açık assembly-nitelikli pack URI (giriş assembly'sinden bağımsız, sağlam)
    private static Uri Pack(string rel) => new($"pack://application:,,,/MindAeonRest;component/{rel}");

    public static void Apply(AppTheme theme)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = Pack(Map[theme].Uri) };

        // POS paleti ([2]): koyu temada koyu POS, diğerlerinde açık POS
        var md = Application.Current.Resources.MergedDictionaries;
        if (md.Count > 2)
            md[2] = new ResourceDictionary { Source = Pack(theme == AppTheme.Dark ? "Themes/PosDarkTheme.xaml" : "Themes/PosTheme.xaml") };
        Current = theme;
    }

    /// <summary>Kayıtlı temayı uygular (başlangıçta çağrılır).</summary>
    public static void LoadSaved()
    {
        var prefs = LoginPreferences.Load();
        Apply(Enum.TryParse<AppTheme>(prefs.Theme, out var t) ? t : AppTheme.Dark);
    }

    /// <summary>Temayı uygular ve tercihi kaydeder.</summary>
    public static void ChangeAndSave(AppTheme theme)
    {
        Apply(theme);
        var prefs = LoginPreferences.Load();
        prefs.Theme = theme.ToString();
        prefs.Save();
    }
}
