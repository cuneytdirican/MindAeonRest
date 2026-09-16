namespace MindAeonRest.Models;

/// <summary>Hediye çeki/kart durumu.</summary>
public enum GiftCardStatus { Aktif = 0, Bitti = 1, Iptal = 2 }

/// <summary>Hediye çeki hareket tipi.</summary>
public enum GiftCardMovementType { Yukleme = 0, Kullanim = 1, Iptal = 2 }

/// <summary>Hediye çeki / hediye kart (firma içi unique kod + bakiye).</summary>
public class GiftCard : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public decimal InitialAmount { get; set; }
    public decimal Balance { get; set; }
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public GiftCardStatus Status { get; set; } = GiftCardStatus.Aktif;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime IssuedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public string? Note { get; set; }

    public string StatusText => Status switch
    {
        GiftCardStatus.Aktif => "Aktif",
        GiftCardStatus.Bitti => "Bitti",
        GiftCardStatus.Iptal => "İptal",
        _ => Status.ToString()
    };
    public string BalanceText => $"{Balance:N2} ₺";
    public string InitialText => $"{InitialAmount:N2} ₺";
}

/// <summary>Hediye çeki hareketi (yükleme/kullanım/iptal).</summary>
public class GiftCardMovement : AuditableEntity
{
    public int GiftCardId { get; set; }
    public int BranchId { get; set; }
    public GiftCardMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public int? OrderId { get; set; }
    public int? RefClFicheId { get; set; }
    public string? Note { get; set; }
    public DateTime MoveDate { get; set; }

    public string TypeText => Type switch
    {
        GiftCardMovementType.Yukleme => "Yükleme",
        GiftCardMovementType.Kullanim => "Kullanım",
        GiftCardMovementType.Iptal => "İptal",
        _ => Type.ToString()
    };
    public string AmountText => $"{Amount:N2} ₺";
}
