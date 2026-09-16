using MindAeonRest.Licensing;

namespace MindAeonRest.Models;

public enum LicenseState { Licensed, Trial, TrialExpired, Expired, Invalid, WrongMachine }

/// <summary>Bir şirketin (DB) güncel lisans durumu — login'de hesaplanır, oturuma yazılır.</summary>
public class LicenseStatus
{
    public LicenseState State { get; set; }
    public LicensePackage Package { get; set; } = LicensePackage.Lite;
    public Feature Features { get; set; }
    public int MaxBranches { get; set; } = 1;
    public int MaxUsers { get; set; } = 3;
    public int MaxKiosks { get; set; } = 1;     // izin verilen terminal sayısı
    public int ActiveKiosks { get; set; }        // şu an etkin terminal sayısı (bu şirkette)
    public System.DateTime? ExpiresUtc { get; set; }
    public int TrialDaysLeft { get; set; }
    public string Message { get; set; } = "";

    /// <summary>Uygulama açılabilir mi? (lisanslı veya deneme devam ediyor)</summary>
    public bool IsUsable => State is LicenseState.Licensed or LicenseState.Trial;
    public bool Has(Feature f) => Features.HasFlag(f);
    public string PackageName => PackageMatrix.Name(Package);

    public string StateText => State switch
    {
        LicenseState.Licensed => ExpiresUtc is { } e ? $"Lisanslı · {PackageName} · bitiş {e.ToLocalTime():dd.MM.yyyy}" : $"Lisanslı · {PackageName} (süresiz)",
        LicenseState.Trial => $"DENEME · {TrialDaysLeft} gün kaldı (Lite)",
        LicenseState.TrialExpired => "Deneme süresi doldu — lisans gerekli",
        LicenseState.Expired => $"Lisans süresi dolmuş ({Package} ) — yenileme gerekli",
        LicenseState.WrongMachine => "Bu lisans başka bir makineye ait",
        _ => "Geçersiz lisans"
    };
}
