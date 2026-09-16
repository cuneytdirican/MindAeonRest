namespace MindAeonRest.Services;

/// <summary>
/// FAZ 3a Talep Tahmini — saf (DB'siz) hesap mantığı. Hareketli ortalama (taban seviye) +
/// haftalık mevsimsellik (gün-içi endeks) ile ertesi gün/hafta tahmini. Birim test edilebilir.
/// Zaman serisi indeksi i, hafta günü (startWeekday + i) % 7 olarak yorumlanır (0..6, çağıran belirler).
/// </summary>
public static class ForecastMath
{
    /// <summary>Son <paramref name="window"/> noktanın ortalaması (daha az nokta varsa hepsinin). Boş seri → 0.</summary>
    public static double MovingAverage(IReadOnlyList<double> values, int window)
    {
        if (values is null || values.Count == 0) return 0;
        if (window <= 0) window = values.Count;
        int take = Math.Min(window, values.Count);
        double sum = 0;
        for (int i = values.Count - take; i < values.Count; i++) sum += values[i];
        return sum / take;
    }

    /// <summary>
    /// Haftalık mevsimsellik endeksleri (7 çarpan, index = hafta günü): o güne düşen değerlerin ortalaması / genel ortalama.
    /// O güne veri yoksa ya da genel ortalama 0 ise endeks 1 (nötr). startWeekday = values[0]'ın hafta günü.
    /// </summary>
    public static double[] WeeklySeasonalIndices(IReadOnlyList<double> values, int startWeekday)
    {
        var idx = new double[7];
        for (int i = 0; i < 7; i++) idx[i] = 1.0;
        if (values is null || values.Count == 0) return idx;

        double overall = 0;
        for (int i = 0; i < values.Count; i++) overall += values[i];
        overall /= values.Count;
        if (overall <= 0) return idx;

        var sum = new double[7];
        var cnt = new int[7];
        for (int i = 0; i < values.Count; i++)
        {
            int wd = ((startWeekday + i) % 7 + 7) % 7;
            sum[wd] += values[i];
            cnt[wd]++;
        }
        for (int w = 0; w < 7; w++)
            if (cnt[w] > 0) idx[w] = (sum[w] / cnt[w]) / overall;
        return idx;
    }

    /// <summary>
    /// Son elemandan SONRAKİ günün tahmini: hareketli ortalama × o günün mevsimsellik endeksi.
    /// targetWeekday = (startWeekday + values.Count) % 7. Negatif sonuç 0'a kırpılır.
    /// </summary>
    public static double ForecastNextDay(IReadOnlyList<double> values, int startWeekday, int window)
    {
        if (values is null || values.Count == 0) return 0;
        double baseLevel = MovingAverage(values, window);
        var idx = WeeklySeasonalIndices(values, startWeekday);
        int targetWd = ((startWeekday + values.Count) % 7 + 7) % 7;
        double f = baseLevel * idx[targetWd];
        return f < 0 ? 0 : f;
    }

    /// <summary>
    /// Son elemandan sonraki <paramref name="days"/> günün tahmini (varsayılan 7). Taban seviye sabit
    /// (hareketli ortalama), her güne kendi mevsimsellik endeksi uygulanır.
    /// </summary>
    public static double[] ForecastNextDays(IReadOnlyList<double> values, int startWeekday, int window, int days = 7)
    {
        var result = new double[Math.Max(0, days)];
        if (values is null || values.Count == 0) return result;
        double baseLevel = MovingAverage(values, window);
        var idx = WeeklySeasonalIndices(values, startWeekday);
        for (int k = 0; k < result.Length; k++)
        {
            int targetWd = ((startWeekday + values.Count + k) % 7 + 7) % 7;
            double f = baseLevel * idx[targetWd];
            result[k] = f < 0 ? 0 : f;
        }
        return result;
    }
}
