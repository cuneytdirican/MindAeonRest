namespace MindAeonRest.Models;

/// <summary>KDV oran tipi.</summary>
public enum VatType
{
    Alis = 1,
    Satis = 2,
    Iade = 3
}

public static class VatTypeExtensions
{
    public static string ToTr(this VatType type) => type switch
    {
        VatType.Alis => "Alış",
        VatType.Satis => "Satış",
        VatType.Iade => "İade",
        _ => type.ToString()
    };
}
