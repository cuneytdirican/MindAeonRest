namespace MindAeonRest.Models;

/// <summary>Puan/sadakat hareket tipi.</summary>
public enum LoyaltyMovementType { Kazanim = 0, Harcama = 1, Duzeltme = 2 }

/// <summary>Müşteri puan/sadakat hesabı (müşteri başına tek).</summary>
public class LoyaltyAccount : AuditableEntity
{
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal Points { get; set; }
    public string PointsText => $"{Points:N0} puan";
}

/// <summary>Puan/sadakat hareketi (kazanım/harcama).</summary>
public class LoyaltyMovement : AuditableEntity
{
    public int CustomerId { get; set; }
    public LoyaltyMovementType Type { get; set; }
    public decimal Points { get; set; }
    public decimal Amount { get; set; }
    public int? OrderId { get; set; }
    public int? RefClFicheId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string TypeText => Type switch
    {
        LoyaltyMovementType.Kazanim => "Kazanım",
        LoyaltyMovementType.Harcama => "Harcama",
        LoyaltyMovementType.Duzeltme => "Düzeltme",
        _ => Type.ToString()
    };
}

/// <summary>Sadakat programı ayarları (tek satır). EarnRate = ₺ başına kazanılan puan; RedeemRate = 1 puanın ₺ karşılığı.</summary>
public class LoyaltySettings
{
    public bool IsActive { get; set; }
    public decimal EarnRate { get; set; }    // ör. 0.10 → harcanan her 1 ₺ için 0,10 puan
    public decimal RedeemRate { get; set; } = 1;  // ör. 1 → 1 puan = 1 ₺
}
