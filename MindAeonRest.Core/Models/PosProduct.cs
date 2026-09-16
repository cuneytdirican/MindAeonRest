using System;
using System.Collections.Generic;
using System.Linq;

namespace MindAeonRest.Models;

/// <summary>Adisyon ekranı için ürün kartı: fiyat çözülmüş, resim, satış-durumu, kategori.</summary>
public class PosProduct
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsSalesStopped { get; set; }
    public bool IsMenu { get; set; }
    public byte[]? Image { get; set; }
    public HashSet<int> CategoryIds { get; set; } = new();
    public List<ProductOption> Options { get; set; } = new();   // varyant/seçenekler
    public List<PosPrice> PriceCandidates { get; set; } = new();

    public string PriceText => $"₺{Price:N2}";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasOptions => Options.Count > 0;

    /// <summary>Şube + (opsiyonel) fiyat listesine göre geçerli satış fiyatını seçer.
    /// Öncelik: ŞUBE ÖZEL (liste yok) > şube+seçili liste > genel seçili liste > genel.
    /// Yani şubeye özel fiyat, fiyat listesinden ÖNCELİKLİDİR.</summary>
    public void ResolvePrice(int branchId, int? priceListId, DateTime now)
    {
        bool V(PosPrice x) => (x.Start == null || x.Start <= now) && (x.End == null || x.End >= now);
        var c = PriceCandidates.Where(V).ToList();
        PosPrice? pick =
            c.FirstOrDefault(x => x.BranchId == branchId && x.PriceListId == null)
            ?? (priceListId is int l1 ? c.FirstOrDefault(x => x.BranchId == branchId && x.PriceListId == l1) : null)
            ?? (priceListId is int l2 ? c.FirstOrDefault(x => x.BranchId == null && x.PriceListId == l2) : null)
            ?? c.FirstOrDefault(x => x.BranchId == null && x.PriceListId == null);
        Price = pick?.Price ?? 0;
    }
}

/// <summary>POS fiyat adayı (şube/liste/tarih bilgisiyle, istemci tarafında çözümleme için).</summary>
public sealed class PosPrice
{
    public decimal Price { get; set; }
    public int? BranchId { get; set; }
    public int? PriceListId { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
