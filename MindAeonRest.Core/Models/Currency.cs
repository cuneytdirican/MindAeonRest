namespace MindAeonRest.Models;

/// <summary>Döviz tipi (DovizTipleri).</summary>
public class Currency : AuditableEntity
{
    public string Code { get; set; } = string.Empty;   // TRY, USD, EUR
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public bool IsActive { get; set; } = true;
}
