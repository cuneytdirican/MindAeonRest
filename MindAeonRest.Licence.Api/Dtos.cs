using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

public record LoginReq(string Email, string Password);
public record LoginResp(string Token, DateTime ExpiresUtc, string Role, string FullName, string Kind);

// Admin: bir işletme için lisans üret (imzala + kaydet)
public record IssueLicenseReq(int BusinessId, int Package, DurationKind Duration, DateTime? ExpiresUtc, string? Notes,
    long AddOnFeatures = 0, int ExtraUsers = 0, int ExtraKiosks = 0, int ExtraBranches = 0);
public record IssueLicenseResp(int LicenseId, string LicenseCode, string Token, int Package, DateTime IssuedUtc, DateTime? ExpiresUtc);

// Admin: MERKEZÎ web tenant'ına lisans ata (CompanyCode ile; işletme yoksa oluşturulur; HardwareId=WEB-CENTRAL).
// Yanıt IssueLicenseResp ile aynı: { licenseId, licenseCode, token, package, issuedUtc, expiresUtc }.
public record AssignTenantLicenseReq(string CompanyCode, string? CompanyName, int Package, DurationKind Duration, DateTime? ExpiresUtc,
    long AddOnFeatures = 0, int ExtraUsers = 0, int ExtraKiosks = 0, int ExtraBranches = 0, string? Notes = null);

// Masaüstü: talep anahtarıyla online aktivasyon
public record ActivateReq(string RequestToken);
public record ActivateResp(string Status, string? Token, string? Message);   // "issued" | "pending" | "none"

// Masaüstü: periyodik doğrulama (heartbeat) — iptal/expiry tespiti
public record ValidateReq(string LicenseCode, string HardwareId);
public record ValidateResp(string Status, DateTime? ExpiresUtc);             // Active | Revoked | Expired | Invalid (NotFound/WrongMachine numaralandırmayı önlemek için tek generik yanıt)

// Müşteri: kendi lisans token'ı (.lic indirme). Sadece oturum açan işletmenin lisansı döner.
public record MyLicenseTokenResp(int Id, string LicenseCode, string? LicenseToken);
