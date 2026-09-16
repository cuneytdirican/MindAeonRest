using MindAeonRest.Licensing;

namespace MindAeonRest.Helpers;

/// <summary>Lisans savunma derinliği: modül kapısı. MainWindow.ApplyLicense ile AYNI API'yi kullanır
/// (Session.CurrentLicense.Has(Feature) / Session.LicenseHas(Feature)) — yeni izin sistemi icat etmez.</summary>
public static class LicenseGuard
{
    /// <summary>Bu özellik (modül) aktif lisans pakette açık mı? (lisans bağlamı yoksa false).</summary>
    public static bool Has(Feature f) => Session.CurrentLicense?.Has(f) ?? false;

    /// <summary>Modül lisansta yoksa kullanıcıyı uyarır ve false döner; varsa true.</summary>
    public static bool Require(Feature f, string moduleName)
    {
        if (Has(f)) return true;
        Dialog.Warn($"{moduleName} modülü lisansınızda yok.", "Lisans");
        return false;
    }
}
