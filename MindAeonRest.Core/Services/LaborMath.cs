namespace MindAeonRest.Services;

/// <summary>
/// FAZ 4c Puantaj / Mesai — saf (DB'siz) işçilik ve bahşiş dağıtım hesap mantığı.
/// Birim test edilebilir olması için tüm formüller buraya izole edildi (AttendanceService bunları çağırır).
/// </summary>
public static class LaborMath
{
    /// <summary>
    /// Türkiye tipik aylık normal çalışma saati (haftalık 45 saat × 52 / 12 ≈ 195; yaygın bordro kabulü).
    /// Aylık maaştan saatlik ücret türetmek için kullanılan varsayılan bölen. Çağıran override edebilir.
    /// </summary>
    public const decimal DefaultMonthlyHours = 195m;

    /// <summary>
    /// Çalışılan saat = (ClockOut − ClockIn). Çıkış yoksa (açık mesai) ya da negatif/sıfır aralıkta 0.
    /// Sonuç 2 haneye yuvarlanır.
    /// </summary>
    public static decimal Hours(DateTime clockIn, DateTime? clockOut)
    {
        if (clockOut is not DateTime outAt) return 0m;
        var span = outAt - clockIn;
        if (span <= TimeSpan.Zero) return 0m;
        return decimal.Round((decimal)span.TotalHours, 2);
    }

    /// <summary>
    /// Saatlik ücret = aylıkMaaş / standartAylıkSaat. standartAylıkSaat ≤ 0 ise 0. Sonuç 4 haneye yuvarlanır.
    /// </summary>
    public static decimal HourlyRate(decimal monthlySalary, decimal standardMonthlyHours)
        => standardMonthlyHours <= 0 ? 0m : decimal.Round(monthlySalary / standardMonthlyHours, 4);

    /// <summary>İşçilik maliyeti = saatlikÜcret × çalışılanSaat. Sonuç 2 haneye yuvarlanır.</summary>
    public static decimal LaborCost(decimal hourlyRate, decimal hoursWorked)
        => decimal.Round(hourlyRate * hoursWorked, 2);

    /// <summary>Labor-cost % = işçilik / ciro × 100. Ciro ≤ 0 ise 0. Sonuç 2 haneye yuvarlanır.</summary>
    public static decimal LaborCostPercent(decimal laborCost, decimal revenue)
        => revenue <= 0 ? 0m : decimal.Round(laborCost / revenue * 100m, 2);

    /// <summary>
    /// Bahşiş havuzu dağıtımı: <paramref name="pool"/> tutarını <paramref name="weights"/> (ör. çalışılan saat)
    /// oranında paylaştırır. Kurallar:
    ///  - Ağırlık toplamı ≤ 0 ise tüm paylar 0 (bölünmez).
    ///  - Her pay 2 haneye yuvarlanır; yuvarlama artığı (pool − Σpay) EN BÜYÜK ağırlıklı paya eklenir
    ///    → toplam tam olarak <paramref name="pool"/> olur (kuruş kaçağı olmaz).
    /// Negatif ağırlıklar 0 kabul edilir.
    /// </summary>
    public static decimal[] DistributeTips(decimal pool, IReadOnlyList<decimal> weights)
    {
        int n = weights?.Count ?? 0;
        var result = new decimal[n];
        if (n == 0) return result;

        decimal total = 0m;
        for (int i = 0; i < n; i++)
            if (weights![i] > 0) total += weights[i];

        if (total <= 0m) return result;   // dağıtılamaz → hepsi 0

        decimal allocated = 0m;
        int maxIdx = 0;
        decimal maxW = decimal.MinValue;
        for (int i = 0; i < n; i++)
        {
            decimal w = weights![i] > 0 ? weights[i] : 0m;
            result[i] = decimal.Round(pool * w / total, 2);
            allocated += result[i];
            if (w > maxW) { maxW = w; maxIdx = i; }
        }

        // Yuvarlama artığını en büyük paya yansıt → Σ = pool
        result[maxIdx] += pool - allocated;
        return result;
    }
}
