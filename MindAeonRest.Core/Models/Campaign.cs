using System;
using System.Collections.Generic;

namespace MindAeonRest.Models;

/// <summary>Kampanya tipi.</summary>
public enum CampaignType
{
    TumSiparisYuzde = 0,   // tüm siparişe % indirim
    TumSiparisTutar = 1,   // tüm siparişe sabit tutar indirim
    KategoriYuzde = 2,     // belirli kategoriye % indirim
    UrunYuzde = 3,         // belirli ürüne % indirim
    MinTutarYuzde = 4,     // min tutar üzerine % indirim
    MinTutarTutar = 5,     // min tutar üzerine sabit tutar indirim
    XAlYOde = 6,           // bir üründen X al Y öde
    Hediye = 7             // koşul(lar) sağlanınca ürün İKRAMI (hediye): min tutar ve/veya ürün-miktar koşulları
}

public static class CampaignTypeExtensions
{
    public static string ToTr(this CampaignType t) => t switch
    {
        CampaignType.TumSiparisYuzde => "Tüm Siparişe % İndirim",
        CampaignType.TumSiparisTutar => "Tüm Siparişe Tutar İndirim",
        CampaignType.KategoriYuzde => "Kategoriye % İndirim",
        CampaignType.UrunYuzde => "Ürüne % İndirim",
        CampaignType.MinTutarYuzde => "Min. Tutar Üzeri % İndirim",
        CampaignType.MinTutarTutar => "Min. Tutar Üzeri Tutar İndirim",
        CampaignType.XAlYOde => "X Al Y Öde",
        CampaignType.Hediye => "Hediye / İkram Ürün",
        _ => t.ToString()
    };
}

/// <summary>Hediye kampanyası koşulu: bu üründen en az MinQty alınmalı (birden çok koşul = HEPSİ sağlanmalı).</summary>
public class CampaignCondition
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public int ProductId { get; set; }
    public decimal MinQty { get; set; } = 1;
    public string? ProductName { get; set; }
    public string Display => $"{ProductName} ≥ {MinQty:0.##}";
}

/// <summary>Hediye kampanyasında verilecek ikram ürün ve miktarı.</summary>
public class CampaignGift
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public int ProductId { get; set; }
    public decimal Qty { get; set; } = 1;
    public string? ProductName { get; set; }
    public string Display => $"{Qty:0.##} x {ProductName}";
}

