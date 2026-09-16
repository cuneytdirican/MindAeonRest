namespace MindAeonRest.Models;

/// <summary>Evrak tipi (CodeSeriesType ile aynı sayısal değerler).</summary>
public enum DocType
{
    AlisSiparis = 10, SatisSiparis = 11,
    AlisIrsaliye = 12, SatisIrsaliye = 13,
    AlisFatura = 14, SatisFatura = 15, AlisIadeFatura = 16, SatisIadeFatura = 17,
    DepoTransfer = 18, Sayim = 19, Fire = 20, Gider = 21
}

public enum DocStatus { Taslak = 0, Onaylandi = 1, KismenKapandi = 2, Kapandi = 3, Iptal = 9 }

public static class DocTypeExtensions
{
    public static string ToTr(this DocType t) => t switch
    {
        DocType.AlisSiparis => "Alış Siparişi",
        DocType.SatisSiparis => "Satış Siparişi",
        DocType.AlisIrsaliye => "Alış İrsaliyesi",
        DocType.SatisIrsaliye => "Satış İrsaliyesi",
        DocType.AlisFatura => "Alış Faturası",
        DocType.SatisFatura => "Satış Faturası",
        DocType.AlisIadeFatura => "Alış İade Faturası",
        DocType.SatisIadeFatura => "Satış İade Faturası",
        DocType.DepoTransfer => "Depo Transfer",
        DocType.Sayim => "Sayım Fişi",
        DocType.Fire => "Fire Fişi",
        DocType.Gider => "Gider Fişi",
        _ => t.ToString()
    };

    public static string ToTr(this DocStatus s) => s switch
    {
        DocStatus.Onaylandi => "Onaylandı",
        DocStatus.KismenKapandi => "Kısmen Kapandı",
        DocStatus.Kapandi => "Kapandı",
        DocStatus.Iptal => "İptal",
        _ => "Taslak"
    };

    /// <summary>Bu evrak cari hareket çalıştırır mı? (sadece faturalar)</summary>
    public static bool RunsCari(this DocType t) =>
        t is DocType.AlisFatura or DocType.SatisFatura or DocType.AlisIadeFatura or DocType.SatisIadeFatura;

    /// <summary>Bu evrak stok hareketi çalıştırır mı? (irsaliye/transfer/sayım/fire + irsaliyesiz fatura)</summary>
    public static bool RunsStock(this DocType t) =>
        t is DocType.AlisIrsaliye or DocType.SatisIrsaliye or DocType.DepoTransfer or DocType.Sayim or DocType.Fire
          or DocType.AlisFatura or DocType.SatisFatura or DocType.AlisIadeFatura or DocType.SatisIadeFatura;

    /// <summary>Stok yönü: +1 giriş (alış), -1 çıkış (satış/fire). 0 = nötr/özel (sayım/transfer).</summary>
    public static int StockSign(this DocType t) => t switch
    {
        DocType.AlisIrsaliye or DocType.AlisFatura or DocType.SatisIadeFatura => +1,
        DocType.SatisIrsaliye or DocType.SatisFatura or DocType.AlisIadeFatura or DocType.Fire => -1,
        _ => 0
    };

    public static bool NeedsCustomer(this DocType t) => t is not (DocType.DepoTransfer or DocType.Sayim or DocType.Fire);

    /// <summary>Bu evrak hangi kaynaktan oluşturulabilir (hiyerarşi). null = kaynaktan oluşturulamaz.</summary>
    public static DocType? SourceType(this DocType t) => t switch
    {
        DocType.AlisIrsaliye => DocType.AlisSiparis,
        DocType.SatisIrsaliye => DocType.SatisSiparis,
        DocType.AlisFatura => DocType.AlisIrsaliye,
        DocType.SatisFatura => DocType.SatisIrsaliye,
        _ => null
    };
}

