namespace MindAeonRest.Services;

/// <summary>
/// FAZ 4b Stok Zekâsı — saf (DB'siz) hesap mantığı: satılan ürünlerin reçeteye göre TEORİK bileşen
/// tüketimi (leaf/ham bileşene kadar özyinelemeli rollup) + teorik/gerçek sapma. StockIntelligenceService
/// bunu çağırır; formüller burada izole edildiği için birim test edilebilir. ADO.NET/DB yok.
/// </summary>
public static class StockIntelMath
{
    /// <summary>
    /// Satılan ürünleri reçeteye göre ham bileşen (leaf) tüketimine indirger:
    /// menü satıldıkça bileşenleri (ve onların bileşenleri) miktarla çarpılarak toplanır.
    /// Reçetesi olmayan ürün kendi başına tüketilir (leaf). Döngü koruması: aynı yol üzerinde
    /// tekrar eden ürün genişletilmez (sonsuz özyineleme engeli).
    /// </summary>
    /// <param name="sales">satılan (ProductId, Qty) çiftleri (aynı ürün birden çok kez gelebilir).</param>
    /// <param name="recipeOf">bir ürünün reçete bileşenlerini döndüren fonksiyon (yoksa boş liste).</param>
    /// <returns>ham bileşen ProductId → toplam teorik tüketim miktarı.</returns>
    public static Dictionary<int, decimal> ExpandConsumption(
        IEnumerable<(int ProductId, decimal Qty)> sales,
        Func<int, IReadOnlyList<(int ComponentId, decimal Quantity)>> recipeOf)
    {
        var acc = new Dictionary<int, decimal>();
        foreach (var (pid, qty) in sales)
            Accumulate(pid, qty, recipeOf, acc, new HashSet<int>());
        return acc;
    }

    private static void Accumulate(int productId, decimal qty,
        Func<int, IReadOnlyList<(int ComponentId, decimal Quantity)>> recipeOf,
        Dictionary<int, decimal> acc, HashSet<int> path)
    {
        if (qty == 0) return;
        var comps = recipeOf(productId);
        if (comps is null || comps.Count == 0)   // leaf/ham bileşen → kendini tüketir
        {
            acc[productId] = acc.TryGetValue(productId, out var v) ? v + qty : qty;
            return;
        }
        if (!path.Add(productId)) return;         // döngü: bu ürün zaten mevcut yolda → genişletme
        try
        {
            foreach (var (comp, cq) in comps)
                Accumulate(comp, qty * cq, recipeOf, acc, path);
        }
        finally { path.Remove(productId); }
    }

    /// <summary>Sapma miktarı = gerçek − teorik. Pozitif = reçetenin öngördüğünden FAZLA tüketim
    /// (fire/kayıp/porsiyon aşımı/sayım eksiği); negatif = teoriden az (sayım fazlası/eksik kayıt).</summary>
    public static decimal Variance(decimal theoretical, decimal actual) => actual - theoretical;

    /// <summary>Sapma yüzdesi = (gerçek − teorik) / teorik × 100. Teorik 0 ise 0. 2 haneye yuvarlanır.</summary>
    public static decimal VariancePercent(decimal theoretical, decimal actual)
        => theoretical == 0 ? 0m : decimal.Round((actual - theoretical) / theoretical * 100m, 2);
}
