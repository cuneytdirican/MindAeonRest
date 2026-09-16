namespace MindAeonRest.Models;

/// <summary>Ürün takip detayı.</summary>
public enum TrackingType
{
    Yok = 0,
    Parti = 1,
    PartiLot = 2,
    Seri = 3,
    RenkBeden = 4
}

public static class TrackingTypeExtensions
{
    public static string ToTr(this TrackingType t) => t switch
    {
        TrackingType.Yok => "Takip Yok",
        TrackingType.Parti => "Parti Bazında",
        TrackingType.PartiLot => "Parti/Lot Bazında",
        TrackingType.Seri => "Seri Bazında",
        TrackingType.RenkBeden => "Renk/Beden Bazında",
        _ => t.ToString()
    };
}
