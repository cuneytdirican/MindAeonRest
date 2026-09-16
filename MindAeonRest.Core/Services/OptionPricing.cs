using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Ücretli ürün opsiyonlarının (varyant) fiyat farkı toplamı — SUNUCU-DOĞRULAMALI (DB'siz saf hesap).
/// Fiyat manipülasyonuna karşı: istemcinin gönderdiği delta tutarına GÜVENİLMEZ; yalnız o ürüne ait
/// (GetOptions'tan gelen) geçerli opsiyon ID'lerinin PriceDelta'sı toplanır. Başka ürünün opsiyonu ya da
/// uydurma/çift ID yok sayılır.</summary>
public static class OptionPricing
{
    /// <param name="productOptions">İlgili ürünün SUNUCUDAN gelen opsiyonları (ProductService.GetOptions).</param>
    /// <param name="selectedOptionIds">İstemcinin seçtiği opsiyon ID'leri (güvenilmez; ID'ler sunucuya doğrulatılır).</param>
    /// <returns>Geçerli+distinct seçili opsiyonların PriceDelta toplamı (seçim yoksa 0).</returns>
    public static decimal Delta(IEnumerable<ProductOption> productOptions, IEnumerable<int>? selectedOptionIds)
    {
        if (selectedOptionIds is null) return 0m;
        var opts = productOptions as IReadOnlyCollection<ProductOption> ?? productOptions.ToList();
        return selectedOptionIds
            .Distinct()
            .Select(oid => opts.FirstOrDefault(o => o.Id == oid))
            .Where(o => o != null)
            .Sum(o => o!.PriceDelta);
    }
}
