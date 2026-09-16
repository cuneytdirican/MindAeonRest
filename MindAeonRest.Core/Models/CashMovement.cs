namespace MindAeonRest.Models;

public enum CashMovementType
{
    Tahsilat = 0,       // adisyon nakit tahsilatı (+)
    Giris = 1,          // manuel para girişi (+)
    Cikis = 2,          // manuel para çıkışı / gider (-)
    TransferGiris = 3,  // başka kasadan transfer (+)
    TransferCikis = 4   // başka kasaya transfer (-)
}

public static class CashMovementTypeExtensions
{
    public static string ToTr(this CashMovementType t) => t switch
    {
        CashMovementType.Tahsilat => "Tahsilat",
        CashMovementType.Giris => "Para Girişi",
        CashMovementType.Cikis => "Para Çıkışı",
        CashMovementType.TransferGiris => "Transfer (Gelen)",
        CashMovementType.TransferCikis => "Transfer (Giden)",
        _ => t.ToString()
    };
}

/// <summary>Nakit kasa hareketi.</summary>
public class CashMovement : AuditableEntity
{
    public int BranchId { get; set; }
    public int CashRegisterId { get; set; }
    public int CurrencyId { get; set; }
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }   // işaretli (+ giriş, - çıkış)
    public int? RefClFicheId { get; set; }
    public int? CounterCashRegisterId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string TypeText => Type.ToTr();
    public string AmountText => $"{Amount:N2}";
    public string DateText => MoveDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Bir kasanın anlık bakiyesi (dövizli).</summary>
public class CashBalance
{
    public int CashRegisterId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal Balance { get; set; }
    public string BalanceText => $"{Balance:N2} {CurrencyCode}";
}
