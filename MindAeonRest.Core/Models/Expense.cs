namespace MindAeonRest.Models;

public enum ExpensePayKind { Kasa = 0, Banka = 1 }

public static class ExpensePayKindExtensions
{
    public static string ToTr(this ExpensePayKind k) => k == ExpensePayKind.Banka ? "Banka" : "Kasa";
}

/// <summary>Gider fişi. Kasa/Banka'dan çıkış yapar; maaş tipinde personel seçilir.</summary>
public class Expense : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int ExpenseTypeId { get; set; }
    public int? PersonnelId { get; set; }
    public int? ProjectId { get; set; }
    public decimal Amount { get; set; }
    public int CurrencyId { get; set; }
    public ExpensePayKind PayKind { get; set; }
    public int InstrumentId { get; set; }     // kasa veya banka hesabı id
    public DateTime ExpenseDate { get; set; } = DateTime.Now;
    public string? Description { get; set; }

    public string? ExpenseTypeName { get; set; }
    public string? PersonnelName { get; set; }
    public string? InstrumentName { get; set; }
    public string DateText => ExpenseDate.ToString("dd.MM.yyyy HH:mm");
    public string AmountText => $"{Amount:N2}";
    public string PayText => PayKind.ToTr();
}
