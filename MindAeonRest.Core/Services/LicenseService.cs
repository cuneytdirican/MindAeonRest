using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Licensing;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>TERMİNAL (KIOSK) bazlı lisans: her makine kendi donanımına bağlı imzalı token ile etkinleşir;
/// pakete göre izin verilen terminal sayısı (MaxKiosks) etkinleştirmede denetlenir. Deneme şirket geneli (7 gün).
/// Lisanslar sistem DB'sinde dbo.Licenses'ta (CompanyCode, HardwareId) çok satır olarak saklanır.</summary>
public class LicenseService
{
    public const int TrialDays = 7;
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    /// <summary>Bu makinenin (terminal) güncel lisans durumu. Kendi lisansı yoksa şirket denemesine düşer.</summary>
    public LicenseStatus GetStatus(string companyCode, string hardwareId)
    {
        string? myKey = ReadKey(companyCode, hardwareId);

        // Legacy göç: eski tek-satır (HardwareId='') token'ı BU makineye aitse, satırı bu makineye taşı.
        if (string.IsNullOrWhiteSpace(myKey))
        {
            var legacy = ReadKey(companyCode, "");
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                var ll = LicenseCrypto.VerifyLicense(legacy!);
                if (ll is not null && ll.HardwareId.Equals(hardwareId, OIC))
                {
                    MoveLegacyRow(companyCode, hardwareId);
                    myKey = legacy;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(myKey))
        {
            var lic = LicenseCrypto.VerifyLicense(myKey!);
            if (lic is null) return Bad(LicenseState.Invalid);
            if (!lic.CompanyCode.Equals(companyCode, OIC)) return Bad(LicenseState.Invalid);
            if (!lic.HardwareId.Equals(hardwareId, OIC)) return Bad(LicenseState.WrongMachine);
            int max = EffectiveMaxKiosks(lic);
            var st = new LicenseStatus
            {
                State = lic.IsExpired ? LicenseState.Expired : LicenseState.Licensed,
                Package = lic.Package, Features = lic.Features,
                MaxBranches = lic.MaxBranches, MaxUsers = lic.MaxUsers,
                MaxKiosks = max, ActiveKiosks = CountActiveKiosks(companyCode),
                ExpiresUtc = lic.ExpiresUtc
            };
            st.Message = st.StateText;
            return st;
        }

        // Lisans yok → ŞİRKET DENEMESİ (tüm terminaller paylaşır). İlk kez ise başlat.
        DateTime? trialStart = ReadCompanyTrialStart(companyCode);
        if (trialStart is null) { trialStart = DateTime.UtcNow; StartCompanyTrial(companyCode, trialStart.Value); }
        int daysLeft = TrialDays - (int)(DateTime.UtcNow - trialStart.Value).TotalDays;
        var trial = new LicenseStatus
        {
            State = daysLeft > 0 ? LicenseState.Trial : LicenseState.TrialExpired,
            Package = LicensePackage.Lite, Features = PackageMatrix.Features(LicensePackage.Lite),
            MaxBranches = PackageMatrix.MaxBranches(LicensePackage.Lite), MaxUsers = PackageMatrix.MaxUsers(LicensePackage.Lite),
            MaxKiosks = PackageMatrix.MaxKiosks(LicensePackage.Lite), ActiveKiosks = CountActiveKiosks(companyCode),
            TrialDaysLeft = Math.Max(0, daysLeft)
        };
        trial.Message = trial.StateText;
        return trial;

        static LicenseStatus Bad(LicenseState s)
        {
            var st = new LicenseStatus { State = s, Package = LicensePackage.Lite, Features = Feature.None, MaxBranches = 0, MaxUsers = 0, MaxKiosks = 0 };
            st.Message = st.StateText;
            return st;
        }
    }

    /// <summary>MERKEZÎ (web/SaaS) lisans durumu: donanım bağı YOK. Şirketin (herhangi terminaldeki) en iyi
    /// geçerli lisansını esas alır → paket/özellik. Web'de tek cloud host tüm kiracılarla aynı HardwareId'yi
    /// paylaştığından terminal-lisans anlamsız; bu metot yalnız CompanyCode ile paket/özellik çözer.</summary>
    public LicenseStatus GetStatusByCompany(string companyCode)
    {
        LicensePayload? best = null;
        using (var c = Database.OpenConnection())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT LicenseKey FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId<>'' AND LicenseKey IS NOT NULL;";
            cmd.Parameters.AddWithValue("@c", companyCode);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var lic = LicenseCrypto.VerifyLicense(r.GetString(0));
                if (lic is null || !lic.CompanyCode.Equals(companyCode, OIC)) continue;
                // En iyi: önce süresi geçmemiş, sonra daha yüksek paket.
                if (best is null || (best.IsExpired && !lic.IsExpired) || (best.IsExpired == lic.IsExpired && lic.Package > best.Package))
                    best = lic;
            }
        }

