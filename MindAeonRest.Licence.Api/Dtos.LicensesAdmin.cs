using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

// ===== Endpoints/licenses-ext: Admin lisans yönetimi DTO'ları =====
// (mevcut Dtos.cs'e DOKUNMADAN ayrı dosya — çakışma yasağı)

/// <summary>Liste filtreleri (hepsi opsiyonel) — paket/durum/işletme/bayi.</summary>
public record LicenseListQuery(int? Package, LicenseStatus? Status, int? BusinessId, int? DealerId);

/// <summary>Liste satırı (özet) — token gönderilmez (gereksiz büyük + hassas).</summary>
public record LicenseListItem(
    int Id,
    string LicenseCode,
    int Package,
    string PackageName,
    LicenseStatus Status,
    int? DealerId,
    string? DealerName,
    int BusinessId,
    string? BusinessTitle,
    string CompanyCode,
    string HardwareId,
    int MaxBranches,
    int MaxUsers,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc);

/// <summary>Lisans yükseltme — yeni paket için yeni imzalı token üretilir; eski lisans Upgraded olur.</summary>
public record UpgradeLicenseReq(int Package, DateTime? ExpiresUtc, string? Note,
    long AddOnFeatures = 0, int ExtraUsers = 0, int ExtraKiosks = 0, int ExtraBranches = 0);

/// <summary>Yükseltme sonucu — yeni lisans + token.</summary>
public record UpgradeLicenseResp(
    int NewLicenseId,
    string LicenseCode,
    string Token,
    int Package,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc);

/// <summary>Yenileme — sadece bitiş tarihi uzatılır; token yeni tarihle yeniden imzalanır.</summary>
public record RenewLicenseReq(DateTime? ExpiresUtc, string? Note);

/// <summary>Yenileme sonucu — aynı lisans, güncel token + yeni bitiş.</summary>
public record RenewLicenseResp(int LicenseId, string LicenseCode, string Token, DateTime? ExpiresUtc);

/// <summary>İptal — lisans Revoked olur (heartbeat'te etki eder).</summary>
public record RevokeLicenseReq(string? Note);

/// <summary>İptal sonucu.</summary>
public record RevokeLicenseResp(int LicenseId, string LicenseCode, LicenseStatus Status);
