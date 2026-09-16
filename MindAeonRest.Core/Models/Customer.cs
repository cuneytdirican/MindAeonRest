namespace MindAeonRest.Models;

/// <summary>Cari/Müşteri (şirket DB). Lokasyon id'leri sistem DB'sindedir.</summary>
public class Customer : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? CustomerGroupId { get; set; }
    public string? Address { get; set; }
    public int? CountryId { get; set; }
    public int? CityId { get; set; }
    public int? DistrictId { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Phone { get; set; }
    public EInvoiceType EInvoiceType { get; set; } = EInvoiceType.Yok;
    public CustomerType CustomerType { get; set; } = CustomerType.Musteri;
    public int? PriceListId { get; set; }            // müşteriye atanan fiyat listesi (adisyonda otomatik)
    public bool IsActive { get; set; } = true;

    public string? CustomerGroupName { get; set; }  // gösterim (join)
    public string? PriceListName { get; set; }       // gösterim (join)

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string EInvoiceText => EInvoiceType.ToTr();
    public string CustomerTypeText => CustomerType.ToTr();
}

/// <summary>Müşteri adresi (bir müşteri birden fazla adrese sahip olabilir).</summary>
public class CustomerAddress : AuditableEntity
{
    public int CustomerId { get; set; }
    public string? Title { get; set; }
    public string? Address { get; set; }
    public int? CountryId { get; set; }
    public int? CityId { get; set; }
    public int? DistrictId { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }

    public string DefaultText => IsDefault ? "Varsayılan" : "";
}