        if (best is not null)
        {
            var st = new LicenseStatus
            {
                State = best.IsExpired ? LicenseState.Expired : LicenseState.Licensed,
                Package = best.Package, Features = best.Features,
                MaxBranches = best.MaxBranches, MaxUsers = best.MaxUsers,
                MaxKiosks = EffectiveMaxKiosks(best), ActiveKiosks = CountActiveKiosks(companyCode),
                ExpiresUtc = best.ExpiresUtc
            };
            st.Message = st.StateText;
            return st;
        }

        // Lisans yok → şirket denemesi (tüm terminaller paylaşır) / Lite.
        DateTime? trialStart = ReadCompanyTrialStart(companyCode);
        if (trialStart is null) { trialStart = DateTime.UtcNow; StartCompanyTrial(companyCode, trialStart.Value); }
        int daysLeft = TrialDays - (int)(DateTime.UtcNow - trialStart.Value).TotalDays;
        var trial = new LicenseStatus
        {
            State = daysLeft > 0 ? LicenseState.Trial : LicenseState.TrialExpired,
            Package = LicensePackage.Lite, Features = PackageMatrix.Features(LicensePackage.Lite),
            MaxBranches = PackageMatrix.MaxBranches(LicensePackage.Lite), MaxUsers = PackageMatrix.MaxUsers(LicensePackage.Lite),
            MaxKiosks = PackageMatrix.MaxKiosks(LicensePackage.Lite), ActiveKiosks = CountActiveKiosks(companyCode),
            TrialDaysLeft = Math.Max(0, daysLeft)
        };
        trial.Message = trial.StateText;
        return trial;
    }

    /// <summary>Bu makineye ait TALEP anahtarı (bayi/portal bunu lisansa çevirir). dealerCode opsiyonel — portal lisansı bu bayiye atfeder.</summary>
    public string BuildRequestKey(string companyCode, LicensePackage requestedPackage, string? dealerCode = null) =>
        LicenseCrypto.EncodeRequest(new RequestPayload
        {
            CompanyCode = companyCode,
            HardwareId = HardwareId.Current,
            RequestedPackage = requestedPackage,
            DealerCode = string.IsNullOrWhiteSpace(dealerCode) ? null : dealerCode.Trim(),
            AppVersion = "7.0"
        });

    /// <summary>Lisans anahtarını BU terminal için etkinleştirir. Terminal limiti (MaxKiosks) aşılırsa engeller.</summary>
    public LicenseStatus Activate(string companyCode, string licenseKey, string hardwareId)
    {
        var lic = LicenseCrypto.VerifyLicense(licenseKey ?? "");
        if (lic is null) throw new InvalidOperationException("Geçersiz lisans anahtarı. Lütfen tam metni yapıştırın.");
        if (!lic.CompanyCode.Equals(companyCode, OIC))
            throw new InvalidOperationException($"Bu lisans bu şirkete ait değil (lisans şirket kodu: {lic.CompanyCode}).");
        if (!lic.HardwareId.Equals(hardwareId, OIC))
            throw new InvalidOperationException("Bu lisans bu makineye (terminale) ait değil. Talep anahtarını BU makinede üretip bize iletin.");
        if (lic.IsExpired)
            throw new InvalidOperationException($"Lisansın süresi dolmuş (bitiş: {lic.ExpiresUtc:dd.MM.yyyy}).");

        // Terminal limiti: bu makine zaten kayıtlı değilse ve diğer aktif terminaller limiti dolduysa engelle.
        int max = EffectiveMaxKiosks(lic);
        bool alreadyThis = !string.IsNullOrWhiteSpace(ReadKey(companyCode, hardwareId));
        int otherActive = CountActiveKiosks(companyCode, exclude: hardwareId);
        if (!alreadyThis && otherActive >= max)
            throw new InvalidOperationException(
                $"Bu paket en fazla {max} terminal (KIOSK) içindir. Şu an {otherActive} terminal kayıtlı. " +
                "Yeni terminal eklemek için birini kaldırın ya da paketi yükseltin.");

        UpsertKey(companyCode, hardwareId, licenseKey!);
        return GetStatus(companyCode, hardwareId);
    }

    /// <summary>Online heartbeat: BU terminalin lisansını sunucuda doğrula; İPTAL ise yerel anahtarı temizle.</summary>
    public async System.Threading.Tasks.Task OnlineHeartbeatAsync(string companyCode, string hardwareId)
    {
        if (!LicenceApiClient.IsConfigured) return;
        var key = ReadKey(companyCode, hardwareId);
        if (string.IsNullOrWhiteSpace(key)) return;
        var lic = LicenseCrypto.VerifyLicense(key!);
        if (lic is null) return;

        var res = await new LicenceApiClient().ValidateAsync(lic.LicenseId, hardwareId);
        if (res.Status == "Revoked") ClearKey(companyCode, hardwareId);
    }

    // ===== Terminal (KIOSK) yönetimi =====

    /// <summary>Bu şirketin lisanslı terminalleri (yönetim ekranı için).</summary>
    public List<KioskLicenseInfo> ListKiosks(string companyCode)
    {
        var list = new List<KioskLicenseInfo>();
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT HardwareId, LicenseKey, UpdatedUtc FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId<>'' AND LicenseKey IS NOT NULL ORDER BY UpdatedUtc;";
        cmd.Parameters.AddWithValue("@c", companyCode);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var lic = LicenseCrypto.VerifyLicense(r.GetString(1));
            list.Add(new KioskLicenseInfo
            {
                HardwareId = r.GetString(0),
                Package = lic?.Package ?? LicensePackage.Lite,
                ExpiresUtc = lic?.ExpiresUtc,
                ActivatedUtc = r.IsDBNull(2) ? null : r.GetDateTime(2),
                Valid = lic is not null && !lic.IsExpired && lic.HardwareId.Equals(r.GetString(0), OIC)
            });
        }
        return list;
    }

    /// <summary>Bir terminalin lisansını kaldırır (slot boşaltma).</summary>
    public void DeactivateKiosk(string companyCode, string hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId)) return;
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId=@h AND HardwareId<>'';";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@h", hardwareId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Bu şirkette aktif (lisans anahtarı olan) terminal sayısı.</summary>
    public int CountActiveKiosks(string companyCode, string? exclude = null)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId<>'' AND LicenseKey IS NOT NULL AND (@x IS NULL OR HardwareId<>@x);";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@x", (object?)exclude ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    // ===== Yardımcılar =====
    private static int EffectiveMaxKiosks(LicensePayload lic) =>
        lic.MaxKiosks > 0 ? lic.MaxKiosks : PackageMatrix.MaxKiosks(lic.Package);

    private static string? ReadKey(string companyCode, string hardwareId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT LicenseKey FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId=@h;";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@h", hardwareId);
        return cmd.ExecuteScalar() as string;
    }

    private static DateTime? ReadCompanyTrialStart(string companyCode)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MIN(TrialStartUtc) FROM dbo.Licenses WHERE CompanyCode=@c AND TrialStartUtc IS NOT NULL;";
        cmd.Parameters.AddWithValue("@c", companyCode);
        var o = cmd.ExecuteScalar();
        return o is DateTime dt ? dt : null;
    }

    private static void StartCompanyTrial(string companyCode, DateTime startUtc)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        // Deneme şirket-geneli: HardwareId='' satırında tutulur (yoksa oluştur).
        cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId='')
    UPDATE dbo.Licenses SET TrialStartUtc=@t, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId='';
