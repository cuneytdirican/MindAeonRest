namespace MindAeonRest.Models;

public enum TableBoardStatus { Empty = 0, Occupied = 1, Reserved = 2 }

/// <summary>Masa board kartı: masanın anlık durumu (boş/dolu/rezerve) + tutar/garson/saat.</summary>
public class TableBoardItem
{
    public int TableId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public TableShape Shape { get; set; }
    public int Seats { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public TableBoardStatus Status { get; set; }
    public int? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? WaiterName { get; set; }
    public DateTime? OpenedAt { get; set; }

    public string? ReservationCustomer { get; set; }
    public DateTime? ReservationStart { get; set; }
    public string? TagColor { get; set; }

    /// <summary>Kartta gösterilecek ad: Name boşsa Code (ör. MASA1).</summary>
    public string Display => string.IsNullOrWhiteSpace(Name) ? Code : Name!;
    public string AmountText => Amount > 0 ? $"₺{Amount:N2}" : "";
    public string TimeText => OpenedAt.HasValue ? OpenedAt.Value.ToLocalTime().ToString("HH:mm")
        : ReservationStart.HasValue ? ReservationStart.Value.ToString("HH:mm") : "";
    /// <summary>Masanın ne kadar süredir açık olduğu (dolu masada).</summary>
    public string ElapsedText
    {
        get
        {
            if (!OpenedAt.HasValue) return "";
            var span = DateTime.UtcNow - OpenedAt.Value;
            if (span.TotalMinutes < 0) span = TimeSpan.Zero;
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}sa {span.Minutes}dk" : $"{span.Minutes}dk";
        }
    }
    public string StatusText => Status switch
    {
        TableBoardStatus.Occupied => "Dolu",
        TableBoardStatus.Reserved => "Rezerve",
        _ => "Boş"
    };
    /// <summary>Kart arka plan rengi (MENULUX: boş=açık, dolu=pembe, rezerve=amber).</summary>
    public string StatusColor => Status switch
    {
        TableBoardStatus.Occupied => "#E91E63",
        TableBoardStatus.Reserved => "#E9A23B",
        _ => "#FFFFFF"
    };
    public bool IsEmpty => Status == TableBoardStatus.Empty;
    public string TextColor => IsEmpty ? "#2B2B3A" : "#FFFFFF";
    public string SubTextColor => IsEmpty ? "#9AA0AB" : "#F2E9EF";
    public string BorderColor => IsEmpty ? "#E6E8EE" : StatusColor;
}
