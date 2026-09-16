namespace MindAeonRest.Models;

/// <summary>CSV içe aktarım için kullanıcı seçimli varsayılanlar ve kapsam.</summary>
public class CsvImportOptions
{
    public int? DefaultUnitId { get; set; }
    public int? DefaultSalesTaxId { get; set; }
    public int? DefaultPurchaseTaxId { get; set; }
    public int? DefaultReturnTaxId { get; set; }
    public int? DefaultCurrencyId { get; set; }
    public int? TargetBranchId { get; set; }     // null = genel fiyat
    public bool UpdateExisting { get; set; } = true;
}

/// <summary>CSV'den okunan ve doğrulanan bir ürün satırı.</summary>
public class CsvProductRow
{
    public int RowNo { get; set; }

    // Ham değerler
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UnitCode { get; set; }
    public string? SalesTaxCode { get; set; }
    public string? PurchaseTaxCode { get; set; }
    public string? ReturnTaxCode { get; set; }
    public string? TrackingRaw { get; set; }
    public string? CategoryCodes { get; set; }
    public string? PriceRaw { get; set; }
    public string? CurrencyCode { get; set; }
    public string? BranchCode { get; set; }
    public string? Barcode { get; set; }

    // Çözümlenmiş değerler
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public int? SalesTaxId { get; set; }
    public int? PurchaseTaxId { get; set; }
    public int? ReturnTaxId { get; set; }
    public TrackingType TrackingType { get; set; } = TrackingType.Yok;
    public List<int> ResolvedCategoryIds { get; set; } = new();
    public decimal? Price { get; set; }
    public int? CurrencyId { get; set; }
    public int? BranchId { get; set; }
    public bool ExistsInDb { get; set; }

    public bool IsValid { get; set; }
    public string? Error { get; set; }

    public string StatusText => IsValid ? (ExistsInDb ? "✓ Güncellenecek" : "✓ Eklenecek") : "✗ " + Error;
    public string PriceDisplay => Price.HasValue ? $"{Price:0.00} {CurrencyCode}" : "";
}

/// <summary>İçe aktarım sonucu özeti.</summary>
public class CsvImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}
