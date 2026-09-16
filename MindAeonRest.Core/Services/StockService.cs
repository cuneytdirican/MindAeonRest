using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Stok bakiye satırı (ürün+depo) + birim çevrim bilgisi.</summary>
public class StockBalance
{
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Qty { get; set; }
    public string? MainUnit { get; set; }
    // alternatif birim çevrimi
    public string? AltUnit { get; set; }
    public decimal? AltQty { get; set; }
}

/// <summary>Stok bakiye/hareket raporları (birim çevrimli).</summary>
public class StockService
{
    /// <summary>Ürün+depo bazlı stok bakiyesi. Birim çevrimi: 1 ana birim = (Conv2/Conv1) alt birim.</summary>
    public List<StockBalance> GetBalances(int? branchId = null, int? warehouseId = null)
    {
        var alt = GetAltUnitMap();
        var list = new List<StockBalance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT p.Id, p.Code, p.Name, sm.WarehouseId, w.Name AS WhName, SUM(sm.Qty) AS Qty, mu.Name AS MainUnit
FROM dbo.StockMovements sm
JOIN dbo.Products p ON p.Id = sm.ProductId
LEFT JOIN dbo.Warehouses w ON w.Id = sm.WarehouseId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = p.MainUnitId
WHERE (@br IS NULL OR sm.BranchId=@br) AND (@wh IS NULL OR sm.WarehouseId=@wh)
GROUP BY p.Id, p.Code, p.Name, sm.WarehouseId, w.Name, mu.Name
HAVING SUM(sm.Qty) <> 0
ORDER BY p.Code, w.Name;";
        cmd.Parameters.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wh", (object?)warehouseId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var b = new StockBalance
            {
                ProductId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                WarehouseId = r.GetInt32(3), WarehouseName = r.IsDBNull(4) ? null : r.GetString(4),
                Qty = r.GetDecimal(5), MainUnit = r.IsDBNull(6) ? null : r.GetString(6)
            };
            if (alt.TryGetValue(b.ProductId, out var a) && a.F1 != 0)
            {
                b.AltUnit = a.Unit;
                b.AltQty = Math.Round(b.Qty * a.F2 / a.F1, 3);   // ana → alt
            }
            list.Add(b);
        }
        return list;
    }

    private Dictionary<int, (string Unit, decimal F1, decimal F2)> GetAltUnitMap()
    {
        var map = new Dictionary<int, (string, decimal, decimal)>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT pu.ProductId, mu.Name, pu.ConversionFactor1, pu.ConversionFactor2
FROM dbo.ProductUnits pu
JOIN [{AppConfig.DatabaseName}].dbo.MainUnits mu ON mu.Id = pu.UnitId
WHERE pu.IsMainUnit = 0 AND pu.IsDeleted = 0
ORDER BY pu.ProductId, pu.Id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int pid = r.GetInt32(0);
            if (!map.ContainsKey(pid))   // ilk alternatif birim
                map[pid] = (r.GetString(1), r.GetDecimal(2), r.GetDecimal(3));
        }
        return map;
    }

    public List<StockMovement> GetMovements(DateTime? from, DateTime? to, int? warehouseId = null, int? branchId = null)
    {
        var list = new List<StockMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT sm.Id, sm.ProductId, p.Name, sm.WarehouseId, w.Name, sm.Qty, sm.MovementType, sm.BranchId, sm.MoveDate
FROM dbo.StockMovements sm
JOIN dbo.Products p ON p.Id = sm.ProductId
LEFT JOIN dbo.Warehouses w ON w.Id = sm.WarehouseId
WHERE (@from IS NULL OR sm.MoveDate >= @from) AND (@to IS NULL OR sm.MoveDate < @to)
  AND (@wh IS NULL OR sm.WarehouseId=@wh) AND (@br IS NULL OR sm.BranchId=@br)
ORDER BY sm.MoveDate DESC, sm.Id DESC;";
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wh", (object?)warehouseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockMovement
            {
                Id = r.GetInt32(0), ProductId = r.GetInt32(1), ProductName = r.GetString(2),
                WarehouseId = r.GetInt32(3), WarehouseName = r.IsDBNull(4) ? null : r.GetString(4),
                Qty = r.GetDecimal(5), MovementType = (StockMovementType)r.GetInt32(6),
                BranchId = r.GetInt32(7), MoveDate = r.GetDateTime(8)
            });
        return list;
    }
}
