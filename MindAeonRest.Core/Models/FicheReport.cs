namespace MindAeonRest.Models;

/// <summary>Detaylı fiş/adisyon raporu satırı (kapanmış adisyonlar).</summary>
public class FicheReportRow
{
    public string FicheCode { get; set; } = "";
    public string OrderCode { get; set; } = "";
    public System.DateTime Date { get; set; }
    public string BranchName { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public string TableCodes { get; set; } = "";
    public string WaiterName { get; set; } = "";
    public string CashierName { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Discount { get; set; }
    public decimal Treat { get; set; }
    public string Payments { get; set; } = "";
    public string StatusText { get; set; } = "Kapandı";

    public string DateText => Date.ToString("dd.MM.yyyy HH:mm");
    public string TotalText => $"{Total:N2} ₺";
    public string DiscountText => $"{Discount:N2} ₺";
    public string TreatText => $"{Treat:N2} ₺";
}

/// <summary>KIOSK dashboard anlık para görüntüsü (tipe göre toplanmış).</summary>
public class KioskMoneySnapshot
{
    public decimal Nakit { get; set; }
    public decimal KrediKarti { get; set; }
    public decimal YemekCeki { get; set; }
    public decimal Havale { get; set; }
    public decimal HediyeCeki { get; set; }
    public decimal PuanSadakat { get; set; }
    public decimal AcikHesap { get; set; }
    public int FisCount { get; set; }
    public System.DateTime? LastZAt { get; set; }

    public decimal Total => Nakit + KrediKarti + YemekCeki + Havale + HediyeCeki + PuanSadakat + AcikHesap;
    public decimal Tahsilat => Nakit + KrediKarti + YemekCeki + Havale + HediyeCeki + PuanSadakat;   // açık hesap hariç gerçek tahsilat

    public string NakitText => $"₺{Nakit:N2}";
    public string KrediKartiText => $"₺{KrediKarti:N2}";
    public string YemekCekiText => $"₺{YemekCeki:N2}";
    public string HavaleText => $"₺{Havale:N2}";
    public string HediyeCekiText => $"₺{HediyeCeki:N2}";
    public string PuanSadakatText => $"₺{PuanSadakat:N2}";
    public string AcikHesapText => $"₺{AcikHesap:N2}";
    public string TahsilatText => $"₺{Tahsilat:N2}";
}

/// <summary>Z (gün sonu) raporları liste satırı.</summary>
public class ZReportRow
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public System.DateTime ReportDate { get; set; }
    public System.DateTime TakenAt { get; set; }
    public string BranchName { get; set; } = "";
    public int KioskNo { get; set; } = 1;
    public decimal SalesTotal { get; set; }
    public int FisCount { get; set; }
    public string TakenByName { get; set; } = "";

    public string DateText => ReportDate.ToString("dd.MM.yyyy");
    public string TakenAtText => TakenAt.ToString("dd.MM.yyyy HH:mm");
    public string KioskText => $"#{KioskNo}";
    public string SalesText => $"{SalesTotal:N2} ₺";
}

/// <summary>İptal raporu satırı (iptal edilen sipariş satırları, sebepli).</summary>
public class CancelReportRow
{
    public System.DateTime Date { get; set; }
    public string OrderCode { get; set; } = "";
    public string TableCodes { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "Belirtilmedi";
    public string CancelledByName { get; set; } = "";
    public string Disposition { get; set; } = "";   // Stoğa İade / Fire

    public string DateText => Date.ToString("dd.MM.yyyy HH:mm");
    public string QtyText => $"{Qty:0.##}";
    public string AmountText => $"{Amount:N2} ₺";
}
