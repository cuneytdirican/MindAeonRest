namespace MindAeonRest.Models;

/// <summary>Kredi kartı POS hareketi (müşteriden tahsilat, karttan ödeme, transfer). Tip = BankMovementType (ortak akış semantiği).</summary>
public class PosMovement : AuditableEntity
{
    public int BranchId { get; set; }
    public int PosId { get; set; }
    public int CurrencyId { get; set; }
    public BankMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public int? CustomerId { get; set; }
    public int? CounterPosId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string? CustomerName { get; set; }
    public string TypeText => Type.ToTr();
    public string AmountText => $"{Amount:N2}";
    public string DateText => MoveDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Bir POS'un anlık bakiyesi.</summary>
public class PosBalance
{
    public int PosId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal Balance { get; set; }
    public string BalanceText => $"{Balance:N2} {CurrencyCode}";
}
