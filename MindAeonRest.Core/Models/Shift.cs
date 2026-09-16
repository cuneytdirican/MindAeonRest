namespace MindAeonRest.Models;

/// <summary>Vardiya durumu.</summary>
public enum ShiftStatus { Acik = 0, Kapali = 1 }

/// <summary>
/// KIOSK (terminal) bazlı vardiya. Bir KIOSK'ta aynı anda yalnız tek AÇIK vardiya olabilir.
/// Açılış kasası (OpeningFloat) terminalde kalan nakit; kapanışta sayılan nakit (ClosingCounted) ile mutabakat yapılır.
/// Nakit fiziksel devri YAPILMAZ — kasa terminalde kalır, sonraki vardiyanın açılış kasası olur; gün-sonu Z'deki KIOSK→şube devri ayrıdır.
/// </summary>
public class Shift : AuditableEntity
{
    public int BranchId { get; set; }
    public int KioskNo { get; set; }
    public int? OpenedBy { get; set; }
    public DateTime OpenedAt { get; set; }
    public int? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal? ClosingCounted { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Acik;
    public string? Note { get; set; }

    public string StatusText => Status == ShiftStatus.Acik ? "Açık" : "Kapalı";
    public string OpenedAtText => OpenedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string ClosedAtText => ClosedAt is DateTime c ? c.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "-";
    public string OpeningFloatText => $"₺{OpeningFloat:N2}";
    public string ClosingCountedText => ClosingCounted is decimal v ? $"₺{v:N2}" : "-";
}

/// <summary>Vardiya raporundaki kullanıcı (tahsil eden) kırılım satırı.</summary>
public class ShiftUserLine
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AmountText => $"₺{Amount:N2}";
}

/// <summary>
/// Bir vardiyanın özet raporu: ödeme yöntemi kırılımı (Payments), tahsil eden kullanıcı kırılımı (ByUser),
/// nakit tahsilat ve beklenen kasa (açılış kasası + nakit tahsilat).
/// </summary>
public class ShiftReport
{
    public int ShiftId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }

    /// <summary>Ödeme yöntemi (Kind) kırılımı — mevcut ReportPay modeli.</summary>
    public List<ReportPay> Payments { get; set; } = new();
    /// <summary>Tahsil eden kullanıcı (ClFiche.CreatedBy) kırılımı.</summary>
    public List<ShiftUserLine> ByUser { get; set; } = new();

    /// <summary>Nakit (PaymentKind.Nakit) tahsilatı toplamı.</summary>
    public decimal CashCollected { get; set; }
    /// <summary>Beklenen kasa nakdi = açılış kasası + nakit tahsilat.</summary>
    public decimal ExpectedCash => OpeningFloat + CashCollected;
    /// <summary>Tüm tahsilat (tüm yöntemler) toplamı.</summary>
    public decimal Total { get; set; }

    public string CashCollectedText => $"₺{CashCollected:N2}";
    public string ExpectedCashText => $"₺{ExpectedCash:N2}";
    public string TotalText => $"₺{Total:N2}";
}
