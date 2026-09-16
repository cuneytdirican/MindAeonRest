namespace MindAeonRest.Models;

/// <summary>Adisyon ve Mutfak (KDS) ekranlarının sürümü.
/// "Varsayilan" = mevcut ekranlar; "V2" = yeni ekranlar. Şirket DB'sinde tek satır (Id=1).</summary>
public class CompanyUiSettings
{
    public string AdisyonVersion { get; set; } = UiVersions.Default;
    public string MutfakVersion { get; set; } = UiVersions.Default;

    public bool IsAdisyonV2 => UiVersions.IsV2(AdisyonVersion);
    public bool IsMutfakV2 => UiVersions.IsV2(MutfakVersion);
}

/// <summary>Ekran sürümü sabitleri.</summary>
public static class UiVersions
{
    public const string Default = "Varsayilan";
    public const string V2 = "V2";

    public static bool IsV2(string? value) => string.Equals(value, V2, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Geçersiz/boş değeri Varsayılan'a normalize eder.</summary>
    public static string Normalize(string? value) => IsV2(value) ? V2 : Default;
}
