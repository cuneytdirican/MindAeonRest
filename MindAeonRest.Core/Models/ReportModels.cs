namespace MindAeonRest.Models;

/// <summary>Rapor kolonu tanımı.</summary>
public class ReportColumn
{
    public string Header { get; set; } = "";
    public double Width { get; set; } = 120;
    public bool Numeric { get; set; }
    public string? Format { get; set; }   // null + Numeric → "N2"

    public ReportColumn() { }
    public ReportColumn(string header, double width = 120, bool numeric = false, string? format = null)
    { Header = header; Width = width; Numeric = numeric; Format = format; }

    public string Effective => Format ?? (Numeric ? "N2" : "");
}

/// <summary>Genel rapor tablosu (gösterim + export ortak veri yapısı).</summary>
public class ReportTable
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public List<ReportColumn> Columns { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new();
    /// <summary>Alt toplam satırı (kolon hizalı). Boş hücreler null.</summary>
    public object?[]? Footer { get; set; }
}

/// <summary>Rapor filtre çubuğunda gösterilecek alanlar.</summary>
[Flags]
public enum ReportFilterOptions
{
    None = 0,
    DateRange = 1,
    Customer = 2,
    Branch = 4,
    Warehouse = 8,
    Search = 16
}

/// <summary>Filtre çubuğundan okunan değerler (rapor üreticiye geçer).</summary>
public class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? CustomerId { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Search { get; set; }
}
