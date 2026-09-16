namespace MindAeonRest.Models;

/// <summary>Vergi/KDV grubu (şirket DB). Oranları TaxGroupRate'te tarih aralıklı tutulur.</summary>
public class TaxGroup : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Bir vergi grubunun belirli tarih aralığında geçerli KDV oranı.</summary>
public class TaxGroupRate : AuditableEntity
{
    public int TaxGroupId { get; set; }
    public VatType VatType { get; set; }
    public decimal Rate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string VatTypeText => VatType.ToTr();
    public string RateText => $"%{Rate:0.##}";
    public string PeriodText => $"{StartDate:dd.MM.yyyy} - {(EndDate.HasValue ? EndDate.Value.ToString("dd.MM.yyyy") : "süresiz")}";
}
