namespace MindAeonRest.Models;

/// <summary>Müşteri e-belge tipi.</summary>
public enum EInvoiceType
{
    Yok = 0,
    EArsiv = 1,
    EFatura = 2
}

public static class EInvoiceTypeExtensions
{
    public static string ToTr(this EInvoiceType t) => t switch
    {
        EInvoiceType.EArsiv => "E-Arşiv",
        EInvoiceType.EFatura => "E-Fatura",
        _ => "Yok"
    };
}
