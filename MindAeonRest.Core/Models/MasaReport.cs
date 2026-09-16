namespace MindAeonRest.Models;

/// <summary>Masa anlık durum satırı (şu an).</summary>
public class TableLiveRow
{
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public int Seats { get; set; }
    public string State { get; set; } = "Boş";          // Boş / Dolu / Rezerve
    public string? OrderCode { get; set; }
    public System.DateTime? OpenedAt { get; set; }
    public int Covers { get; set; }
    public decimal Total { get; set; }
    public string? WaiterName { get; set; }
    public string? Info { get; set; }                   // rezervasyon vb.

    public string SeatsText => $"{Seats} kişilik";
    // OpenedAt UTC saklanır (SYSUTCDATETIME). Görüntüde yerele çevir; süre farkını UTC'ye UTC ile hesapla
    // (DateTime.Now ile çıkarma GMT+3'te "+180 dk" hatası verirdi — DateTime aritmetiği Kind'ı yok sayar).
    public string OpenedText => OpenedAt is { } d ? System.DateTime.SpecifyKind(d, System.DateTimeKind.Utc).ToLocalTime().ToString("dd.MM HH:mm") : "—";
    public string DurationText => OpenedAt is { } d ? FmtDur(System.DateTime.UtcNow - d) : "—";
    public string CoversText => Covers > 0 ? $"{Covers}" : "—";
    public string TotalText => State == "Dolu" ? $"{Total:N2} ₺" : "—";
    private static string FmtDur(System.TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}s {t.Minutes}dk" : $"{t.Minutes}dk";
}

/// <summary>Masa geçmiş performans satırı (tarih aralığı).</summary>
public class TableHistoryRow
{
    public string TableCode { get; set; } = "";
    public string TableName { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public int Seats { get; set; }
    public int AdisyonCount { get; set; }               // kaç kez kullanıldı (devir/turnover)
    public decimal Revenue { get; set; }
    public int Covers { get; set; }
    public int CancelCount { get; set; }
    public System.DateTime? LastUsed { get; set; }

    public decimal AvgTicket => AdisyonCount > 0 ? Revenue / AdisyonCount : 0;
    public decimal RevenuePerSeat => Seats > 0 ? Revenue / Seats : 0;
    public string RevenueText => $"{Revenue:N2} ₺";
    public string AvgTicketText => $"{AvgTicket:N2} ₺";
    public string RevPerSeatText => $"{RevenuePerSeat:N2} ₺";
    public string LastUsedText => LastUsed is { } d ? d.ToString("dd.MM.yyyy HH:mm") : "—";
}
