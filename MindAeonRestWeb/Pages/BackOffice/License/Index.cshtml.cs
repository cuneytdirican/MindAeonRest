using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.License;

/// <summary>
/// Lisans Durumu + AKTİVASYON (back-office). WPF LicenseWindow'un web karşılığı.
///
/// Durum kartı SALT-OKUNUR (kaynak: <see cref="LicenseService.GetStatusByCompany"/>).
/// Aktivasyon:
///   • OFFLINE — üretici/bayiden gelen imzalı MAR-LIC anahtarını yapıştır → <see cref="LicenseCrypto.VerifyLicense"/>
///     ile imza+şirket doğrula → sistem DB dbo.Licenses'a yaz (MakeDemoLicense UPSERT deseni). Tam çalışır.
///   • ONLINE — Bayi kodu + paket ile talep anahtarı üret (<see cref="LicenseService.BuildRequestKey"/>) →
///     <see cref="LicenceApiClient"/> ile portala gönder; "issued" dönerse token'ı aynı şekilde kaydet.
///     Yalnız AppConfig.LicenceApiUrl tanımlıysa (LicenceApiClient.IsConfigured) etkin.
///
/// GÜVENLİK: RSA ÖZEL anahtara dokunulmaz; yalnız gömülü GENEL anahtarla doğrulama (VerifyLicense) yapılır.
/// Kapı: Session.IsTenantAdmin (SysAdmin/Patron/Yönetici). Tüm işlemler Session.CurrentCompany.Code ile kapsamlıdır ve
/// token imzadaki şirket koduyla eşleşmek zorundadır → kiracı yöneticisi yalnız KENDİ şirketinin lisansını görür/etkinleştirir.
/// </summary>
public class IndexModel : PageModel
{
    private readonly LicenseService _svc = new();

    public LicenseStatus? Status { get; private set; }
    public string CompanyName { get; private set; } = "";
    public string CompanyCode { get; private set; } = "";
    public string? Error { get; private set; }

    /// <summary>Aktivasyon (offline/online) sonucu — PRG mesajı.</summary>
    public string? ActivationMessage { get; private set; }
    /// <summary>Aktivasyon (offline/online) hatası — PRG mesajı.</summary>
    public string? ActivationError { get; private set; }

    /// <summary>Üretilen talep anahtarı (offline) — kullanıcı seçip kopyalar. WPF RequestBox karşılığı.</summary>
    public string? RequestKey { get; private set; }
    /// <summary>Talep anahtarı formunda seçili paket (POST sonrası korunur).</summary>
    public int RequestPackage { get; private set; } = (int)LicensePackage.Standart;
    /// <summary>Talep anahtarı formunda girilen bayi kodu (POST sonrası korunur).</summary>
    public string? RequestDealerCode { get; private set; }

    /// <summary>Online aktivasyon uygun mu (LicenceApiUrl tanımlı mı).</summary>
    public bool OnlineConfigured => LicenceApiClient.IsConfigured;

    /// <summary>Pakette açık modüller (Feature bit → Türkçe ad), çekirdek + add-on ayrımıyla.</summary>
    public List<FeatureRow> FeatureRows { get; private set; } = new();

    public sealed record FeatureRow(string Name, bool Enabled, bool Core);

    // Kiracı yöneticisi paketini görmeli; aktivasyon da yalnız aktif şirket koduna yazar (PersistLicenseToken şirket eşleşmesi).
    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        ActivationMessage = TempData["LicActivationMessage"] as string;
        ActivationError = TempData["LicActivationError"] as string;

        // Talep anahtarı üretimi PRG sonrası (OnPostBuildRequest) burada gösterilir.
        RequestKey = TempData["LicRequestKey"] as string;
        if (TempData["LicRequestPackage"] is int rp) RequestPackage = rp;
        RequestDealerCode = TempData["LicRequestDealer"] as string;

