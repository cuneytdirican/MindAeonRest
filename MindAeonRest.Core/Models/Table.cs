namespace MindAeonRest.Models;

/// <summary>Masa şekli.</summary>
public enum TableShape
{
    Kare = 0,
    Yuvarlak = 1
}

/// <summary>Masa (yer) - bir zone altında, yerleşim planı koordinatlarıyla.</summary>
public class Table : AuditableEntity
{
    public int ZoneId { get; set; }
    public int BranchId { get; set; }          // zone'un şubesi (şube bazlı unique kod)
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public TableShape Shape { get; set; } = TableShape.Kare;
    public int Seats { get; set; } = 4;        // kaç kişilik
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 80;
    public double Height { get; set; } = 80;
    public bool IsActive { get; set; } = true;
}