/// <summary>Evrak başlığı (tek genel motor).</summary>
public class Document : AuditableEntity
{
    public DocType DocType { get; set; }
    public string FisNo { get; set; } = string.Empty;
    public string? BelgeNo { get; set; }
    public DateTime DocDate { get; set; } = DateTime.Now;   // dakika hassas
    public DateTime? BelgeTarihi { get; set; }
    public int BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public int? TargetBranchId { get; set; }
    public int? TargetWarehouseId { get; set; }
    public int? ProjectId { get; set; }
    public int? CustomerId { get; set; }
    public int? AddressId { get; set; }
    public int? PersonnelId { get; set; }
    public int? ExpenseTypeId { get; set; }
    public int CurrencyId { get; set; }
    public decimal? UsdRate { get; set; }
    public decimal? EurRate { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Taslak;
    public string? Note { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }

    // gösterim
    public string? CustomerName { get; set; }
    public string? BranchName { get; set; }
    public string? ProjectCode { get; set; }
    public string DocTypeText => DocType.ToTr();
    public string StatusText => Status.ToTr();
    public string DateText => DocDate.ToString("dd.MM.yyyy HH:mm");
    public string GrandTotalText => $"{GrandTotal:N2}";

    public void Recalc(IEnumerable<DocumentLine> lines)
    {
        SubTotal = DiscountTotal = TaxTotal = GrandTotal = 0;
        foreach (var l in lines)
        {
            l.Recalc();
            SubTotal += l.LineNet;
            DiscountTotal += l.DiscountAmount;
            TaxTotal += l.LineTax;
            GrandTotal += l.LineTotal;
        }
        SubTotal = Math.Round(SubTotal, 2);
        DiscountTotal = Math.Round(DiscountTotal, 2);
        TaxTotal = Math.Round(TaxTotal, 2);
        GrandTotal = Math.Round(GrandTotal, 2);
    }
}

/// <summary>Evrak satırı: 10 ardışık iskonto, KDV grup oranı; KDV matrah üstüne eklenir.</summary>
public class DocumentLine : AuditableEntity
{
    public int DocumentId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public string? Description { get; set; }
    public decimal Qty { get; set; }
    public int? UnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public int LineCurrencyId { get; set; }
    public decimal Disc1 { get; set; }
    public decimal Disc2 { get; set; }
    public decimal Disc3 { get; set; }
    public decimal Disc4 { get; set; }
    public decimal Disc5 { get; set; }
    public decimal Disc6 { get; set; }
    public decimal Disc7 { get; set; }
    public decimal Disc8 { get; set; }
    public decimal Disc9 { get; set; }
    public decimal Disc10 { get; set; }
    public int? TaxGroupId { get; set; }
    public decimal TaxRate { get; set; }

    public decimal LineGross { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineNet { get; set; }
    public decimal LineTax { get; set; }
    public decimal LineTotal { get; set; }
    public decimal ReceivedQty { get; set; }   // depo transfer teslim onayı

    // gösterim
    public string? ProductName { get; set; }
    public string? ProductCode { get; set; }
    public string? UnitName { get; set; }

    public IEnumerable<decimal> Discounts => new[] { Disc1, Disc2, Disc3, Disc4, Disc5, Disc6, Disc7, Disc8, Disc9, Disc10 };

    /// <summary>Satırdaki uygulanan iskontoların zinciri (10'a kadar), ör. "%10 +%5 +%2".</summary>
    public string DiscountChainText
    {
        get { var ds = Discounts.Where(d => d > 0).Select(d => $"%{d:0.##}").ToList(); return ds.Count > 0 ? string.Join(" +", ds) : "—"; }
    }

    public void Recalc()
    {
        decimal gross = Qty * UnitPrice;
        decimal net = gross;
        foreach (var d in Discounts)
            if (d > 0) net *= (1 - d / 100m);
        LineGross = Math.Round(gross, 2);
        LineNet = Math.Round(net, 2);
        DiscountAmount = LineGross - LineNet;   // yuvarlanmış değerlerden türet (tutarlılık)
        LineTax = Math.Round(LineNet * TaxRate / 100m, 2);
        LineTotal = LineNet + LineTax;
    }
}

/// <summary>Sipariş→İrsaliye→Fatura satır bağlantısı.</summary>
public class DocumentLink : AuditableEntity
{
    public int DocumentId { get; set; }
    public int? DocumentLineId { get; set; }
    public int SourceDocumentId { get; set; }
    public int? SourceLineId { get; set; }
    public decimal Qty { get; set; }
}