        return LoadAndPage();
    }

    /// <summary>OFFLINE talep anahtarı üretimi: paket (+ ops. bayi kodu) → <see cref="LicenseService.BuildRequestKey"/>.
    /// İnterneti olmayan müşteri üretilen talep anahtarını salt-okunur alandan kopyalayıp üreticiye iletir.
    /// GÜVENLİK: yalnız talep anahtarı (imzasız istek zarfı) üretilir; RSA özel anahtara dokunulmaz.</summary>
    public IActionResult OnPostBuildRequest(string? dealerCode, int package)
    {
        if (!CanAccess) return Forbid();

        var company = Session.CurrentCompany;
        if (company is null || string.IsNullOrWhiteSpace(company.Code))
        {
            TempData["LicActivationError"] = "Aktif şirket bulunamadı.";
            return RedirectToPage();
        }

        try
        {
            var pkg = Enum.IsDefined(typeof(LicensePackage), package) ? (LicensePackage)package : LicensePackage.Standart;
            var requestKey = _svc.BuildRequestKey(company.Code, pkg, dealerCode);

            TempData["LicRequestKey"] = requestKey;
            TempData["LicRequestPackage"] = (int)pkg;
            TempData["LicRequestDealer"] = string.IsNullOrWhiteSpace(dealerCode) ? null : dealerCode.Trim();
        }
        catch (Exception ex)
        {
            TempData["LicActivationError"] = "Talep anahtarı üretilemedi: " + ex.Message;
        }

        return RedirectToPage();
    }

    /// <summary>OFFLINE aktivasyon: yapıştırılan VEYA .lic dosyadan yüklenen MAR-LIC anahtarını doğrula → sistem DB'ye yaz.
    /// Dosya yolu WPF LoadFile_Click karşılığı: IFormFile (.lic/.txt) içeriği okunup aynı doğrulama+kaydetme akışına verilir.
    /// Öncelik yapıştırılan metindedir; boşsa dosya kullanılır.</summary>
    public async Task<IActionResult> OnPostOfflineActivateAsync(string? licenseKey, IFormFile? licenseFile)
    {
        if (!CanAccess) return Forbid();

        var company = Session.CurrentCompany;
        if (company is null || string.IsNullOrWhiteSpace(company.Code))
        {
            TempData["LicActivationError"] = "Aktif şirket bulunamadı.";
            return RedirectToPage();
        }

        try
        {
            var token = (licenseKey ?? "").Trim();

            // Yapıştırılan metin boşsa .lic/.txt dosyasından oku (WPF "Dosyadan Yükle" karşılığı).
            if (token.Length == 0 && licenseFile is { Length: > 0 })
            {
                using var reader = new StreamReader(licenseFile.OpenReadStream());
                token = (await reader.ReadToEndAsync()).Trim();
            }

            var st = PersistLicenseToken(company.Code, token);
            TempData["LicActivationMessage"] = $"Lisans etkinleştirildi. {st.StateText}";
        }
        catch (Exception ex)
        {
            TempData["LicActivationError"] = ex.Message;
        }

        return RedirectToPage();
    }

    /// <summary>ONLINE aktivasyon: bayi kodu + paket → talep anahtarı → portal → "issued" ise token'ı kaydet.
    /// AppConfig.LicenceApiUrl tanımlı değilse pasif.</summary>
    public async Task<IActionResult> OnPostOnlineActivateAsync(string? dealerCode, int package)
    {
        if (!CanAccess) return Forbid();

        var company = Session.CurrentCompany;
        if (company is null || string.IsNullOrWhiteSpace(company.Code))
        {
            TempData["LicActivationError"] = "Aktif şirket bulunamadı.";
            return RedirectToPage();
        }

        if (!LicenceApiClient.IsConfigured)
        {
            TempData["LicActivationError"] = "Online aktivasyon yapılandırılmamış (Lisans API adresi tanımlı değil). Lütfen offline anahtar kullanın.";
            return RedirectToPage();
        }

        try
        {
            var pkg = Enum.IsDefined(typeof(LicensePackage), package) ? (LicensePackage)package : LicensePackage.Standart;
            var requestKey = _svc.BuildRequestKey(company.Code, pkg, dealerCode);

            var res = await new LicenceApiClient().ActivateAsync(requestKey);
            if (res.Status == "issued" && !string.IsNullOrWhiteSpace(res.Token))
            {
                var st = PersistLicenseToken(company.Code, res.Token);
                TempData["LicActivationMessage"] = $"Lisans online etkinleştirildi. {st.StateText}";
            }
            else if (res.Status == "pending")
            {
                TempData["LicActivationMessage"] = res.Message
                    ?? "Lisans talebiniz alındı. Onaylandığında tekrar 'Online Etkinleştir' deyin.";
            }
            else
            {
                TempData["LicActivationError"] = res.Message
                    ?? "Online aktivasyon başarısız. Offline (kopyala-yapıştır) yöntemini kullanabilirsiniz.";
            }
        }
        catch (Exception ex)
        {
            TempData["LicActivationError"] = "Online hata: " + ex.Message;
        }

        return RedirectToPage();
    }

    /// <summary>İmzalı MAR-LIC token'ı doğrula → şirkete ait olduğunu denetle → sistem DB dbo.Licenses'a yaz.
    /// Yalnız GENEL anahtarla doğrulama (VerifyLicense); imza geçersizse "imza geçersiz" hatası.</summary>
    private LicenseStatus PersistLicenseToken(string companyCode, string? licenseKey)
    {
        var token = (licenseKey ?? "").Trim();
        if (token.Length == 0)
            throw new InvalidOperationException("Lisans anahtarını yapıştırın.");

        var lic = LicenseCrypto.VerifyLicense(token);
        if (lic is null)
            throw new InvalidOperationException("İmza geçersiz. Anahtarın tam metnini (MAR-LIC. ile başlayan) yapıştırın.");

        if (!lic.CompanyCode.Equals(companyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Bu lisans bu şirkete ait değil (lisans şirket kodu: {lic.CompanyCode}).");

        if (lic.IsExpired)
            throw new InvalidOperationException($"Lisansın süresi dolmuş (bitiş: {lic.ExpiresUtc:dd.MM.yyyy}).");

        // Sistem DB dbo.Licenses'a yaz — MakeDemoLicense/Program.cs UPSERT deseni.
        // HardwareId = token'ın kendi donanım kimliği (web merkezî host; GetStatusByCompany HardwareId<>'' satırları okur).
        var hardwareId = string.IsNullOrWhiteSpace(lic.HardwareId) ? "WEB" : lic.HardwareId;
        using (var c = Database.OpenConnection())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId=@h)
    UPDATE dbo.Licenses SET LicenseKey=@k, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId=@h;
ELSE
    INSERT INTO dbo.Licenses (CompanyCode, HardwareId, LicenseKey) VALUES (@c, @h, @k);";
            cmd.Parameters.AddWithValue("@c", companyCode);
            cmd.Parameters.AddWithValue("@h", hardwareId);
            cmd.Parameters.AddWithValue("@k", token);
            cmd.ExecuteNonQuery();
        }

        // Yeni durum.
        return _svc.GetStatusByCompany(companyCode);
    }

    private IActionResult LoadAndPage()
    {
        var company = Session.CurrentCompany;
        if (company is null || string.IsNullOrWhiteSpace(company.Code))
        {
            Error = "Aktif şirket bulunamadı.";
            return Page();
        }

        CompanyCode = company.Code;
        CompanyName = company.Name ?? company.Code;

        try
        {
            Status = _svc.GetStatusByCompany(company.Code);
            BuildFeatureRows(Status);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    private void BuildFeatureRows(LicenseStatus status)
    {
        var rows = new List<FeatureRow>();

        // Çekirdek modüller — her pakette daima açık.
        foreach (Feature f in EnumFeatures(PackageMatrix.CoreSet))
            rows.Add(new FeatureRow(PackageMatrix.FeatureName(f), status.Has(f), Core: true));

        // Add-on modüller — pakete göre açık/kapalı.
        foreach (var f in PackageMatrix.AddOnModules)
            rows.Add(new FeatureRow(PackageMatrix.FeatureName(f), status.Has(f), Core: false));

        FeatureRows = rows;
    }

    /// <summary>Bir bit maskesindeki tek tek Feature bayraklarını (None hariç) döndürür.</summary>
    private static IEnumerable<Feature> EnumFeatures(Feature set)
    {
        foreach (Feature f in Enum.GetValues(typeof(Feature)))
            if (f != Feature.None && set.HasFlag(f))
                yield return f;
    }

    // ===== Görünüm yardımcıları =====

    public static string LimitText(int v) => v == int.MaxValue ? "sınırsız" : v.ToString();

    public string ExpiresText => Status?.ExpiresUtc is { } e
        ? e.ToLocalTime().ToString("dd.MM.yyyy")
        : "süresiz";

    /// <summary>Durum rozeti için CSS renk sınıfı (yalnız iyi/uyarı/kötü ayrımı).</summary>
    public string StateCss => Status?.State switch
    {
        LicenseState.Licensed => "ok",
        LicenseState.Trial => "warn",
        _ => "bad"
    };
}
