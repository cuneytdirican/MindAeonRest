namespace MindAeonRest.Models;

public enum CheckNoteKind { Cek = 0, Senet = 1 }
public enum CheckDirection { Alinan = 0, Verilen = 1 }   // Alınan=müşteriden, Verilen=tedarikçiye
public enum CheckStatus { Portfoyde = 0, Tahsilde = 1, Tahsil = 2, Ciro = 3, Karsiliksiz = 4, Odendi = 5 }

public static class CheckEnumExtensions
{
    public static string ToTr(this CheckNoteKind k) => k == CheckNoteKind.Senet ? "Senet" : "Çek";
    public static string ToTr(this CheckDirection d) => d == CheckDirection.Verilen ? "Verilen" : "Alınan";
    public static string ToTr(this CheckStatus s) => s switch
    {
        CheckStatus.Tahsilde => "Tahsilde",
        CheckStatus.Tahsil => "Tahsil Edildi",
        CheckStatus.Ciro => "Ciro Edildi",
        CheckStatus.Karsiliksiz => "Karşılıksız",
        CheckStatus.Odendi => "Ödendi",
        _ => "Portföyde"
    };
}

/// <summary>Çek/Senet portföy kaydı. Alınan → cari alacak (müşteri borcu azalır); Verilen → cari borç.</summary>
public class CheckNote : AuditableEntity
{
    public int BranchId { get; set; }
    public CheckNoteKind Kind { get; set; }
    public CheckDirection Direction { get; set; }
    public int CustomerId { get; set; }
    public string? Serial { get; set; }
    public string? BankName { get; set; }
    public decimal Amount { get; set; }
    public int CurrencyId { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public CheckStatus Status { get; set; }
    public string? Note { get; set; }
    public int? CariHareketId { get; set; }

    public string? CustomerName { get; set; }
    public string KindText => Kind.ToTr();
    public string DirectionText => Direction.ToTr();
    public string StatusText => Status.ToTr();
    public string AmountText => $"{Amount:N2}";
    public string IssueText => IssueDate.ToString("dd.MM.yyyy");
    public string DueText => DueDate.ToString("dd.MM.yyyy");
    /// <summary>Vadesine kalan gün (negatif=geçmiş).</summary>
    public int DaysToDue => (DueDate.Date - DateTime.Today).Days;

    public bool IsClosed => Status is CheckStatus.Tahsil or CheckStatus.Odendi or CheckStatus.Ciro or CheckStatus.Karsiliksiz;
    public string DaysToDueText => IsClosed ? "—" : DaysToDue < 0 ? $"{-DaysToDue} gün geçti" : DaysToDue == 0 ? "BUGÜN" : $"{DaysToDue} gün";
    /// <summary>Vade durumu: Kapandı / Geçti / Bugün / Yakın (≤7g) / Normal — satır renklendirme için.</summary>
    public string DueState => IsClosed ? "Kapandı" : DaysToDue < 0 ? "Geçti" : DaysToDue == 0 ? "Bugün" : DaysToDue <= 7 ? "Yakın" : "Normal";
}
