using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Mutfak ekranı (KDS): reyona düşen, mutfakta/hazır üst satırlar + menü alt kalemleri.</summary>
public class KdsService
{
    /// <summary>branchId zorunlu; reyonId=0 ise tüm reyonlar. includeDone=true ise son N dk teslim edilenleri de getirir (KDS V2 TAMAMLANDI sütunu).</summary>
    public List<KdsLine> GetTickets(int branchId, int reyonId = 0, bool includeDone = false, int doneWithinMin = 30)
    {
        var list = new List<KdsLine>();
        using var connection = Database.OpenCompany();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT l.Id, l.OrderId, o.Code, p.Name, l.Qty, l.SeatNo, l.KitchenNote, l.ReyonId, ry.Name,
       l.LineStatus, l.LastPrintedAt, l.PrintCount, u.FullName,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes,
       ISNULL(ry.StationType,0), z.Name AS ZoneName, o.CoverCount, o.Note, o.AllergyNote,
       ISNULL(p.IsSpicy,0), ISNULL(o.Priority,0), ISNULL(o.IsVip,0),
       ISNULL(o.OrderType,0), ISNULL(o.Platform,0)
FROM dbo.OrderLines l
JOIN dbo.Orders o ON o.Id = l.OrderId
LEFT JOIN dbo.Products p ON p.Id = l.ProductId
LEFT JOIN dbo.Reyons ry ON ry.Id = l.ReyonId
LEFT JOIN dbo.Zones z ON z.Id = o.ZoneId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Users u ON u.Id = o.CreatedBy
WHERE o.BranchId=@b AND o.Status=0 AND l.IsDeleted=0 AND l.ParentLineId IS NULL
  AND (l.LineStatus IN (1,2,5) OR (@done=1 AND l.LineStatus=3 AND l.UpdatedAt >= DATEADD(MINUTE,-@win,SYSUTCDATETIME())))
  AND (@r=0 OR l.ReyonId=@r)
ORDER BY l.LastPrintedAt, l.OrderId, l.Id;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@r", reyonId);
            cmd.Parameters.AddWithValue("@done", includeDone ? 1 : 0);
            cmd.Parameters.AddWithValue("@win", doneWithinMin);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new KdsLine
                {
                    LineId = r.GetInt32(0), OrderId = r.GetInt32(1), OrderCode = r.GetString(2),
                    ProductName = r.IsDBNull(3) ? null : r.GetString(3),
                    Qty = r.GetDecimal(4), SeatNo = r.GetInt32(5),
                    KitchenNote = r.IsDBNull(6) ? null : r.GetString(6),
                    ReyonId = r.IsDBNull(7) ? null : r.GetInt32(7),
                    ReyonName = r.IsDBNull(8) ? null : r.GetString(8),
                    LineStatus = (OrderLineStatus)r.GetInt32(9),
                    SentAt = r.IsDBNull(10) ? null : r.GetDateTime(10),
                    PrintCount = r.GetInt32(11),
                    WaiterName = r.IsDBNull(12) ? null : r.GetString(12),
                    TableCodes = r.IsDBNull(13) ? null : r.GetString(13),
                    StationType = (StationType)r.GetInt32(14),
                    ZoneName = r.IsDBNull(15) ? null : r.GetString(15),
                    CoverCount = r.IsDBNull(16) ? 0 : r.GetInt32(16),
                    OrderNote = r.IsDBNull(17) ? null : r.GetString(17),
                    AllergyNote = r.IsDBNull(18) ? null : r.GetString(18),
                    IsSpicy = !r.IsDBNull(19) && r.GetBoolean(19),
                    Priority = !r.IsDBNull(20) && r.GetBoolean(20),
                    IsVip = !r.IsDBNull(21) && r.GetBoolean(21),
                    OrderType = (OrderType)r.GetInt32(22),
                    Platform = (DeliveryPlatform)r.GetInt32(23)
                });
        }

        if (list.Count == 0) return list;

        // Menü alt kalemleri (gösterim)
        var ids = list.Select(l => l.LineId).ToList();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT l.ParentLineId, p.Name, l.Qty, l.KitchenNote
FROM dbo.OrderLines l LEFT JOIN dbo.Products p ON p.Id=l.ProductId
WHERE l.IsDeleted=0 AND l.ParentLineId IN ({string.Join(",", ids)});";
            using var r = cmd.ExecuteReader();
            var byId = list.ToDictionary(l => l.LineId);
            while (r.Read())
            {
                int parent = r.GetInt32(0);
                if (byId.TryGetValue(parent, out var line))
                    line.Children.Add(new KdsChild
                    {
                        ProductName = r.IsDBNull(1) ? null : r.GetString(1),
                        Qty = r.GetDecimal(2),
                        KitchenNote = r.IsDBNull(3) ? null : r.GetString(3)
                    });
            }
        }
        return list;
    }
}
