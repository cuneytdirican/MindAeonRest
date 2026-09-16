namespace MindAeonRest.Models;

/// <summary>Menü matrisi sınıfı: popülerlik (ort. üstü/altı) × kârlılık (ort. üstü/altı).</summary>
public enum MenuClass
{
    Star,       // Yıldız  — yüksek popülerlik + yüksek kârlılık
    Plowhorse,  // İnek    — yüksek popülerlik + düşük kârlılık (çok satar, az kazandırır)
    Puzzle,     // Bilmece — düşük popülerlik + yüksek kârlılık (kazandırır ama az satar)
    Dog         // Köpek   — düşük popülerlik + düşük kârlılık
}

/// <summary>Menü mühendisliği: bir ürünün satış + maliyet + katkı-marjı + matris sınıfı.</summary>
public class MenuItemStat
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Qty { get; set; }              // net satılan adet (iade düşülmüş)
    public decimal Revenue { get; set; }          // net ciro (iskonto düşülmüş)
    public decimal UnitCost { get; set; }          // birim maliyet (CostService)
    public decimal TotalCost => decimal.Round(UnitCost * Qty, 2);
    public decimal ContributionMargin => decimal.Round(Revenue - TotalCost, 2); // katkı marjı (tutar)
    public decimal UnitMargin => Qty > 0 ? decimal.Round(ContributionMargin / Qty, 4) : 0m; // birim katkı marjı
    public decimal MarginPercent => Revenue == 0 ? 0m : decimal.Round(ContributionMargin / Revenue * 100m, 2);
    public decimal PopularityShare { get; set; }   // adet payı % (toplam adete oranı)
    public MenuClass MenuClass { get; set; }

    public string ClassLabel => Services.MenuMath.Label(MenuClass);
    public string ClassColor => Services.MenuMath.Color(MenuClass);

    public string QtyText => Qty.ToString("0.##");
    public string RevenueText => $"₺{Revenue:N2}";
    public string CostText => $"₺{TotalCost:N2}";
    public string MarginText => $"₺{ContributionMargin:N2}";
    public string MarginPercentText => $"%{MarginPercent:N1}";
}

/// <summary>Menü mühendisliği raporu: tarih aralığı + şube için ürün matrisi + eşikler + toplamlar.</summary>
public class MenuEngineeringReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int BranchId { get; set; }
    public List<MenuItemStat> Items { get; set; } = new();

    public decimal AvgQty { get; set; }         // popülerlik eşiği (ort. adet)
    public decimal AvgUnitMargin { get; set; }  // kârlılık eşiği (ort. birim katkı marjı)

    public decimal TotalRevenue => Items.Sum(i => i.Revenue);
    public decimal TotalCost => Items.Sum(i => i.TotalCost);
    public decimal TotalMargin => Items.Sum(i => i.ContributionMargin);
    public decimal TotalQty => Items.Sum(i => i.Qty);

    public int StarCount => Items.Count(i => i.MenuClass == MenuClass.Star);
    public int PlowhorseCount => Items.Count(i => i.MenuClass == MenuClass.Plowhorse);
    public int PuzzleCount => Items.Count(i => i.MenuClass == MenuClass.Puzzle);
    public int DogCount => Items.Count(i => i.MenuClass == MenuClass.Dog);
}

/// <summary>Gün×saat ısı haritası: hafta günü (0=Pzt … 6=Paz) × saat (0-23). Hücre = ciro + adisyon adedi.</summary>
public class SalesHeatmap
{
    public static readonly string[] DayNames = { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };

    public decimal[,] Revenue { get; } = new decimal[7, 24];
    public int[,] Count { get; } = new int[7, 24];
    public decimal MaxRevenue { get; private set; }

    public void Add(int weekday, int hour, decimal revenue, int count)
    {
        if (weekday < 0 || weekday > 6 || hour < 0 || hour > 23) return;
        Revenue[weekday, hour] += revenue;
        Count[weekday, hour] += count;
        if (Revenue[weekday, hour] > MaxRevenue) MaxRevenue = Revenue[weekday, hour];
    }

    /// <summary>Hücre yoğunluğu 0..1 (renk için); MaxRevenue 0 ise 0.</summary>
    public double Intensity(int weekday, int hour)
        => MaxRevenue > 0 ? (double)(Revenue[weekday, hour] / MaxRevenue) : 0;
}

/// <summary>Basit talep tahmini: hareketli ortalama + haftalık mevsimsellik ile ertesi gün/hafta ciro tahmini.</summary>
public class SalesForecast
{
    public int LookbackDays { get; set; }
    public int Window { get; set; }
    public decimal AvgDailyRevenue { get; set; }
    public decimal NextDayRevenue { get; set; }
    public decimal Next7DaysTotal { get; set; }
    public List<ForecastPoint> Next7Days { get; set; } = new();

    public string NextDayText => $"₺{NextDayRevenue:N0}";
    public string Next7Text => $"₺{Next7DaysTotal:N0}";
}

/// <summary>Tahmin noktası (tarih + gün adı + tahmini ciro).</summary>
public class ForecastPoint
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = "";
    public decimal Revenue { get; set; }
    public string RevenueText => $"₺{Revenue:N0}";
}
