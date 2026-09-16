using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Masa board: bir lokasyondaki masaların anlık durumu (boş/dolu/rezerve + tutar/garson/saat).</summary>
public class TableBoardService
{
    private readonly TableService _tables = new();
    private readonly ReservationService _reservations = new();

    public List<TableBoardItem> GetBoard(int zoneId, DateTime? day = null)
    {
        var date = (day ?? DateTime.Today).Date;
        var items = _tables.GetByZone(zoneId).Select(t => new TableBoardItem
        {
            TableId = t.Id, Code = t.Code, Name = t.Name, Shape = t.Shape, Seats = t.Seats,
            X = t.X, Y = t.Y, Width = t.Width, Height = t.Height, Status = TableBoardStatus.Empty
        }).ToList();
        var byId = items.ToDictionary(i => i.TableId);

        // Açık adisyonlar
        using (var connection = Database.OpenCompany())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $@"
SELECT ot.TableId, o.Id, o.OpenedAt, u.FullName,
       (SELECT ISNULL(SUM(l.Qty*l.UnitPrice),0) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0 AND l.LineStatus<>4) AS Total
FROM dbo.Orders o
JOIN dbo.OrderTables ot ON ot.OrderId=o.Id
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Users u ON u.Id=o.CreatedBy
WHERE o.ZoneId=@z AND o.Status=0 AND o.IsDeleted=0;";
            command.Parameters.AddWithValue("@z", zoneId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int tableId = reader.GetInt32(0);
                if (!byId.TryGetValue(tableId, out var it)) continue;
                it.Status = TableBoardStatus.Occupied;
                it.OrderId = reader.GetInt32(1);
                it.OpenedAt = reader.GetDateTime(2);
                it.WaiterName = reader.IsDBNull(3) ? null : reader.GetString(3);
                it.Amount = reader.GetDecimal(4);
            }
        }

        // Rezervasyonlar (bugün, iptal/gelmedi hariç) - dolu olmayan masalar rezerve görünür
        foreach (var res in _reservations.GetByDate(zoneId, date))
        {
            if (res.Status is ReservationStatus.Iptal or ReservationStatus.Gelmedi) continue;
            foreach (int tid in res.TableIds)
            {
                if (!byId.TryGetValue(tid, out var it) || it.Status == TableBoardStatus.Occupied) continue;
                if (it.Status == TableBoardStatus.Reserved && it.ReservationStart <= res.StartTime) continue;
                it.Status = TableBoardStatus.Reserved;
                it.ReservationCustomer = res.CustomerName;
                it.ReservationStart = res.StartTime;
                it.TagColor = res.TagColor;
            }
        }

        return items;
    }
}
