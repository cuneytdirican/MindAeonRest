namespace MindAeonRest.Models;

/// <summary>
/// FAZ 4c — Personel mesai giriş/çıkış kaydı (time-clock / puantaj).
/// DİKKAT: Bu, kasa <see cref="Shift"/> (vardiya) kaydından tamamen ayrıdır; personelin işe geliş-gidiş saatidir.
/// Bir personelin aynı anda tek AÇIK (ClockOut == null) kaydı olabilir.
/// </summary>
public class Attendance : AuditableEntity
{
    public int PersonnelId { get; set; }
    public int BranchId { get; set; }
    public DateTime ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public string? Note { get; set; }
    public int KioskNo { get; set; }

    // ---- Gösterim (cross-join / hesap) ----
    public string? PersonnelName { get; set; }
    public string? PersonnelCode { get; set; }
    public string? BranchName { get; set; }

    /// <summary>Açık mesai mi (çıkış yapılmamış)?</summary>
    public bool IsOpen => ClockOut is null;

    /// <summary>Çalışılan saat (açıksa 0). Saf hesap <see cref="Services.LaborMath.Hours"/>.</summary>
    public decimal Hours => Services.LaborMath.Hours(ClockIn, ClockOut);

    public string ClockInText => ClockIn.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string ClockOutText => ClockOut?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string HoursText => IsOpen ? "(devam ediyor)" : Hours.ToString("N2");
}

/// <summary>FAZ 4c — (opsiyonel) vardiya planı: personel bazlı gün/başlangıç-bitiş saatleri.</summary>
public class WorkSchedule : AuditableEntity
{
    public int PersonnelId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Note { get; set; }

    public string? PersonnelName { get; set; }
}

/// <summary>FAZ 4c — (opsiyonel) bahşiş havuzu: şube+gün toplam tutar + dağıtım (JSON).</summary>
public class TipPool : AuditableEntity
{
    public int BranchId { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Dağıtım (JSON): [{ "PersonnelId": 1, "Share": 120.50 }, ...]</summary>
    public string? Distribution { get; set; }
}

/// <summary>Puantaj (timesheet) rapor satırı: personel bazında dönem toplamı + labor-cost.</summary>
public class LaborCostRow
{
    public int PersonnelId { get; set; }
    public string PersonnelCode { get; set; } = string.Empty;
    public string PersonnelName { get; set; } = string.Empty;
    public decimal Salary { get; set; }
    public int Shifts { get; set; }           // kapanmış mesai adedi
    public decimal Hours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal LaborCost { get; set; }

    public string HoursText => Hours.ToString("N2");
    public string LaborCostText => LaborCost.ToString("N2");
    public string HourlyRateText => HourlyRate.ToString("N2");
}

/// <summary>Puantaj / labor-cost raporu: dönem + şube kırılımı, toplam saat/maliyet ve ciroya oran (%).</summary>
public class LaborCostReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int BranchId { get; set; }
    public decimal StandardMonthlyHours { get; set; }
    public List<LaborCostRow> Rows { get; } = new();

    public decimal TotalHours => Rows.Sum(r => r.Hours);
    public decimal TotalLaborCost => Rows.Sum(r => r.LaborCost);
    /// <summary>Dönem cirosu (ClFiches.Total; % için).</summary>
    public decimal Revenue { get; set; }
    /// <summary>Labor-cost % = toplam işçilik / ciro × 100 (ciro 0 ise 0).</summary>
    public decimal LaborCostPercent => Services.LaborMath.LaborCostPercent(TotalLaborCost, Revenue);
}
