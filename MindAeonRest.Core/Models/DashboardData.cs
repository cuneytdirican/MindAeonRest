namespace MindAeonRest.Models;

/// <summary>Şube özet (dashboard) verisi — gün bazlı.</summary>
public class DashboardData
{
    public decimal SalesTotal { get; set; }
    public decimal Nakit { get; set; }
    public decimal Kart { get; set; }
    public decimal Diger { get; set; }
    public int AdisyonCount { get; set; }
    public decimal KasaTotal { get; set; }
    public decimal Average { get; set; }
    public List<TopProduct> TopProducts { get; set; } = new();

    // ERP KPI'ları
    public decimal Receivable { get; set; }    // toplam alacak (cari bize borçlu)
    public decimal Payable { get; set; }        // toplam borç (biz cariye borçlu)
    public decimal TodayExpense { get; set; }   // bugünkü gider
    public decimal BankTotal { get; set; }      // toplam banka bakiyesi
    public decimal GiftCardLiability { get; set; }   // dolaşımdaki hediye çeki yükümlülüğü
    public decimal LoyaltyLiability { get; set; }    // toplam puan yükümlülüğü (₺)

    public string ReceivableText => $"₺{Receivable:N2}";
    public string PayableText => $"₺{Payable:N2}";
    public string TodayExpenseText => $"₺{TodayExpense:N2}";
    public string BankText => $"₺{BankTotal:N2}";
    public string GiftCardLiabilityText => $"₺{GiftCardLiability:N2}";
    public string LoyaltyLiabilityText => $"₺{LoyaltyLiability:N2}";

    public string SalesText => $"₺{SalesTotal:N2}";
    public string NakitText => $"₺{Nakit:N2}";
    public string KartText => $"₺{Kart:N2}";
    public string DigerText => $"₺{Diger:N2}";
    public string KasaText => $"₺{KasaTotal:N2}";
    public string AvgText => $"₺{Average:N2}";
}

public class TopProduct
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percent { get; set; }
    public string Color { get; set; } = "#888";
    public string PercentText => $"%{Percent:0.#}";
}
