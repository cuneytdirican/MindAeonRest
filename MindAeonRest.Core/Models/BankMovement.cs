namespace MindAeonRest.Models;

public enum BankMovementType
{
    Tahsilat = 0,       // müşteriden tahsilat (+) → cari alacak
    Odeme = 1,          // tedarikçiye ödeme (-) → cari borç
    Giris = 2,          // manuel para girişi (+)
    Cikis = 3,          // manuel para çıkışı (-)
    TransferGiris = 4,  // başka hesaptan transfer (+)
    TransferCikis = 5   // başka hesaba transfer (-)
}

public static class BankMovementTypeExtensions
{
    public static string ToTr(this BankMovementType t) => t switch
    {
        BankMovementType.Tahsilat => "Tahsilat (Müşteri)",
        BankMovementType.Odeme => "Ödeme (Tedarikçi)",
        BankMovementType.Giris => "Para Girişi",
        BankMovementType.Cikis => "Para Çıkışı",
        BankMovementType.TransferGiris => "Transfer (Gelen)",
        BankMovementType.TransferCikis => "Transfer (Giden)",
        _ => t.ToString()
    };
    public static bool IsInflow(this BankMovementType t) =>
        t is BankMovementType.Tahsilat or BankMovementType.Giris or BankMovementType.TransferGiris;
}

/// <summary>Banka hesabı hareketi (havale/eft, tahsilat, ödeme, transfer).</summary>
public class BankMovement : AuditableEntity
{
    public int BranchId { get; set; }
    public int BankAccountId { get; set; }
    public int CurrencyId { get; set; }
    public BankMovementType Type { get; set; }
    public decimal Amount { get; set; }   // işaretli (+ giriş, - çıkış)
    public int? CustomerId { get; set; }
    public int? CounterBankAccountId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string? CustomerName { get; set; }
    public string TypeText => Type.ToTr();
    public string AmountText => $"{Amount:N2}";
    public string DateText => MoveDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Bir banka hesabının anlık bakiyesi.</summary>
public class BankBalance
{
    public int BankAccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal Balance { get; set; }
    public string BalanceText => $"{Balance:N2} {CurrencyCode}";
}
