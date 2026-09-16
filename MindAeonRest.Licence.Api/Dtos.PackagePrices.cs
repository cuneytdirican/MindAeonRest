using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

/// <summary>Admin: paket×süre fiyat satırı oluşturma/güncelleme.</summary>
public record PackagePriceUpsertReq(
    int Package,
    DurationKind Duration,
    decimal BasePrice,
    string? CurrencyCode,
    bool IsActive);

/// <summary>Admin görünümü — ham taban fiyat (marjsız).</summary>
public record PackagePriceResp(
    int Id,
    int Package,
    string PackageName,
    DurationKind Duration,
    decimal BasePrice,
    string CurrencyCode,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc);

/// <summary>Bayi/Müşteri görünümü — marj uygulanmış satış fiyatı.
/// BasePrice = taban (yalnız Admin'e döner; bayi/müşteride null), Price = uygulanan fiyat.</summary>
public record PackagePricePublicResp(
    int Package,
    string PackageName,
    DurationKind Duration,
    decimal Price,
    string CurrencyCode,
    decimal MarginPercent,
    decimal? BasePrice);
