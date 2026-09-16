using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

// ---- Bayi (Dealer) DTO'ları — yalnızca DealersController kullanır ----

/// <summary>Liste/özet satırı.</summary>
public record DealerListItem(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    bool IsActive,
    int BusinessCount,
    int LicenseCount,
    DateTime CreatedUtc);

/// <summary>Tek bayi detayı.</summary>
public record DealerDetail(
    int Id,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    bool IsActive,
    string? Notes,
    int BusinessCount,
    int LicenseCount,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

/// <summary>Bayi oluşturma isteği.</summary>
public record DealerCreateReq(
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    string? Notes,
    string? Code = null);

/// <summary>Bayi güncelleme isteği (marj/tahsilat/aktiflik dâhil).</summary>
public record DealerUpdateReq(
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    decimal MarginPercent,
    BillingMode BillingMode,
    bool IsActive,
    string? Notes);

/// <summary>Bayiye portal kullanıcı hesabı (giriş) oluşturma isteği.</summary>
public record DealerUserCreateReq(
    string Email,
    string Password,
    string? FullName);

/// <summary>Oluşturulan bayi kullanıcısı yanıtı.</summary>
public record DealerUserResp(
    string UserId,
    string Email,
    string? FullName,
    int DealerId,
    string Role);

/// <summary>Bayi marj/kazanç özeti — ödeme toplamlarından (yalnızca tahsil edilenler).</summary>
public record DealerEarningsResp(
    int DealerId,
    string DealerName,
    string CurrencyCode,
    int PaidCount,
    decimal GrossTotal,     // brüt (Amount)
    decimal MarginTotal,    // bayi payı (MarginAmount)
    decimal NetTotal,       // bizim net (NetAmount)
    int PendingCount,
    decimal PendingGross);
