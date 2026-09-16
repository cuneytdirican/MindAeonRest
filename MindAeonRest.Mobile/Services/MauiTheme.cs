using Microsoft.Maui.Storage;
using MindAeonRest.Mobile.Resources.Styles;

namespace MindAeonRest.Mobile.Services;

public enum AppThemeKind { Light, Dark, Ocean }
public record ThemeOption(AppThemeKind Value, string Label, string Glyph);

/// <summary>MAUI çalışma-zamanı tema yöneticisi. Aktif paleti (Açık/Koyu/Okyanus) MergedDictionaries'te takas eder,
/// seçimi Preferences'ta saklar. V2 ekranları DynamicResource kullandığı için tema anında değişir.</summary>
public static class MauiTheme
{
    private const string PrefKey = "ui_theme";
    private static ResourceDictionary? _current;

    public static AppThemeKind Current { get; private set; } = AppThemeKind.Light;
    public static event Action? Changed;

    public static List<ThemeOption> Options => new()
    {
        new(AppThemeKind.Light, "Açık", "☀"),
        new(AppThemeKind.Dark, "Koyu", "🌙"),
        new(AppThemeKind.Ocean, "Okyanus", "🌊"),
    };

    public static string CurrentGlyph => Options.First(o => o.Value == Current).Glyph;
    public static string CurrentLabel => Options.First(o => o.Value == Current).Label;

    private static ResourceDictionary Build(AppThemeKind t) => t switch
    {
        AppThemeKind.Dark => new PaletteDark(),
        AppThemeKind.Ocean => new PaletteOcean(),
        _ => new PaletteLight(),
    };

    public static void Apply(AppThemeKind t)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        var md = res.MergedDictionaries;
        // Mevcut paleti (App.xaml'dakini veya önceki seçimi) tipine göre kaldır
        foreach (var d in md.Where(x => x is PaletteLight or PaletteDark or PaletteOcean).ToList())
            md.Remove(d);
        _current = Build(t);
        md.Add(_current);              // en sona eklenir → palet anahtarları kazanır
        Current = t;
        Changed?.Invoke();
    }

    public static void LoadSaved()
    {
        var saved = Preferences.Get(PrefKey, AppThemeKind.Light.ToString());
        Apply(Enum.TryParse<AppThemeKind>(saved, out var t) ? t : AppThemeKind.Light);
    }

    public static void ChangeAndSave(AppThemeKind t)
    {
        Apply(t);
        Preferences.Set(PrefKey, t.ToString());
    }

    /// <summary>Sıradaki temaya geçer (Açık→Koyu→Okyanus→Açık) ve saklar.</summary>
    public static void Cycle() => ChangeAndSave(Current switch
    {
        AppThemeKind.Light => AppThemeKind.Dark,
        AppThemeKind.Dark => AppThemeKind.Ocean,
        _ => AppThemeKind.Light,
    });
}
