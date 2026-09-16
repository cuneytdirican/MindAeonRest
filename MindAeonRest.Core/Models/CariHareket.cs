namespace MindAeonRest.Models;

/// <summary>Cari hareket belge tipi.</summary>
public enum CariDocType
{
    Acilis = 0, SatisFaturasi = 1, AlisFaturasi = 2, SatisIadeFaturasi = 3, AlisIadeFaturasi = 4,
    Tahsilat = 5, Odeme = 6, Cek = 7, Senet = 8, Manuel = 9
}

public static class CariDocTypeExtensions
{
    public static string ToTr(this CariDocType t) => t switch
    {
        CariDocType.Acilis => "Açılış",
        CariDocType.SatisFaturasi => "Satış Faturası",
        CariDocType.AlisFaturasi => "Alış Faturası",
        CariDocType.SatisIadeFaturasi => "Satış İade Faturası",
        CariDocType.AlisIadeFaturasi => "Alış İade Faturası",
        CariDocType.Tahsilat => "Tahsilat",
        CariDocType.Odeme => "Ödeme",
        CariDocType.Cek => "Çek",
        CariDocType.Senet => "Senet",
        _ => "Manuel"
    };
}

/// <summary>
/// Cari hesap hareketi (defter). Borç (Debt): cari bize borçlanır (satış faturası).
/// Alacak (Credit): cari alacaklanır (ödeme/tahsilat veya alış faturası). Bakiye = Σ(Borç-Alacak).
/// Sadece faturalar ve ödeme/tahsilatlar cari hareket üretir (sipariş/irsaliye ÜRETMEZ).
/// </summary>
public class CariHareket : AuditableEntity
{
    public int BranchId { get; set; }
    public int CustomerId { get; set; }
    public DateTime TxnDate { get; set; }
    public CariDocType DocType { get; set; }
    public string? DocRefType { get; set; }   // ör. "ClFiche", "Fatura"
    public int? DocRefId { get; set; }
    public string? DocNo { get; set; }         // evrak/fiş no
    public string? Description { get; set; }
    public int CurrencyId { get; set; }
    public decimal Debt { get; set; }          // Borç
    public decimal Credit { get; set; }        // Alacak
    public int? ProjectId { get; set; }

    // gösterim
    public string? CustomerName { get; set; }
    public string? ProjectCode { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal RunningBalance { get; set; }

    public string DocTypeText => DocType.ToTr();
    public string DateText => TxnDate.ToString("dd.MM.yyyy HH:mm");
    public decimal Net => Debt - Credit;
}

/// <summary>Cari bakiye listesi satırı (rapor).</summary>
public class CariBalance
{
    public int CustomerId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string TypeText { get; set; } = "";
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal Debt { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance => Debt - Credit;
}
