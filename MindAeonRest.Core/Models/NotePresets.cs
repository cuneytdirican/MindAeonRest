namespace MindAeonRest.Models;

/// <summary>Masa Notu modalı için hızlı not ön-tanımları (hem WPF hem MAUI kullanır).</summary>
public static class NotePresets
{
    /// <summary>Genel masa notu chip'leri.</summary>
    public static readonly string[] General =
    {
        "Doğum günü kutlaması",
        "VIP misafir",
        "Acelesi var",
        "Çocuklu masa",
        "Pencere kenarı istedi"
    };

    /// <summary>Alerji chip'leri (AllergyNote alanına yazılır → kırmızı ALERJİ rozeti).</summary>
    public static readonly string[] Allergy =
    {
        "Alerji: fıstık",
        "Alerji: gluten",
        "Alerji: süt / laktoz",
        "Alerji: deniz ürünü",
        "Alerji: yumurta"
    };
}