ELSE
    INSERT INTO dbo.Licenses (CompanyCode, HardwareId, TrialStartUtc) VALUES (@c, '', @t);";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@t", startUtc);
        cmd.ExecuteNonQuery();
    }

    private static void UpsertKey(string companyCode, string hardwareId, string licenseKey)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId=@h)
    UPDATE dbo.Licenses SET LicenseKey=@k, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId=@h;
ELSE
    INSERT INTO dbo.Licenses (CompanyCode, HardwareId, LicenseKey) VALUES (@c, @h, @k);";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@h", hardwareId);
        cmd.Parameters.AddWithValue("@k", licenseKey);
        cmd.ExecuteNonQuery();
    }

    private static void ClearKey(string companyCode, string hardwareId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Licenses SET LicenseKey=NULL, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId=@h;";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@h", hardwareId);
        cmd.ExecuteNonQuery();
    }

    private static void MoveLegacyRow(string companyCode, string hardwareId)
    {
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        // '' satırını bu makineye taşı (PK çakışması olmaz: bu makinenin satırı yoktu).
        cmd.CommandText = "UPDATE dbo.Licenses SET HardwareId=@h, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId='';";
        cmd.Parameters.AddWithValue("@c", companyCode);
        cmd.Parameters.AddWithValue("@h", hardwareId);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Bir terminalin lisans özeti (yönetim ekranı).</summary>
public class KioskLicenseInfo
{
    public string HardwareId { get; set; } = "";
    public LicensePackage Package { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? ActivatedUtc { get; set; }
    public bool Valid { get; set; }
    public string PackageName => PackageMatrix.Name(Package);
    public string ExpiresText => ExpiresUtc is { } e ? e.ToLocalTime().ToString("dd.MM.yyyy") : "süresiz";
    public string StatusText => Valid ? "Aktif" : "Geçersiz/Süresi dolmuş";
}
