namespace MindAeonRest.Models;

/// <summary>Rezervasyon durumu.</summary>
public enum ReservationStatus
{
    Beklemede = 0,
    Onaylandi = 1,
    Geldi = 2,
    Tamamlandi = 3,
    Iptal = 4,
    Gelmedi = 5
}

public static class ReservationStatusExtensions
{
    public static string ToTr(this ReservationStatus s) => s switch
    {
        ReservationStatus.Beklemede => "Beklemede",
        ReservationStatus.Onaylandi => "Onaylandı",
        ReservationStatus.Geldi => "Geldi",
        ReservationStatus.Tamamlandi => "Tamamlandı",
        ReservationStatus.Iptal => "İptal",
        ReservationStatus.Gelmedi => "Gelmedi",
        _ => s.ToString()
    };
}

/// <summary>Rezervasyon açıklama etiketi (Doğum Günü, İş Yemeği...). Renkli, anında görünür.</summary>
public class ReservationTag : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ColorOrDefault => string.IsNullOrWhiteSpace(Color) ? "#607D8B" : Color!;
    public string Display => Name;
}

/// <summary>Rezervasyon. Bir müşteri, bir zaman aralığı, bir veya daha fazla masa, opsiyonel etiket.</summary>
public class Reservation : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int ZoneId { get; set; }
    public string? ZoneName { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int? TagId { get; set; }
    public string? TagName { get; set; }
    public string? TagColor { get; set; }
    public string? Note { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int PartySize { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Beklemede;
    public bool IsActive { get; set; } = true;

    // gösterim
    public List<int> TableIds { get; set; } = new();
    public string? TableCodes { get; set; }
    public string StatusText => Status.ToTr();
    public string TimeRangeText => $"{StartTime:dd.MM.yyyy HH:mm} - {EndTime:HH:mm}";
    public string TagColorOrDefault => string.IsNullOrWhiteSpace(TagColor) ? "#607D8B" : TagColor!;
    public string Summary => $"{StartTime:HH:mm} · {CustomerName} · {TableCodes}" + (string.IsNullOrWhiteSpace(TagName) ? "" : $" · {TagName}");
}