/// <summary>Gelişmiş kampanya (şirket DB). Şube bazlı + tarih/saat/gün aralıklı + aktif/pasif.</summary>
public class Campaign : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CampaignType Type { get; set; } = CampaignType.TumSiparisYuzde;
    public decimal Value { get; set; }                 // % ya da tutar (tipe göre)

    public int? TargetCategoryId { get; set; }         // KategoriYuzde
    public int? TargetProductId { get; set; }          // UrunYuzde / XAlYOde
    public decimal MinAmount { get; set; }             // MinTutar*
    public decimal BuyQty { get; set; }                // XAlYOde: kaç al
    public decimal PayQty { get; set; }                // XAlYOde: kaç öde

    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public int DaysOfWeek { get; set; }                // bit maske (0=her gün); Pzt=1,Sal=2,Çar=4,Per=8,Cum=16,Cmt=32,Paz=64
    public int? DailyStartMin { get; set; }            // gün içi başlangıç (dk; null=gün boyu) — happy hour
    public int? DailyEndMin { get; set; }
    public bool AllBranches { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool Stackable { get; set; } = false;       // başka kampanyalarla birleştirilebilir mi (aynı adisyonda)
    public bool IsMandatory { get; set; } = false;     // ZORUNLU: kriter karşılanınca adisyonda OTOMATİK uygulanır

    // gösterim / yardımcı
    public string? TargetCategoryName { get; set; }
    public string? TargetProductName { get; set; }
    public List<int> BranchIds { get; set; } = new();  // AllBranches=false ise
    public List<CampaignCondition> Conditions { get; set; } = new();  // Hediye: koşullar (HEPSİ sağlanmalı)
    public List<CampaignGift> Gifts { get; set; } = new();            // Hediye: verilecek ikram ürün(ler)

    public string TypeText => Type.ToTr();
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ValueText => Type switch
    {
        CampaignType.TumSiparisTutar or CampaignType.MinTutarTutar => $"{Value:N2} ₺",
        CampaignType.XAlYOde => $"{BuyQty:0.##} al {PayQty:0.##} öde",
        CampaignType.Hediye => Gifts.Count == 0 ? "İkram" : "İkram: " + string.Join(", ", Gifts.Select(g => g.Display)),
        _ => $"%{Value:0.##}"
    };
    public string PeriodText => $"{StartDateTime:dd.MM.yy} - {EndDateTime:dd.MM.yy}";
    public string ScopeText => AllBranches ? "Tüm şubeler" : $"{BranchIds.Count} şube";

    /// <summary>Seçilen tipin ne işe yaradığının açıklaması (kullanıcı doğru tipi seçsin diye).</summary>
    public static string TypeDescription(CampaignType t) => t switch
    {
        CampaignType.TumSiparisYuzde => "Tüm adisyon tutarına yüzde indirim uygular. Örn: %10 → 200₺'lik hesap 180₺.",
        CampaignType.TumSiparisTutar => "Tüm adisyona sabit tutar indirim uygular. Örn: 25₺ → 200₺'lik hesap 175₺.",
        CampaignType.KategoriYuzde => "Seçilen KATEGORİDEKİ ürünlere yüzde indirim. Örn: 'İçecekler' %15.",
        CampaignType.UrunYuzde => "Seçilen ÜRÜNE yüzde indirim. Örn: 'Filtre Kahve' %20.",
        CampaignType.MinTutarYuzde => "Adisyon belirtilen MİN. TUTARI geçerse yüzde indirim. Örn: 150₺ üzeri %10.",
        CampaignType.MinTutarTutar => "Adisyon MİN. TUTARI geçerse sabit tutar indirim. Örn: 150₺ üzeri 20₺.",
        CampaignType.XAlYOde => "Bir üründen X adet alana Y adet ödetir (kalanı bedava). Örn: 3 al 2 öde.",
        CampaignType.Hediye => "Koşullar (min tutar ve/veya ürün-miktar) sağlanınca ikram ürün(ler) ekler. Örn: 100₺ üzeri 1 Çay ikram; ya da 2 Pizza alana 1 Kola.",
        _ => ""
    };

    /// <summary>Kullanıcının tanımladığı kampanyanın insan-okur özeti (gerçekten istediğini tanımlamış mı?).</summary>
    public string Summary
    {
        get
        {
            string scope = AllBranches ? "tüm şubelerde" : $"{BranchIds.Count} seçili şubede";
            string when = $"{StartDateTime:dd.MM.yyyy}–{EndDateTime:dd.MM.yyyy}";
            var days = new[] { (1, "Pzt"), (2, "Sal"), (4, "Çar"), (8, "Per"), (16, "Cum"), (32, "Cmt"), (64, "Paz") }
                .Where(d => (DaysOfWeek & d.Item1) != 0).Select(d => d.Item2).ToList();
            string dayTxt = DaysOfWeek == 0 ? "her gün" : string.Join(",", days);
            string hour = DailyStartMin is int ds && DailyEndMin is int de ? $", {ds / 60:00}:{ds % 60:00}-{de / 60:00}:{de % 60:00} arası" : "";
            string what = Type switch
            {
                CampaignType.TumSiparisYuzde => $"tüm adisyona %{Value:0.##} indirim",
                CampaignType.TumSiparisTutar => $"tüm adisyona {Value:N2}₺ indirim",
                CampaignType.KategoriYuzde => $"'{TargetCategoryName}' kategorisine %{Value:0.##} indirim",
                CampaignType.UrunYuzde => $"'{TargetProductName}' ürününe %{Value:0.##} indirim",
                CampaignType.MinTutarYuzde => $"{MinAmount:N2}₺ üzeri siparişe %{Value:0.##} indirim",
                CampaignType.MinTutarTutar => $"{MinAmount:N2}₺ üzeri siparişe {Value:N2}₺ indirim",
                CampaignType.XAlYOde => $"'{TargetProductName}' üründe {BuyQty:0.##} al {PayQty:0.##} öde",
                CampaignType.Hediye => HediyeSummary(),
                _ => ""
            };
            string stack = Stackable ? " · başka kampanyalarla birleştirilebilir" : " · tek başına uygulanır";
            return $"Bu kampanya {scope}, {when} ({dayTxt}{hour}) tarihinde: {what}.{stack}";
        }
    }

    private string HediyeSummary()
    {
        string cond = MinAmount > 0 ? $"{MinAmount:N2}₺ üzeri" : "";
        if (Conditions.Count > 0) cond += (cond.Length > 0 ? " ve " : "") + string.Join(" ve ", Conditions.Select(c => c.Display));
        if (cond.Length == 0) cond = "koşulsuz";
        string gifts = Gifts.Count == 0 ? "(ikram tanımsız)" : string.Join(", ", Gifts.Select(g => g.Display));
        return $"{cond} alana {gifts} İKRAM";
    }

    /// <summary>Şu an + verilen şube için geçerli mi? (aktif, tarih, gün, saat, şube)</summary>
    public bool IsValidNow(int branchId, DateTime now)
    {
        if (!IsActive) return false;
        if (now < StartDateTime || now > EndDateTime) return false;
        if (DaysOfWeek != 0)
        {
            int bit = now.DayOfWeek switch
            {
                DayOfWeek.Monday => 1, DayOfWeek.Tuesday => 2, DayOfWeek.Wednesday => 4,
                DayOfWeek.Thursday => 8, DayOfWeek.Friday => 16, DayOfWeek.Saturday => 32, _ => 64
            };
            if ((DaysOfWeek & bit) == 0) return false;
        }
        if (DailyStartMin is int ds && DailyEndMin is int de)
        {
            int mins = now.Hour * 60 + now.Minute;
            if (ds <= de) { if (mins < ds || mins > de) return false; }
            else { if (mins < ds && mins > de) return false; }   // gece yarısını aşan aralık
        }
        if (!AllBranches && !BranchIds.Contains(branchId)) return false;
        return true;
    }
}
