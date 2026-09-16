using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Portal.Models;

/// <summary>"Lisans Ata" form girdisi (Tenants modalı + NewTenant formu ortak).</summary>
public sealed record TenantLicenseForm(
    int Package, DurationKind Duration, DateTime? ExpiresUtc,
    long AddOnFeatures, int ExtraUsers, int ExtraKiosks, int ExtraBranches, string? Note);

/// <summary>Atama sonucu. <see cref="IssuedButNotInstalled"/>: Licence API lisansı ÜRETTİ ama POS kurulumu başarısız.</summary>
public sealed record TenantLicenseAssignOutcome(
    bool Ok, string Message, string? LicenseCode, TenantLicenseStatus? Status, bool IssuedButNotInstalled);

/// <summary>
/// Admin → tenant lisans atama orkestrasyonu (iki güven sınırı):
///   1) Licence API <c>POST /api/v1/admin/tenant-licenses</c> (admin JWT) → imzalı token (WEB-CENTRAL; özel anahtar yalnız orada)
///   2) POS <c>POST /api/provision/tenant/{code}/license</c> (X-Provision-Key) → token'ı dbo.Licenses'a kurar.
/// Her adımın hatası AYRI ve açık mesajla döner; 1 başarılı + 2 başarısızsa bu durum açıkça söylenir
/// (tekrar "Lisans Ata" güvenlidir: Licence API önceki merkezî lisansı otomatik devre dışı bırakır).
/// </summary>
public static class TenantLicenseAssigner
{
    private sealed record AssignResp(int LicenseId, string LicenseCode, string Token, int Package, DateTime IssuedUtc, DateTime? ExpiresUtc);

    public static async Task<TenantLicenseAssignOutcome> AssignAsync(
        AdminApi api, PosProvisionClient pos, string code, string? name, TenantLicenseForm f, CancellationToken ct)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0)
            return Fail("Tenant kodu boş.");
        if (!Enum.IsDefined(typeof(LicensePackage), f.Package))
            return Fail("Geçersiz paket.");
        if (!pos.IsConfigured)
            return Fail("POS provisioning yapılandırılmamış (Pos:ProvisionKey boş); lisans kurulamaz.");

        var pkgName = PackageMatrix.Name((LicensePackage)f.Package);

        // Süresiz → bitiş yok; süreli → seçilen günün sonu (UTC) — Licenses sayfasıyla aynı normalizasyon.
        DateTime? expiresUtc = f.Duration == DurationKind.Perpetual ? null : ExpiryNormalizer.ToEndOfDayUtc(f.ExpiresUtc);
        if (f.Duration != DurationKind.Perpetual && expiresUtc is null)
            return Fail("Süreli (Aylık/Yıllık) lisans için bitiş tarihi seçin.");

        // 1) Licence API: üret + imzala
        var (issued, issueErr) = await api.PostAsync<AssignResp>("/api/v1/admin/tenant-licenses", new
        {
            companyCode = code,
            companyName = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            package = f.Package,
            duration = (int)f.Duration,
            expiresUtc,
            addOnFeatures = f.AddOnFeatures,
            extraUsers = Math.Max(0, f.ExtraUsers),
            extraKiosks = Math.Max(0, f.ExtraKiosks),
            extraBranches = Math.Max(0, f.ExtraBranches),
            notes = string.IsNullOrWhiteSpace(f.Note) ? null : f.Note.Trim()
        }, ct);
        if (issued is null || string.IsNullOrWhiteSpace(issued.Token))
            return Fail($"Lisans üretilemedi (Lisans API): {issueErr ?? "boş yanıt"}");

        // 2) POS: kur
        var (status, installErr) = await pos.InstallLicenseAsync(code, issued.Token, ct);
        if (status is null)
            return new(false,
                $"Lisans Lisans API'de üretildi (kod {issued.LicenseCode}, {pkgName}) ANCAK POS'a kurulamadı: {installErr ?? "boş yanıt"}. " +
                "Tenant hâlâ eski lisansıyla çalışıyor; sorunu giderip 'Lisans Ata' ile tekrar deneyin.",
                issued.LicenseCode, null, IssuedButNotInstalled: true);

        var msg = $"{code} → {pkgName} lisans atandı.";
        // POS en yüksek geçerli paketi seçer: şirketin daha yüksek paketli masaüstü terminal lisansı varsa etkin paket farklı olabilir.
        if (!string.IsNullOrWhiteSpace(status.Package) && !string.Equals(status.Package, pkgName, StringComparison.OrdinalIgnoreCase))
            msg += $" Uyarı: POS'ta etkin paket {status.Package} (şirketin başka geçerli terminal lisansı öncelikli).";
        return new(true, msg, issued.LicenseCode, status, false);

        static TenantLicenseAssignOutcome Fail(string m) => new(false, m, null, null, false);
    }
}
