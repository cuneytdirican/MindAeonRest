namespace MindAeonRest.Models;

/// <summary>Bir ürünün belirli şubede hangi depodan tüketileceği (reçete/stok düşümü için).</summary>
public class ProductBranchConsume
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int? ConsumeWarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    public string WarehouseText => WarehouseName ?? "—";
}
