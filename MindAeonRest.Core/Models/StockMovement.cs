namespace MindAeonRest.Models;

public enum StockMovementType
{
    Satis = 0,        // satış çıkışı (reçete tüketimi)
    SatisIptal = 1,   // satış iptali / iade (geri giriş)
    Giris = 2,
    Cikis = 3,
    Sayim = 4,
    Transfer = 5,
    Uretim = 6,
    Fire = 7
}

public static class StockMovementTypeExtensions
{
    public static string ToTr(this StockMovementType t) => t switch
    {
        StockMovementType.Satis => "Satış",
        StockMovementType.SatisIptal => "Satış İptal",
        StockMovementType.Giris => "Giriş",
        StockMovementType.Cikis => "Çıkış",
        StockMovementType.Sayim => "Sayım",
        StockMovementType.Transfer => "Transfer",
        StockMovementType.Uretim => "Üretim",
        StockMovementType.Fire => "Fire",
        _ => t.ToString()
    };
}

/// <summary>Stok fişi başlığı (bir gönderim/işlem için stok hareketlerini gruplar).</summary>
public class StFiche : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int? OrderId { get; set; }
    public StockMovementType FicheType { get; set; }
    public DateTime FicheDate { get; set; }
}

/// <summary>Stok hareketi (envanter defteri satırı). Adisyon satırından reçeteyle türetilir.</summary>
public class StockMovement : AuditableEntity
{
    public int? StFicheId { get; set; }
    public int? OrderLineId { get; set; }
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Qty { get; set; }          // işaretli (- çıkış, + giriş)
    public StockMovementType MovementType { get; set; }
    public int BranchId { get; set; }
    public DateTime MoveDate { get; set; }
    public decimal? UnitCost { get; set; }

    public string TypeText => MovementType.ToTr();
    public string QtyText => $"{Qty:0.###}";
    public string DateText => MoveDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
