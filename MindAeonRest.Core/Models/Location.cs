namespace MindAeonRest.Models;

/// <summary>Ülke.</summary>
public class Country : AuditableEntity
{
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Şehir.</summary>
public class City : AuditableEntity
{
    public int CountryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PlateCode { get; set; }
}

/// <summary>İlçe.</summary>
public class District : AuditableEntity
{
    public int CityId { get; set; }
    public string Name { get; set; } = string.Empty;
}
