namespace MindAeonRest.Models;

/// <summary>Otomatik kod serisinin uygulandığı kart tipi.</summary>
public enum CodeSeriesType
{
    Product = 1,
    Customer = 2,
    // Evrak tipleri (DocType ile aynı sayısal değerler)
    AlisSiparis = 10,
    SatisSiparis = 11,
    AlisIrsaliye = 12,
    SatisIrsaliye = 13,
    AlisFatura = 14,
    SatisFatura = 15,
    AlisIadeFatura = 16,
    SatisIadeFatura = 17,
    DepoTransfer = 18,
    Sayim = 19,
    Fire = 20,
    Gider = 21
}

public static class CodeSeriesTypeExtensions
{
    public static string ToTr(this CodeSeriesType t) => t switch
    {
        CodeSeriesType.Product => "Stok Kartı",
        CodeSeriesType.Customer => "Müşteri (Cari)",
        CodeSeriesType.AlisSiparis => "Alış Siparişi",
        CodeSeriesType.SatisSiparis => "Satış Siparişi",
        CodeSeriesType.AlisIrsaliye => "Alış İrsaliyesi",
        CodeSeriesType.SatisIrsaliye => "Satış İrsaliyesi",
        CodeSeriesType.AlisFatura => "Alış Faturası",
        CodeSeriesType.SatisFatura => "Satış Faturası",
        CodeSeriesType.AlisIadeFatura => "Alış İade Faturası",
        CodeSeriesType.SatisIadeFatura => "Satış İade Faturası",
        CodeSeriesType.DepoTransfer => "Depo Transfer",
        CodeSeriesType.Sayim => "Sayım Fişi",
        CodeSeriesType.Fire => "Fire Fişi",
        CodeSeriesType.Gider => "Gider Fişi",
        _ => t.ToString()
    };

    /// <summary>Seri tanımlanmamışsa kullanılacak varsayılan ön ek.</summary>
    public static string DefaultPrefix(this CodeSeriesType t) => t switch
    {
        CodeSeriesType.AlisSiparis => "AS",
        CodeSeriesType.SatisSiparis => "SS",
        CodeSeriesType.AlisIrsaliye => "AIR",
        CodeSeriesType.SatisIrsaliye => "SIR",
        CodeSeriesType.AlisFatura => "AF",
        CodeSeriesType.SatisFatura => "SF",
        CodeSeriesType.AlisIadeFatura => "AIF",
        CodeSeriesType.SatisIadeFatura => "SIF",
        CodeSeriesType.DepoTransfer => "DT",
        CodeSeriesType.Sayim => "SY",
        CodeSeriesType.Fire => "FR",
        CodeSeriesType.Gider => "GD",
        CodeSeriesType.Product => "STK",
        CodeSeriesType.Customer => "CR",
        _ => "DOC"
    };
}

/// <summary>
/// Kod serisi: belirli kart tipi için ön ek + dolgu + sıradaki numara.
/// Yeni kart açılışında bir sonraki kod otomatik önerilir, kayıtta numara artar.
/// </summary>
public class CodeSeries : AuditableEntity
{
    public CodeSeriesType EntityType { get; set; }
    public int? BranchId { get; set; }          // null = firma varsayılanı (tüm şubeler)
    public string? Prefix { get; set; }
    public int Padding { get; set; } = 6;
    public int NextNumber { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public string? BranchName { get; set; }     // gösterim
    public string BranchText => BranchName ?? "(Varsayılan)";
    public string EntityTypeText => EntityType.ToTr();
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string NextCodePreview => Format(NextNumber);
    public string Format(int number) => $"{Prefix}{number.ToString().PadLeft(Math.Max(1, Padding), '0')}";
}
