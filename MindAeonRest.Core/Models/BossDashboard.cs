namespace MindAeonRest.Models;

/// <summary>Patron ekranı: tarih aralığı + (tüm şubeler veya tek şube) için zengin işletme özeti.</summary>
public class BossDashboard
{
    // Ana KPI'lar
    public decimal Revenue { get; set; }          // ciro (kapanan satışlar)
    public int FisCount { get; set; }             // fiş/adisyon adedi
    public decimal AvgTicket { get; set; }        // ortalama fiş
    public decimal Discount { get; set; }         // toplam iskonto
    public decimal RefundTotal { get; set; }      // toplam iade
    public int CancelCount { get; set; }          // iptal edilen satır adedi
    public decimal Nakit { get; set; }
    public decimal Kart { get; set; }
    public decimal Diger { get; set; }
    public int OpenTables { get; set; }           // anlık doluluk (açık masa)

    public string RevenueText => $"₺{Revenue:N2}";
    public string AvgText => $"₺{AvgTicket:N2}";
    public string DiscountText => $"₺{Discount:N2}";
    public string RefundText => $"₺{RefundTotal:N2}";
    public string NakitText => $"₺{Nakit:N2}";
    public string KartText => $"₺{Kart:N2}";
    public string DigerText => $"₺{Diger:N2}";

    public List<BranchStat> Branches { get; set; } = new();      // şube kırılımı (çok şubeli)
    public List<TrendPoint> Hourly { get; set; } = new();        // saatlik yoğunluk (0-23)
    public List<TrendPoint> Daily { get; set; } = new();         // günlük ciro trendi
    public List<RankRow> TopProducts { get; set; } = new();      // en çok satan
    public List<RankRow> TopTables { get; set; } = new();        // en çok ciro masa
    public List<RankRow> TopZones { get; set; } = new();         // en çok ciro lokasyon
    public List<RankRow> Cancelled { get; set; } = new();        // en çok iptal
    public List<RankRow> Refunded { get; set; } = new();         // en çok iade
    public List<RankRow> Waiters { get; set; } = new();          // garson performansı
    public List<RankRow> Cashiers { get; set; } = new();         // kasiyer performansı
    public List<Campaign> ActiveCampaigns { get; set; } = new(); // şu an geçerli kampanyalar
    public List<RankRow> CampaignSales { get; set; } = new();    // kampanya bazlı satış (ciro + adisyon adedi)
}

public class BranchStat
{
    public int BranchId { get; set; }
    public string Name { get; set; } = "";
    public decimal Revenue { get; set; }
    public int FisCount { get; set; }
    public int OpenTables { get; set; }
    public decimal Avg => FisCount > 0 ? Math.Round(Revenue / FisCount, 2) : 0;
    public string RevenueText => $"₺{Revenue:N2}";
    public string AvgText => $"₺{Avg:N2}";
}

/// <summary>Zaman çizelgesi noktası (saat ya da gün).</summary>
public class TrendPoint
{
    public string Label { get; set; } = "";
    public decimal Value { get; set; }    // ciro
    public int Count { get; set; }        // adisyon adedi
    public double BarWidth { get; set; }  // 0-100 (UI'da çubuk oranı)
    public string ValueText => $"₺{Value:N0}";
}

/// <summary>Sıralı liste satırı (ürün/masa/garson…): ad + tutar/adet + çubuk.</summary>
public class RankRow
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public double BarWidth { get; set; }  // 0-100
    public string AmountText => $"₺{Amount:N2}";
    public string CountText => Count.ToString();
    public string Color { get; set; } = "#888";
}
