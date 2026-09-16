namespace MindAeonRest.Models;

/// <summary>Şirket şubesi.</summary>
public class CompanyBranch : AuditableEntity
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;   // gösterim için (join)
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Genişletilmiş tanım alanları
    public string? Address { get; set; }
    public int? CountryId { get; set; }
    public int? CityId { get; set; }
    public int? DistrictId { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Phone { get; set; }

    /// <summary>İŞ GÜNÜ KESİMİ: gece yarısından itibaren kaç dakika sonra "yeni iş günü" başlar.
    /// 0 = gece yarısı (varsayılan/mevcut davranış). Örn. 360 = 06:00. Şube bazlıdır.</summary>
    public int DayStartMinutes { get; set; } = 0;

    /// <summary>Kesim saatinin HH:mm metni (gösterim için). Örn. 360 → "06:00".</summary>
    public string DayStartText => TimeSpan.FromMinutes(DayStartMinutes).ToString(@"hh\:mm");

    // Gösterim (join)
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? DistrictName { get; set; }

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}
