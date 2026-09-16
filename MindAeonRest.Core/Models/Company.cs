namespace MindAeonRest.Models;

/// <summary>Şirket. Her şirketin kendi veritabanı vardır: MindAeonRest_{Code}.</summary>
public class Company : AuditableEntity
{
    public string Code { get; set; } = string.Empty;        // Boşluksuz, BÜYÜK, Türkçe karaktersiz
    public string Name { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty; // MindAeonRest_{Code}
    public bool IsActive { get; set; } = true;

    // Genişletilmiş tanım alanları
    public string? Address { get; set; }
    public int? CountryId { get; set; }
    public int? CityId { get; set; }
    public int? DistrictId { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Phone { get; set; }
    public int? CurrencyId { get; set; }

    // Logolar (talep üzerine yüklenir; liste sorgularında çekilmez)
    public byte[]? LogoColor { get; set; }   // renkli (login/ekran/fiş önizleme)
    public byte[]? LogoMono { get; set; }    // siyah-beyaz (termal baskı)

    // Gösterim (join)
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? DistrictName { get; set; }
    public string? CurrencyCode { get; set; }
    public string? CurrencyName { get; set; }

    public string StatusText => IsActive ? "Aktif" : "Pasif";
}
