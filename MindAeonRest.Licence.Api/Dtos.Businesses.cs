using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

/// <summary>İşletme (Business) uç noktaları için DTO'lar. (Endpoints/businesses ajanı)</summary>

// ---- Liste / detay görünümleri ----
public record BusinessListItem(
    int Id,
    int? DealerId,
    string? DealerName,
    string CompanyCode,
    string Title,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId,
    int ActiveLicenseCount,
    DateTime CreatedUtc);

public record BusinessDetail(
    int Id,
    int? DealerId,
    string? DealerName,
    string CompanyCode,
    string Title,
    string? TaxOffice,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    IReadOnlyList<BusinessLicenseInfo> Licenses);

/// <summary>İşletmeye bağlı lisans + donanım/geçmiş özeti.</summary>
public record BusinessLicenseInfo(
    int LicenseId,
    string LicenseCode,
    int Package,
    int MaxBranches,
    int MaxUsers,
    string HardwareId,
    LicenseStatus Status,
    DateTime IssuedUtc,
    DateTime? ExpiresUtc,
    string? Notes,
    IReadOnlyList<BusinessLicenseHistoryInfo> History);

public record BusinessLicenseHistoryInfo(
    int Id,
    LicenseAction Action,
    int? FromPackage,
    int? ToPackage,
    string? Note,
    DateTime CreatedUtc);

// ---- Oluştur / güncelle ----
/// <summary>Yeni işletme. DealerId sadece Admin için anlamlı; Bayi kendi DealerId'sine bağlanır.</summary>
public record CreateBusinessReq(
    string CompanyCode,
    string Title,
    int? DealerId,
    string? TaxOffice,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId);

public record UpdateBusinessReq(
    string Title,
    int? DealerId,
    string? TaxOffice,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Phone,
    string? Email,
    string? HardwareId);

// ---- Müşteri (Customer) portal hesabı oluştur ----
/// <summary>Bir işletme için müşteri portal hesabı (Kind=Customer + BusinessId + rol Customer).</summary>
public record CreateBusinessCustomerReq(
    string Email,
    string Password,
    string? FullName);

public record CreateBusinessCustomerResp(
    string UserId,
    string Email,
    string FullName,
    int BusinessId);
