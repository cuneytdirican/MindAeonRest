namespace MindAeonRest.Models;

/// <summary>Yemek çek hesabı hareket tipi.</summary>
public enum MealVoucherMovementType
{
    Tahsilat = 0,   // adisyon ödemesi → yemek çek hesabına alacak (+)
    Iade = 1,       // iade (-)
    Giris = 2,      // manuel giriş (+)
    Cikis = 3       // manuel çıkış (-)
}

/// <summary>Yemek çek hesabı hareketi (defter). Bakiye = SUM(Amount).</summary>
public class MealVoucherMovement : AuditableEntity
{
    public int BranchId { get; set; }
    public int MealVoucherAccountId { get; set; }
    public int CurrencyId { get; set; }
    public MealVoucherMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public int? RefClFicheId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string TypeText => Type switch
    {
        MealVoucherMovementType.Tahsilat => "Tahsilat",
        MealVoucherMovementType.Iade => "İade",
        MealVoucherMovementType.Giris => "Giriş",
        MealVoucherMovementType.Cikis => "Çıkış",
        _ => Type.ToString()
    };
    public string AmountText => $"{Amount:N2} ₺";
}
