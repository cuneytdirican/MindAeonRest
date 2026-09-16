namespace MindAeonRest.Models;

/// <summary>Bir şube için ürünün satış durumu (şube bazlı satış durdurma ekranı).</summary>
public class ProductSalesStatus
{
    public int ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSalesStopped { get; set; }

    public string StatusText => IsSalesStopped ? "DURDURULDU" : "Satışta";
}
