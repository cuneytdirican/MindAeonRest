using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

// ---- LicenseRequest (lisans/yükseltme talebi) DTO'ları ----
// NOTE: Bu dosya RequestsController'a özeldir; mevcut Dtos.cs'e DOKUNULMAZ.

/// <summary>Liste/detay görünümü (Admin Pending kuyruğu + Bayi kendi talepleri).</summary>
public record RequestListItem(
    int Id,
    int? BusinessId,
    int? DealerId,
    string CompanyCode,
    string HardwareId,
    int RequestedPackage,
    string RequestedPackageName,
    RequestStatus Status,
    int? IssuedLicenseId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

/// <summary>Bayi: yükseltme/lisans talebi gönder. Bayi lisans ÜRETEMEZ; sadece kuyruğa atar.</summary>
public record CreateRequestReq(
    int? BusinessId,
    string CompanyCode,
    string HardwareId,
    int RequestedPackage);

public record CreateRequestResp(int Id, RequestStatus Status, string Message);

/// <summary>Admin: talebi onayla → lisans üret. Süre seçenekleri opsiyonel (varsayılan perpetual).</summary>
public record ApproveRequestReq(
    DurationKind Duration = DurationKind.Perpetual,
    DateTime? ExpiresUtc = null,
    string? Notes = null,
    long AddOnFeatures = 0, int ExtraUsers = 0, int ExtraKiosks = 0, int ExtraBranches = 0);

public record ApproveRequestResp(
    int RequestId,
    int LicenseId,
    string LicenseCode,
    string Token,
    int Package,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc);

/// <summary>Admin: talebi reddet.</summary>
public record RejectRequestReq(string? Reason = null);

public record RejectRequestResp(int RequestId, RequestStatus Status);
