namespace MindAeonRest.Models;

/// <summary>Lokasyon (Zone) - bir şube altında (ör. Otel, Beach, Tatil Köyü).</summary>
public class Zone : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string? BranchName { get; set; }   // gösterim (sistem DB cross-db)
    public string? ReceiptPrinterName { get; set; }   // Fiş/hesap yazıcısı (Windows)
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ReceiptPrinterText => string.IsNullOrWhiteSpace(ReceiptPrinterName) ? "—" : ReceiptPrinterName!;
    public string Display => $"{Name} ({Code})";
}

/// <summary>İstasyon tipi: reyon mutfak yemeği mi (KDS) yoksa bar/içecek mi (Meşrubat) hazırlar.
/// Garson terminalinde "Sipariş Teslim" (Mutfak) ile "Meşrubatlar" (Bar) ayrımı buna göre yapılır.</summary>
public enum StationType { Mutfak = 0, Bar = 1 }

/// <summary>Reyon (Mutfak/hazırlık birimi) - bir zone altında, opsiyonel yazıcı.</summary>
public class Reyon : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ZoneId { get; set; }
    public string? ZoneName { get; set; }     // gösterim (join)
    public string? PrinterName { get; set; }
    public StationType StationType { get; set; } = StationType.Mutfak;
    public bool IsActive { get; set; } = true;

    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string StationText => StationType == StationType.Bar ? "Bar / İçecek" : "Mutfak";
    public string PrinterText => string.IsNullOrWhiteSpace(PrinterName) ? "— (yalnızca ekran)" : PrinterName!;
    public string Display => $"{Name} ({Code})";
}
