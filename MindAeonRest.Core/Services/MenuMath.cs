using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// FAZ 3a Menü Mühendisliği — saf (DB'siz) matris sınıflandırma mantığı. Birim test edilebilir olması için
/// popülerlik × kârlılık eşik kıyaslaması buraya izole edildi (MenuEngineeringService bunu çağırır).
/// </summary>
public static class MenuMath
{
    /// <summary>
    /// Menü matrisi: popülerlik (adet) ve kârlılık (birim katkı marjı) ortalama eşiğinin üstü/altına göre sınıf.
    /// Eşiğe eşit değerler "üst" (yüksek) sayılır (>=). Yıldız / İnek / Bilmece / Köpek.
    /// </summary>
    public static MenuClass Classify(decimal popularity, decimal avgPopularity, decimal profitability, decimal avgProfitability)
    {
        bool highPop = popularity >= avgPopularity;
        bool highProfit = profitability >= avgProfitability;
        if (highPop) return highProfit ? MenuClass.Star : MenuClass.Plowhorse;
        return highProfit ? MenuClass.Puzzle : MenuClass.Dog;
    }

    /// <summary>Türkçe sınıf etiketi.</summary>
    public static string Label(MenuClass c) => c switch
    {
        MenuClass.Star => "Yıldız",
        MenuClass.Plowhorse => "İnek",
        MenuClass.Puzzle => "Bilmece",
        _ => "Köpek"
    };

    /// <summary>Sınıf rozet rengi (UI).</summary>
    public static string Color(MenuClass c) => c switch
    {
        MenuClass.Star => "#2E7D32",      // yeşil
        MenuClass.Plowhorse => "#1565C0", // mavi
        MenuClass.Puzzle => "#E8A33D",    // amber
        _ => "#C62828"                    // kırmızı
    };
}
