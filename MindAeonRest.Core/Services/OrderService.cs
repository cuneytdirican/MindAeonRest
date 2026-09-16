using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// Adisyon yaşam döngüsü. Sipariş Orders/OrderLines'a yazılır; stok mutfağa gönderince
/// reçeteyle StockMovements'a düşer (iptalde ters); kapanışta ClFiche + ödeme hareketleri kesilir.
/// </summary>
public class OrderService
{
    private readonly ReyonRouteService _routes = new();

    private const string OrderSelect = @"
SELECT o.Id, o.Code, o.BranchId, o.ZoneId, z.Name AS ZoneName, o.CustomerId, c.Name AS CustomerName,
       o.Status, o.Note, o.OpenedAt, o.ClosedAt,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes,
       (SELECT ISNULL(SUM(CASE WHEN l.IsTreat=1 OR l.ParentLineId IS NOT NULL THEN 0 ELSE l.Qty*l.UnitPrice - l.DiscountAmount END),0)
        FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0 AND l.LineStatus<>4) AS Total,
       o.OrderType, o.CoverCount, o.DiscountAmount, o.ReservationId,
       o.Platform, o.DeliveryStatus, o.CourierId, cr.Name AS CourierName, o.DeliveryName, o.DeliveryPhone, o.DeliveryAddress,
       o.AllergyNote, o.Priority, o.IsVip
FROM dbo.Orders o
LEFT JOIN dbo.Zones z ON z.Id=o.ZoneId
LEFT JOIN dbo.Customers c ON c.Id=o.CustomerId
LEFT JOIN dbo.Couriers cr ON cr.Id=o.CourierId";

    public List<Order> GetOpenOrders(int branchId)
    {
        var list = new List<Order>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = OrderSelect + " WHERE o.BranchId=@b AND o.Status=0 AND o.IsDeleted=0 ORDER BY o.OpenedAt;";
        command.Parameters.AddWithValue("@b", branchId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapOrder(reader));
        return list;
    }

    /// <summary>Şubedeki hesabı alınmamış (açık/kısmi/ödeme bekleyen) masalar — aramaya gerek kalmadan toplu liste.</summary>
    public List<UnpaidTable> GetUnpaidTables(int branchId)
    {
        var list = new List<UnpaidTable>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT o.Id, o.Code, o.OpenedAt, o.CoverCount, z.Name AS ZoneName, u.FullName AS WaiterName,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes,
       (SELECT ISNULL(SUM(CASE WHEN l.IsTreat=1 OR l.ParentLineId IS NOT NULL OR l.LineStatus=4 THEN 0 ELSE l.Qty*l.UnitPrice - l.DiscountAmount END),0)
        FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0) AS Total,
       (SELECT ISNULL(SUM(CASE WHEN l.SettledClFicheId IS NOT NULL AND l.IsTreat=0 AND l.ParentLineId IS NULL AND l.LineStatus<>4 THEN l.Qty*l.UnitPrice - l.DiscountAmount ELSE 0 END),0)
        FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0) AS Paid,
       (CASE WHEN EXISTS(SELECT 1 FROM dbo.ServiceRequests sr WHERE sr.OrderId=o.Id AND sr.Type=2 AND sr.Status=0 AND sr.IsDeleted=0) THEN 1 ELSE 0 END) AS HasBill,
       o.OrderType, o.DeliveryName
FROM dbo.Orders o
LEFT JOIN dbo.Zones z ON z.Id=o.ZoneId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Users u ON u.Id=o.CreatedBy
WHERE o.BranchId=@b AND o.Status=0 AND o.IsDeleted=0
ORDER BY o.OpenedAt;";
        command.Parameters.AddWithValue("@b", branchId);
        using var r = command.ExecuteReader();
        while (r.Read())
            list.Add(new UnpaidTable
            {
                OrderId = r.GetInt32(0),
                OrderCode = r.GetString(1),
                OpenedAt = r.GetDateTime(2),
                CoverCount = r.GetInt32(3),
                ZoneName = r.IsDBNull(4) ? null : r.GetString(4),
                WaiterName = r.IsDBNull(5) ? null : r.GetString(5),
                TableCodes = r.IsDBNull(6) ? null : r.GetString(6),
                Total = r.GetDecimal(7),
                Paid = r.GetDecimal(8),
                HasBillRequest = r.GetInt32(9) == 1,
                OrderType = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                DeliveryName = r.IsDBNull(11) ? null : r.GetString(11)
            });
        return list;
    }

    /// <summary>Siparişin ait olduğu şube (yetki/IDOR kontrolü için). Yoksa null.</summary>
    public int? GetOrderBranch(int orderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BranchId FROM dbo.Orders WHERE Id=@id AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@id", orderId);
        var o = cmd.ExecuteScalar();
        return o is int b ? b : (o is null || o == DBNull.Value ? null : Convert.ToInt32(o));
    }

    /// <summary>Bir kalemin bağlı olduğu siparişin şubesi (kalem-bazlı IDOR kontrolü).</summary>
    public int? GetLineBranch(int lineId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT o.BranchId FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId WHERE l.Id=@id;";
        cmd.Parameters.AddWithValue("@id", lineId);
        var o = cmd.ExecuteScalar();
        return o is null || o == DBNull.Value ? null : Convert.ToInt32(o);
    }

    /// <summary>KDS "Hazır" alarmı (WaiterReady) için: kalemin bağlı olduğu siparişin sahibini (CreatedBy = garson),
    /// masa kodunu ve ürün adını TEK sorguda çöz. Hedefli push payload'ını beslemek için salt-okuma. Yoksa null.</summary>
    public LineWaiterInfo? GetLineWaiterInfo(int lineId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT o.CreatedBy, p.Name,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes
FROM dbo.OrderLines l
JOIN dbo.Orders o ON o.Id=l.OrderId
LEFT JOIN dbo.Products p ON p.Id=l.ProductId
WHERE l.Id=@id;";
        cmd.Parameters.AddWithValue("@id", lineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new LineWaiterInfo(
            r.IsDBNull(0) ? null : r.GetInt32(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2));
    }

    /// <summary>Bir masanın şubesi (masa-bazlı IDOR kontrolü).</summary>
    public int? GetTableBranch(int tableId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BranchId FROM dbo.Tables WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", tableId);
        var o = cmd.ExecuteScalar();
        return o is null || o == DBNull.Value ? null : Convert.ToInt32(o);
    }

    public Order? GetOpenByTable(int tableId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = OrderSelect + @"
WHERE o.Status=0 AND o.IsDeleted=0 AND EXISTS (SELECT 1 FROM dbo.OrderTables ot WHERE ot.OrderId=o.Id AND ot.TableId=@t)
ORDER BY o.OpenedAt;";
        command.Parameters.AddWithValue("@t", tableId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapOrder(reader) : null;
    }

    public Order? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        Order? order;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = OrderSelect + " WHERE o.Id=@id;";
            command.Parameters.AddWithValue("@id", id);
            using var reader = command.ExecuteReader();
            order = reader.Read() ? MapOrder(reader) : null;
        }
        if (order != null)
            using (var tc = connection.CreateCommand())
            {
                tc.CommandText = "SELECT TableId FROM dbo.OrderTables WHERE OrderId=@id;";
                tc.Parameters.AddWithValue("@id", id);
                using var rr = tc.ExecuteReader();
                while (rr.Read()) order.TableIds.Add(rr.GetInt32(0));
            }
        return order;
    }

    public List<OrderLine> GetLines(int orderId)
    {
        var list = new List<OrderLine>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT l.Id, l.OrderId, l.ProductId, p.Name, l.ReyonId, ry.Name, l.Qty, l.UnitPrice, l.TaxRate, l.LineStatus, l.KitchenNote,
       l.SeatNo, l.ParentLineId, l.PrintCount, l.LastPrintedAt, l.IsTreat, l.DiscountAmount, l.SettledClFicheId, l.RefundedQty, l.CampaignId
FROM dbo.OrderLines l
LEFT JOIN dbo.Products p ON p.Id=l.ProductId
LEFT JOIN dbo.Reyons ry ON ry.Id=l.ReyonId
WHERE l.OrderId=@o AND l.IsDeleted=0
ORDER BY l.SeatNo, ISNULL(l.ParentLineId, l.Id), CASE WHEN l.ParentLineId IS NULL THEN 0 ELSE 1 END, l.Id;";
        command.Parameters.AddWithValue("@o", orderId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new OrderLine
            {
                Id = reader.GetInt32(0), OrderId = reader.GetInt32(1), ProductId = reader.GetInt32(2),
                ProductName = reader.IsDBNull(3) ? null : reader.GetString(3),
                ReyonId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ReyonName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Qty = reader.GetDecimal(6), UnitPrice = reader.GetDecimal(7), TaxRate = reader.GetDecimal(8),
                LineStatus = (OrderLineStatus)reader.GetInt32(9),
                KitchenNote = reader.IsDBNull(10) ? null : reader.GetString(10),
                SeatNo = reader.GetInt32(11),
                ParentLineId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                PrintCount = reader.GetInt32(13),
                LastPrintedAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                IsTreat = reader.GetBoolean(15),
                DiscountAmount = reader.GetDecimal(16),
                SettledClFicheId = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                RefundedQty = reader.GetDecimal(18),
                CampaignId = reader.IsDBNull(19) ? null : reader.GetInt32(19)
            });
        return list;
    }

    public Order CreateOrder(int branchId, int zoneId, int customerId, IEnumerable<int> tableIds, int? userId, int? reservationId = null)
    {
        if (customerId == 0) throw new ArgumentException("Müşteri belirtilmeli (varsayılan PA).");
        var tableList = tableIds.Distinct().ToList();
        using var connection = Database.OpenCompany();
        using var tx = connection.BeginTransaction();
        try
        {
            // Aynı masaya çift adisyon engeli
            if (tableList.Count > 0)
            {
                using var guard = connection.CreateCommand();
                guard.Transaction = tx;
                guard.CommandText = $@"
SELECT COUNT(1) FROM dbo.OrderTables ot WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Orders o ON o.Id = ot.OrderId
WHERE o.Status=0 AND o.IsDeleted=0 AND ot.TableId IN ({string.Join(",", tableList)});";
                if ((int)guard.ExecuteScalar() > 0)
                    throw new InvalidOperationException("Seçilen masalardan birinde zaten açık adisyon var.");
            }

            string code = DocCounter.NextCode(connection, tx, branchId, "ADS");
            int id;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.Orders (Code, BranchId, ZoneId, CustomerId, ReservationId, Status, OpenedAt, CreatedBy, KioskNo)
OUTPUT INSERTED.Id VALUES (@c,@b,@z,@cust,@res,0,SYSUTCDATETIME(),@by,@kno);";
                cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@b", branchId);
                cmd.Parameters.AddWithValue("@z", zoneId);
                cmd.Parameters.AddWithValue("@cust", customerId);
                cmd.Parameters.AddWithValue("@res", (object?)reservationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                id = (int)cmd.ExecuteScalar()!;
            }
            foreach (int tid in tableList)
            {
                using var t = connection.CreateCommand();
                t.Transaction = tx;
                t.CommandText = "INSERT INTO dbo.OrderTables (OrderId, TableId) VALUES (@o,@t);";
                t.Parameters.AddWithValue("@o", id);
                t.Parameters.AddWithValue("@t", tid);
                t.ExecuteNonQuery();
            }
            tx.Commit();
            return GetById(id)!;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Adisyona satır ekler. Menü ise (reçete bileşeni varsa) bileşenleri alt satır (ParentLineId) olarak materyalize eder.</summary>
    // ===== Ödeme sonrası iade =====
    /// <summary>Sipariş izleme: tarih aralığındaki tüm siparişler (açık/kapalı/iptal), masa servis (paket hariç).</summary>
    public List<Order> GetOrders(int branchId, DateTime from, DateTime to)
    {
        var list = new List<Order>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        // OpenedAt UTC saklanır; yerel tarihe çevirip filtrele (gece-yarısı kayması olmasın)
        command.CommandText = OrderSelect + @" WHERE o.BranchId=@b
  AND DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), o.OpenedAt) >= @from
  AND DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), o.OpenedAt) < @to
  ORDER BY o.OpenedAt DESC;";
        command.Parameters.AddWithValue("@b", branchId);
        command.Parameters.AddWithValue("@from", from.Date);
        command.Parameters.AddWithValue("@to", to.Date.AddDays(1));
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapOrder(reader));
        return list;
    }

    /// <summary>Detay görünümü için TÜM satırlar (silinmiş/iptal dahil).</summary>
    public List<OrderLine> GetAllLines(int orderId)
    {
        var list = new List<OrderLine>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT l.Id, l.ProductId, p.Name, l.SeatNo, l.ParentLineId, l.Qty, l.UnitPrice, l.TaxRate, l.LineStatus,
       l.KitchenNote, l.IsTreat, l.DiscountAmount, l.IsDeleted, l.RefundedQty
FROM dbo.OrderLines l LEFT JOIN dbo.Products p ON p.Id=l.ProductId
WHERE l.OrderId=@o ORDER BY l.SeatNo, l.Id;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OrderLine
            {
                Id = r.GetInt32(0), OrderId = orderId, ProductId = r.GetInt32(1),
                ProductName = r.IsDBNull(2) ? null : r.GetString(2), SeatNo = r.GetInt32(3),
                ParentLineId = r.IsDBNull(4) ? null : r.GetInt32(4), Qty = r.GetDecimal(5),
                UnitPrice = r.GetDecimal(6), TaxRate = r.GetDecimal(7), LineStatus = (OrderLineStatus)r.GetInt32(8),
                KitchenNote = r.IsDBNull(9) ? null : r.GetString(9), IsTreat = r.GetBoolean(10),
                DiscountAmount = r.GetDecimal(11), IsDeleted = r.GetBoolean(12), RefundedQty = r.GetDecimal(13)
            });
        return list;
    }

    /// <summary>Siparişe kesilen cari fişler (ödeme detayı için).</summary>
    public List<ClFiche> GetOrderFiches(int orderId)
    {
        var list = new List<ClFiche>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT cf.Id, cf.Code, cf.BranchId, cf.OrderId, cf.CustomerId, cu.Name, cf.Total, cf.Paid, cf.FicheDate
FROM dbo.ClFiches cf LEFT JOIN dbo.Customers cu ON cu.Id=cf.CustomerId
WHERE cf.OrderId=@o ORDER BY cf.FicheDate, cf.Id;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ClFiche
            {
                Id = r.GetInt32(0), Code = r.GetString(1), BranchId = r.GetInt32(2), OrderId = r.GetInt32(3),
                CustomerId = r.GetInt32(4), CustomerName = r.IsDBNull(5) ? null : r.GetString(5),
                Total = r.GetDecimal(6), Paid = r.GetDecimal(7), FicheDate = r.GetDateTime(8)
            });
        return list;
    }

    /// <summary>Bir fişin ödeme satırları (araç bazında).</summary>
    public List<PaymentLine> GetPaymentLines(int clFicheId)
    {
        var list = new List<PaymentLine>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, ClFicheId, Kind, InstrumentId, CurrencyId, Amount, PayDate FROM dbo.PaymentLines WHERE ClFicheId=@f ORDER BY Id;";
        cmd.Parameters.AddWithValue("@f", clFicheId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PaymentLine
            {
                Id = r.GetInt32(0), ClFicheId = r.GetInt32(1), Kind = (PaymentKind)r.GetInt32(2),
                InstrumentId = r.IsDBNull(3) ? null : r.GetInt32(3), CurrencyId = r.GetInt32(4),
                Amount = r.GetDecimal(5), PayDate = r.GetDateTime(6)
            });
        return list;
    }

    /// <summary>Adisyon (sipariş) başlık notunu günceller.</summary>
    public void SetOrderNote(int orderId, string? note, int? userId)
    {
        EnsureNotZLocked(orderId);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET Note=@n, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adisyon alerji notunu günceller (KDS/ödeme'de kırmızı ALERJİ rozeti).</summary>
    public void SetAllergyNote(int orderId, string? note, int? userId)
    {
        EnsureNotZLocked(orderId);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET AllergyNote=@n, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adisyon ÖNCELİK / VIP bayraklarını günceller (KDS rozetleri).</summary>
    public void SetOrderFlags(int orderId, bool? vip, bool? priority, int? userId)
    {
        EnsureNotZLocked(orderId);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.Orders SET
            IsVip = COALESCE(@vip, IsVip), Priority = COALESCE(@pri, Priority),
            UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@vip", (object?)vip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pri", (object?)priority ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Masa kişi (kuver) sayısını günceller.</summary>
    public void SetCoverCount(int orderId, int count, int? userId)
    {
        if (count < 0) count = 0;
        EnsureNotZLocked(orderId);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET CoverCount=@n, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@n", count);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adisyonda hiç satır yoksa (yanlışlıkla açılmış boş masa) siparişi siler ve masayı serbest bırakır.
    /// Yalnız AÇIK ve satırsız siparişler silinir; veri kaybı olmaz.</summary>
    /// <summary>Açık adisyonda ÖDENEBİLİR açık satır kalmadıysa masayı boşaltır:
    /// hiç satır yoksa/sadece iptal varsa → İptal (masa serbest); tahsil edilmiş satır varsa → Kapandı.
    /// Açık ödenecek satır varsa dokunmaz.</summary>
    public void DeleteIfEmpty(int orderId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            int status, openCnt, anyCnt, settledCnt;
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"
SELECT o.Status,
  (SELECT COUNT(1) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0 AND l.LineStatus<>4 AND l.IsTreat=0 AND l.ParentLineId IS NULL AND l.SettledClFicheId IS NULL),
  (SELECT COUNT(1) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0),
  (SELECT COUNT(1) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.SettledClFicheId IS NOT NULL)
FROM dbo.Orders o WHERE o.Id=@id;";
                chk.Parameters.AddWithValue("@id", orderId);
                using var r = chk.ExecuteReader();
                if (!r.Read()) { tx.Rollback(); return; }
                status = r.GetInt32(0); openCnt = r.GetInt32(1); anyCnt = r.GetInt32(2); settledCnt = r.GetInt32(3);
            }
            // Yalnız AÇIK adisyon + açık ödenecek satır KALMAMIŞSA işle
            if (status != 0 || openCnt > 0) { tx.Rollback(); return; }

            string upd = settledCnt > 0
                ? "UPDATE dbo.Orders SET Status=1, ClosedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;"   // tahsil var → Kapandı (kayıtlar korunur)
                : "UPDATE dbo.Orders SET Status=2, ClosedAt=SYSUTCDATETIME(), IsDeleted=@del, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;"; // sadece iptal/boş → İptal
            foreach (var sql in new[] { "DELETE FROM dbo.OrderTables WHERE OrderId=@id;", upd })
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@id", orderId);
                if (sql.Contains("@by")) cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                if (sql.Contains("@del")) cmd.Parameters.AddWithValue("@del", anyCnt == 0 ? 1 : 0);  // hiç satır yoksa kalıcı sil; iptal satırı varsa kaydı koru
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Açık adisyonu başka bir masaya taşır.</summary>
    public void MoveOrder(int orderId, int targetTableId, int? userId)
    {
        EnsureNotZLocked(orderId);
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            // hedef masa başka açık adisyonda mı?
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                // UPDLOCK+HOLDLOCK: eşzamanlı iki taşımanın aynı BOŞ hedef masaya çift adisyon açmasını engelle
                // (CreateOrder/SplitOrder ile aynı guard deseni).
                chk.CommandText = @"SELECT COUNT(1) FROM dbo.OrderTables ot WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Orders o ON o.Id=ot.OrderId
WHERE ot.TableId=@t AND o.Status=0 AND o.Id<>@o;";
                chk.Parameters.AddWithValue("@t", targetTableId);
                chk.Parameters.AddWithValue("@o", orderId);
                if ((int)chk.ExecuteScalar()! > 0) throw new InvalidOperationException("Hedef masada açık adisyon var.");
            }
            using (var del = c.CreateCommand()) { del.Transaction = tx; del.CommandText = "DELETE FROM dbo.OrderTables WHERE OrderId=@o;"; del.Parameters.AddWithValue("@o", orderId); del.ExecuteNonQuery(); }
            using (var ins = c.CreateCommand()) { ins.Transaction = tx; ins.CommandText = "INSERT INTO dbo.OrderTables (OrderId, TableId) VALUES (@o,@t);"; ins.Parameters.AddWithValue("@o", orderId); ins.Parameters.AddWithValue("@t", targetTableId); ins.ExecuteNonQuery(); }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Seçili üst satırları (+menü alt-satırları) kaynak adisyondan hedef AÇIK adisyona taşır (Kişi/Adisyon Aktar).
    /// Yalnız ödenmemiş (SettledClFicheId NULL) satırlar; stok/ödeme bozulmaz (yalnız OrderId değişir). Taşınan satır sayısını döner.</summary>
    public int MoveLines(int sourceOrderId, IEnumerable<int> topLineIds, int targetOrderId, int? userId)
    {
        if (sourceOrderId == targetOrderId) throw new InvalidOperationException("Kaynak ve hedef adisyon aynı olamaz.");
        var ids = topLineIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0) throw new InvalidOperationException("Taşınacak kalem seçilmedi.");
        EnsureNotZLocked(sourceOrderId);
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            int moved = MoveLinesTx(c, tx, sourceOrderId, ids, targetOrderId, userId);
            if (moved == 0) throw new InvalidOperationException("Taşınacak (ödenmemiş) kalem bulunamadı.");
            tx.Commit();
            return moved;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Aynı tx içinde satır taşıma. Kaynak+hedef açık ve aynı şube olmalı. Taşınan satır sayısını döner.</summary>
    private int MoveLinesTx(SqlConnection c, SqlTransaction tx, int sourceOrderId, List<int> topIds, int targetOrderId, int? userId)
    {
        using (var chk = c.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = @"SELECT
 (SELECT COUNT(1) FROM dbo.Orders WHERE Id=@s AND Status=0 AND IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.Orders WHERE Id=@t AND Status=0 AND IsDeleted=0),
 (SELECT BranchId FROM dbo.Orders WHERE Id=@s),
 (SELECT BranchId FROM dbo.Orders WHERE Id=@t);";
            chk.Parameters.AddWithValue("@s", sourceOrderId);
            chk.Parameters.AddWithValue("@t", targetOrderId);
            using var r = chk.ExecuteReader();
            r.Read();
            if (r.GetInt32(0) == 0) throw new InvalidOperationException("Kaynak adisyon açık değil.");
            if (r.GetInt32(1) == 0) throw new InvalidOperationException("Hedef adisyon açık değil.");
            if (r.IsDBNull(2) || r.IsDBNull(3) || r.GetInt32(2) != r.GetInt32(3))
                throw new InvalidOperationException("Adisyonlar farklı şubede; taşınamaz.");
        }
        string csv = string.Join(",", topIds);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"
UPDATE dbo.OrderLines SET OrderId=@t, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHERE OrderId=@s AND IsDeleted=0 AND SettledClFicheId IS NULL
  AND (Id IN ({csv}) OR ParentLineId IN ({csv}));
SELECT @@ROWCOUNT;";
        cmd.Parameters.AddWithValue("@t", targetOrderId);
        cmd.Parameters.AddWithValue("@s", sourceOrderId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Masa Böl: seçili üst satırları BOŞ bir hedef masada YENİ adisyona taşır (kişi böl = o koltuğun satırları). Yeni adisyonu döner.</summary>
    public Order SplitOrder(int sourceOrderId, IEnumerable<int> topLineIds, int targetTableId, int? userId)
    {
        var ids = topLineIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0) throw new InvalidOperationException("Bölünecek kalem seçilmedi.");
        EnsureNotZLocked(sourceOrderId);
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            int branchId, zoneId, customerId;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT BranchId, ZoneId, CustomerId, Status FROM dbo.Orders WHERE Id=@o AND IsDeleted=0;";
                q.Parameters.AddWithValue("@o", sourceOrderId);
                using var r = q.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException("Kaynak adisyon bulunamadı.");
                branchId = r.GetInt32(0); zoneId = r.GetInt32(1); customerId = r.GetInt32(2);
                if (r.GetInt32(3) != 0) throw new InvalidOperationException("Kaynak adisyon açık değil.");
            }
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = @"SELECT COUNT(1) FROM dbo.OrderTables ot WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Orders o ON o.Id=ot.OrderId WHERE ot.TableId=@t AND o.Status=0 AND o.IsDeleted=0;";
                chk.Parameters.AddWithValue("@t", targetTableId);
                if ((int)chk.ExecuteScalar()! > 0)
                    throw new InvalidOperationException("Hedef masada açık adisyon var. Bölmek için BOŞ masa seçin (dolu masaya aktarmak için 'Birleştir/Aktar' kullanın).");
            }
            string code = DocCounter.NextCode(c, tx, branchId, "ADS");
            int newId;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.Orders (Code, BranchId, ZoneId, CustomerId, Status, OpenedAt, CreatedBy, KioskNo)
OUTPUT INSERTED.Id VALUES (@c,@b,@z,@cust,0,SYSUTCDATETIME(),@by,@kno);";
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@b", branchId);
                cmd.Parameters.AddWithValue("@z", zoneId);
                cmd.Parameters.AddWithValue("@cust", customerId);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
                newId = (int)cmd.ExecuteScalar()!;
            }
            using (var ins = c.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO dbo.OrderTables (OrderId, TableId) VALUES (@o,@t);";
                ins.Parameters.AddWithValue("@o", newId); ins.Parameters.AddWithValue("@t", targetTableId);
                ins.ExecuteNonQuery();
            }
            int moved = MoveLinesTx(c, tx, sourceOrderId, ids, newId, userId);
            if (moved == 0) throw new InvalidOperationException("Bölünecek (ödenmemiş) kalem bulunamadı.");
            tx.Commit();
            return GetById(newId)!;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Masa Birleştir: kaynak adisyonun TÜM ödenmemiş satırlarını hedef adisyona taşır, masalarını hedefe devreder, kaynağı kapatır.</summary>
    public void MergeOrders(int sourceOrderId, int targetOrderId, int? userId)
    {
        if (sourceOrderId == targetOrderId) throw new InvalidOperationException("Kaynak ve hedef masa aynı olamaz.");
        EnsureNotZLocked(sourceOrderId); EnsureNotZLocked(targetOrderId);
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var topIds = new List<int>();
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT Id FROM dbo.OrderLines WHERE OrderId=@s AND ParentLineId IS NULL AND IsDeleted=0 AND SettledClFicheId IS NULL;";
                q.Parameters.AddWithValue("@s", sourceOrderId);
                using var r = q.ExecuteReader(); while (r.Read()) topIds.Add(r.GetInt32(0));
            }
            if (topIds.Count > 0) MoveLinesTx(c, tx, sourceOrderId, topIds, targetOrderId, userId);
            else
            {
                // satır yoksa bile hedef/kaynak açık + aynı şube doğrula
                MoveLinesTx(c, tx, sourceOrderId, new List<int> { -1 }, targetOrderId, userId);
            }

            int remaining;
            using (var rc = c.CreateCommand())
            {
                rc.Transaction = tx;
                rc.CommandText = "SELECT COUNT(1) FROM dbo.OrderLines WHERE OrderId=@s AND IsDeleted=0 AND SettledClFicheId IS NULL AND LineStatus<>4;";
                rc.Parameters.AddWithValue("@s", sourceOrderId);
                remaining = (int)rc.ExecuteScalar()!;
            }
            string targetCode;
            using (var tcmd = c.CreateCommand()) { tcmd.Transaction = tx; tcmd.CommandText = "SELECT Code FROM dbo.Orders WHERE Id=@t;"; tcmd.Parameters.AddWithValue("@t", targetOrderId); targetCode = (string?)tcmd.ExecuteScalar() ?? "?"; }
            if (remaining == 0)
            {
                using var mv = c.CreateCommand();
                mv.Transaction = tx;
                mv.CommandText = "UPDATE dbo.OrderTables SET OrderId=@t WHERE OrderId=@s;";
                mv.Parameters.AddWithValue("@t", targetOrderId); mv.Parameters.AddWithValue("@s", sourceOrderId);
                mv.ExecuteNonQuery();
                using var up = c.CreateCommand();
                up.Transaction = tx;
                up.CommandText = "UPDATE dbo.Orders SET Status=1, ClosedAt=SYSUTCDATETIME(), Note=ISNULL(Note+N' ',N'')+@n, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@s;";
                up.Parameters.AddWithValue("@n", $"→ {targetCode} ile birleştirildi");
                up.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                up.Parameters.AddWithValue("@s", sourceOrderId);
                up.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public List<Order> GetClosedOrders(int branchId, DateTime date)
    {
        var list = new List<Order>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = OrderSelect + " WHERE o.BranchId=@b AND o.IsDeleted=0 AND o.Status=1 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), o.ClosedAt) AS DATE)=@d ORDER BY o.ClosedAt DESC;";
        command.Parameters.AddWithValue("@b", branchId);
        command.Parameters.AddWithValue("@d", date.Date);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapOrder(reader));
        return list;
    }

    /// <summary>
    /// Ödenmiş bir satırı (kısmi miktar destekli) iade eder: negatif ClFiche + KDV + ödeme iadesi (Nakit/Kart/Yemek Çeki);
    /// stok geri (kullanılabilir) / Fire; nakit iadesinde kasadan çıkış. RefundedQty güncellenir; tam iade olursa satır iptal.
    /// </summary>
    public ClFiche RefundLine(int lineId, decimal refundQty, bool returnToStock, PaymentKind refundKind, int? instrumentId, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var tx = connection.BeginTransaction();
        try
        {
            int branchId, orderId, customerId, status; decimal rate, lineQty, fullGross, refundedQty; bool component, settled, hasChildren;
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = @"
SELECT o.BranchId, o.Id, o.CustomerId, l.LineStatus, l.TaxRate, l.Qty,
       CASE WHEN l.IsTreat=1 OR l.ParentLineId IS NOT NULL THEN 0 ELSE l.Qty*l.UnitPrice - l.DiscountAmount END,
       l.RefundedQty,
       CASE WHEN l.ParentLineId IS NOT NULL THEN 1 ELSE 0 END,
       CASE WHEN l.SettledClFicheId IS NULL THEN 0 ELSE 1 END,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.OrderLines ch WHERE ch.ParentLineId=l.Id AND ch.IsDeleted=0) THEN 1 ELSE 0 END
FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId WHERE l.Id=@id;";
                q.Parameters.AddWithValue("@id", lineId);
                using var rr = q.ExecuteReader();
                if (!rr.Read()) throw new InvalidOperationException("Satır bulunamadı.");
                branchId = rr.GetInt32(0); orderId = rr.GetInt32(1); customerId = rr.GetInt32(2);
                status = rr.GetInt32(3); rate = rr.GetDecimal(4); lineQty = rr.GetDecimal(5); fullGross = rr.GetDecimal(6);
                refundedQty = rr.GetDecimal(7); component = rr.GetInt32(8) == 1; settled = rr.GetInt32(9) == 1; hasChildren = rr.GetInt32(10) == 1;
            }
            if (component) throw new InvalidOperationException("Menü alt satırı tek başına iade edilemez; ana satırı iade edin.");
            if (!settled) throw new InvalidOperationException("Bu satır ödenmemiş; açık adisyonda normal iptal/iade kullanın.");
            if (status == 4) throw new InvalidOperationException("Satır zaten iptal/iade edilmiş.");
            decimal remaining = lineQty - refundedQty;
            if (refundQty <= 0) refundQty = remaining;   // 0/verilmedi → kalan tümü
            if (refundQty > remaining) throw new InvalidOperationException($"En fazla {remaining:0.##} adet iade edilebilir.");
            if (hasChildren && refundQty != remaining) throw new InvalidOperationException("Menü kısmi iade edilemez; tam iade edin.");

            decimal ratio = lineQty == 0 ? 1 : refundQty / lineQty;
            decimal gross = Math.Round(fullGross * ratio, 2);
            bool full = refundedQty + refundQty >= lineQty;

            // stok geri / fire (oransal) — satır + alt satırlar
            var affected = new List<int>();
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT Id FROM dbo.OrderLines WHERE (Id=@id OR ParentLineId=@id) AND IsDeleted=0;";
                q.Parameters.AddWithValue("@id", lineId);
                using var rr = q.ExecuteReader();
                while (rr.Read()) affected.Add(rr.GetInt32(0));
            }
            foreach (int id in affected)
                ReversePartial(connection, tx, id, branchId, ratio, !returnToStock, userId);

            // RefundedQty güncelle; tam iade ise iptal (alt satırlar dahil)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE dbo.OrderLines SET RefundedQty = RefundedQty + @rq, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@rq", refundQty);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", lineId);
                cmd.ExecuteNonQuery();
            }
            if (full)
                foreach (int id in affected) SetLineStatusTx(connection, tx, id, OrderLineStatus.Iptal, userId);

            decimal matrah = Math.Round(gross / (1 + rate / 100m), 2);
            int cur = new CurrencyService().GetDefaultId() ?? 0;

            string code = DocCounter.NextCode(connection, tx, branchId, "IADE");
            int clId;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.ClFiches (Code, BranchId, OrderId, CustomerId, Total, Paid, FicheDate, CreatedBy, KioskNo)
OUTPUT INSERTED.Id VALUES (@c,@b,@o,@cust,@total,@paid,SYSUTCDATETIME(),@by,@kno);";
                cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@b", branchId);
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@cust", customerId);
                cmd.Parameters.AddWithValue("@total", -gross);
                cmd.Parameters.AddWithValue("@paid", -gross);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                clId = (int)cmd.ExecuteScalar()!;
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO dbo.ClFicheLines (ClFicheId, TaxRate, Matrah, TaxAmount, LineTotal, CreatedBy) VALUES (@cl,@r,@m,@t,@g,@by);";
                cmd.Parameters.AddWithValue("@cl", clId);
                cmd.Parameters.AddWithValue("@r", rate);
                cmd.Parameters.AddWithValue("@m", -matrah);
                cmd.Parameters.AddWithValue("@t", -(gross - matrah));
                cmd.Parameters.AddWithValue("@g", -gross);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.PaymentLines (ClFicheId, Kind, InstrumentId, CurrencyId, Amount, PayDate, CreatedBy)
VALUES (@cl,@k,@inst,@cur,@amt,SYSUTCDATETIME(),@by);";
                cmd.Parameters.AddWithValue("@cl", clId);
                cmd.Parameters.AddWithValue("@k", (int)refundKind);
                cmd.Parameters.AddWithValue("@inst", (object?)instrumentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cur", cur);
                cmd.Parameters.AddWithValue("@amt", -gross);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            // İade araç hareketi: SettleLines'taki ileri-yön posting'in AYNASI (ters yön).
            // Her enstrüman defterine geri-alma kaydı; aksi halde para/yükümlülük kaybı.
            if (refundKind == PaymentKind.Nakit && instrumentId is int kasaId)
                CashMovementService.Post(connection, tx, branchId, kasaId, cur, CashMovementType.Cikis, gross, clId, null, "İade", userId);
            // Kart-türevi (Kredi/Banka/Temassız/QR/Mobil/Online) → POS çıkışı (Odeme)
            else if (refundKind.IsCardType() && instrumentId is int posId)
                PosMovementService.Post(connection, tx, branchId, posId, cur, BankMovementType.Odeme, gross, customerId, null, "Kart iade", userId);
            // Yemek çeki → yemek çek hesabına iade hareketi (-)
            else if (refundKind == PaymentKind.YemekCeki && instrumentId is int mvId)
                MealVoucherMovementService.Post(connection, tx, branchId, mvId, cur, MealVoucherMovementType.Iade, gross, clId, "Yemek çeki iade", userId);
            // Havale/EFT → banka hesabından ödeme (-)
            else if (refundKind == PaymentKind.HavaleEft && instrumentId is int bankId)
                BankMovementService.Post(connection, tx, branchId, bankId, cur, BankMovementType.Odeme, gross, customerId, null, "Havale/EFT iade", userId);
            // Hediye çeki → çek bakiyesini GERİ EKLE (RedeemTx tersi); instrumentId = ödenen çek id'si
            else if (refundKind == PaymentKind.HediyeCeki && instrumentId is int giftId)
                GiftCardService.RefundTx(connection, tx, giftId, gross, orderId, clId, userId);
            // Puan/Sadakat → harcanan puanı müşteriye geri ekle
            else if (refundKind == PaymentKind.PuanSadakat)
                LoyaltyService.RefundTx(connection, tx, customerId, gross, orderId, clId, userId);

            // CARİ ters kaydı: satış iadesi alacağımızı azaltır (Credit). Para fiilen iade edildiyse (nakit/kart/çek)
            // dengeleyici Ödeme (Debt) → cari net değişmez; AÇIK HESAP iadesinde sadece Credit → müşteri borcu azalır.
            CariHareketService.PostTx(connection, tx, new CariHareket
            {
                BranchId = branchId, CustomerId = customerId, TxnDate = DateTime.Now,
                DocType = CariDocType.SatisIadeFaturasi, DocRefType = "ClFiche", DocRefId = clId, DocNo = code,
                Description = $"Satış iadesi {code}", CurrencyId = cur, Debt = 0, Credit = gross
            }, userId);
            if (refundKind != PaymentKind.AcikHesap)
                CariHareketService.PostTx(connection, tx, new CariHareket
                {
                    BranchId = branchId, CustomerId = customerId, TxnDate = DateTime.Now,
                    DocType = CariDocType.Odeme, DocRefType = "ClFiche", DocRefId = clId, DocNo = code,
                    Description = $"İade ödemesi ({refundKind.ToTr()}) {code}", CurrencyId = cur, Debt = gross, Credit = 0
                }, userId);

            tx.Commit();

            // AUDIT (best-effort, commit sonrası): ödeme sonrası satır iadesi — para hareketi izi.
            AuditService.Write("OrderLineRefund", lineId, "Refund",
                new { LineId = lineId, RemainingBefore = remaining },
                new { OrderId = orderId, ClFicheId = clId, Code = code, RefundQty = refundQty, RefundKind = refundKind.ToString(), Amount = gross, ReturnToStock = returnToStock },
                userId);
            return new ClFiche { Id = clId, Code = code, OrderId = orderId, BranchId = branchId, CustomerId = customerId, Total = -gross, Paid = -gross };
        }
        catch { tx.Rollback(); throw; }
    }

    // ===== Paket / Take-Away =====
    public Order CreatePackage(int branchId, int zoneId, int customerId, DeliveryPlatform platform, OrderType type,
        string? name, string? phone, string? address, int? userId)
    {
        var order = CreateOrder(branchId, zoneId, customerId, Array.Empty<int>(), userId);
        using var connection = Database.OpenCompany();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.Orders SET OrderType=@t, Platform=@p, DeliveryStatus=0,
            DeliveryName=@n, DeliveryPhone=@ph, DeliveryAddress=@a, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@p", (int)platform);
        cmd.Parameters.AddWithValue("@n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ph", (object?)phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", (object?)address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", order.Id);
        cmd.ExecuteNonQuery();
        return GetById(order.Id)!;
    }

    public List<Order> GetPackages(int branchId, bool delivered)
    {
        var list = new List<Order>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = OrderSelect + (delivered
            ? " WHERE o.BranchId=@b AND o.IsDeleted=0 AND o.OrderType IN (1,2) AND o.DeliveryStatus IN (3,4) AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), o.OpenedAt) AS DATE)=CAST(SYSDATETIME() AS DATE) ORDER BY o.OpenedAt DESC;"
            : " WHERE o.BranchId=@b AND o.IsDeleted=0 AND o.OrderType IN (1,2) AND o.DeliveryStatus IN (0,1,2) ORDER BY o.OpenedAt DESC;");
        command.Parameters.AddWithValue("@b", branchId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapOrder(reader));
        return list;
    }

    public void SetDeliveryStatus(int orderId, DeliveryStatus status, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET DeliveryStatus=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@s", (int)status);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    public void AssignCourier(int orderId, int courierId, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Orders SET CourierId=@c, DeliveryStatus=CASE WHEN DeliveryStatus<2 THEN 2 ELSE DeliveryStatus END, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@c", courierId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", orderId);
        cmd.ExecuteNonQuery();
    }

    public OrderLine AddLine(int orderId, int zoneId, int productId, decimal qty, decimal unitPrice, decimal taxRate,
        string? kitchenNote, int seatNo = 0, int? userId = null, bool materializeMenu = true)
    {
        if (qty <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        EnsureNotZLocked(orderId);
        using var connection = Database.OpenCompany();
        int parentId = InsertLine(connection, null, orderId, zoneId, productId, null, qty, unitPrice, taxRate, kitchenNote, seatNo, userId);

        if (materializeMenu)
        {
            var components = new List<(int Pid, decimal Q)>();
            using (var rc = connection.CreateCommand())
            {
                rc.CommandText = "SELECT ComponentProductId, Quantity FROM dbo.ProductRecipes WHERE MenuProductId=@m AND IsDeleted=0;";
                rc.Parameters.AddWithValue("@m", productId);
                using var rr = rc.ExecuteReader();
                while (rr.Read()) components.Add((rr.GetInt32(0), rr.GetDecimal(1)));
            }
            foreach (var (pid, cq) in components)
                InsertLine(connection, null, orderId, zoneId, pid, parentId, qty * cq, 0, 0, null, seatNo, userId);
        }
        return GetLines(orderId).First(l => l.Id == parentId);
    }

    private int InsertLine(SqlConnection connection, SqlTransaction? tx, int orderId, int zoneId, int productId,
        int? parentLineId, decimal qty, decimal unitPrice, decimal taxRate, string? kitchenNote, int seatNo, int? userId)
    {
        int? reyonId = _routes.Resolve(zoneId, productId);
        using var command = connection.CreateCommand();
        if (tx != null) command.Transaction = tx;
        command.CommandText = @"
INSERT INTO dbo.OrderLines (OrderId, ProductId, ReyonId, SeatNo, ParentLineId, Qty, UnitPrice, TaxRate, LineStatus, KitchenNote, CreatedBy)
OUTPUT INSERTED.Id VALUES (@o,@p,@r,@seat,@parent,@q,@u,@t,0,@note,@by);";
        command.Parameters.AddWithValue("@o", orderId);
        command.Parameters.AddWithValue("@p", productId);
        command.Parameters.AddWithValue("@r", (object?)reyonId ?? DBNull.Value);
        command.Parameters.AddWithValue("@seat", seatNo);
        command.Parameters.AddWithValue("@parent", (object?)parentLineId ?? DBNull.Value);
        command.Parameters.AddWithValue("@q", qty);
        command.Parameters.AddWithValue("@u", unitPrice);
        command.Parameters.AddWithValue("@t", taxRate);
        command.Parameters.AddWithValue("@note", (object?)kitchenNote ?? DBNull.Value);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }

    /// <summary>Barkoddan ürün Id'si çözer (aktif barkod). Bulunamazsa null.</summary>
    public int? ResolveProductIdByBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 ProductId FROM dbo.ProductBarcodes WHERE Barcode=@b AND IsDeleted=0;";
        command.Parameters.AddWithValue("@b", barcode.Trim());
        var r = command.ExecuteScalar();
        return r is int id ? id : null;
    }

    /// <summary>Gönderilmiş satırların fişini yeniden bastırmak için sayaç artırır (stok/durum değişmez).</summary>
    public void Reprint(int orderId, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.OrderLines SET PrintCount=PrintCount+1, LastPrintedAt=SYSUTCDATETIME() WHERE OrderId=@o AND IsDeleted=0 AND LineStatus IN (1,2,3,5);";
        command.Parameters.AddWithValue("@o", orderId);
        command.ExecuteNonQuery();
    }

    /// <summary>Tek bir gönderilmiş kalemin fişini yeniden bastırmak için sayaç artırır (stok/durum değişmez).
    /// Order-bazlı Reprint'in satır-bazlı karşılığı: yalnız verilen kalem (mutfağa düşmüş: LineStatus IN 1,2,3).</summary>
    public void ReprintLine(int lineId, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.OrderLines SET PrintCount=PrintCount+1, LastPrintedAt=SYSUTCDATETIME() WHERE Id=@l AND IsDeleted=0 AND LineStatus IN (1,2,3,5);";
        command.Parameters.AddWithValue("@l", lineId);
        command.ExecuteNonQuery();
    }

    /// <summary>Yalnız KAPANMIŞ/iptal siparişler için, kapanış (ClosedAt) gününe ait Z raporu alınmışsa true.
    /// AÇIK siparişler (Status=0) asla kilitlenmez: Z açık masaları kapsamaz, sonraki vardiyaya devreder
    /// (ürün eklenebilir, ödeme alınabilir). Z yalnız kapanmış satışları mali olarak kesinleştirir;
    /// kapanmış bir adisyonu geriye dönük düzeltmek için ayrı tarihli iade/düzeltme fişi kullanılır.</summary>
    public bool IsZLocked(int orderId)
    {
        using var c = Database.OpenCompany();
        // İş-günü kesimi (DayStartMinutes) MASTER DB'deki dbo.CompanyBranches'ta; company DB'den erişilemez.
        // Bu yüzden önce siparişin BranchId'sini company DB'den al, sonra cutoff'u master DB'den yükle,
        // sonra asıl EXISTS sorgusunu @cutoff parametresiyle çalıştır. cutoff=0'da davranış aynı (yerel takvim günü).
        int branchId;
        using (var bcmd = c.CreateCommand())
        {
            bcmd.CommandText = "SELECT BranchId FROM dbo.Orders WHERE Id=@o;";
            bcmd.Parameters.AddWithValue("@o", orderId);
            if (bcmd.ExecuteScalar() is int bid) branchId = bid;
            else return false; // sipariş yoksa kilitli değil
        }
        int cutoff = new BranchService().GetDayStartMinutes(branchId);
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.Orders o JOIN dbo.ZReports z ON z.BranchId=o.BranchId
        AND z.ReportDate >= CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, o.ClosedAt) AS DATE)
    WHERE o.Id=@o AND o.Status IN (1,2) AND o.ClosedAt IS NOT NULL) THEN 1 ELSE 0 END;";
        cmd.Parameters.AddWithValue("@o", orderId);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        return (int)cmd.ExecuteScalar()! == 1;
    }

    private static void EnsureNotZLocked(int orderId)
    {
        if (new OrderService().IsZLocked(orderId))
            throw new InvalidOperationException("Bu adisyon kapanmış ve kapanış günü için Z raporu alınmış; geriye dönük değiştirilemez. Düzeltme için ayrı tarihli iade/düzeltme fişi kullanın.");
    }

    private static void EnsureNotZLockedLine(int lineId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT OrderId FROM dbo.OrderLines WHERE Id=@l;";
        cmd.Parameters.AddWithValue("@l", lineId);
        if (cmd.ExecuteScalar() is int oid) EnsureNotZLocked(oid);
    }

    public void SetLineStatus(int lineId, OrderLineStatus status, int? userId)
    {
        using var connection = Database.OpenCompany();
        // Savunma: KDS yalnız İLERİ ilerletmeli. İptal edilmiş satırın durumu değişmez; aşama geri alınamaz (sessiz no-op).
        using (var cur = connection.CreateCommand())
        {
            cur.CommandText = "SELECT LineStatus FROM dbo.OrderLines WHERE Id=@id;";
            cur.Parameters.AddWithValue("@id", lineId);
            if (cur.ExecuteScalar() is int cs)
            {
                var c = (OrderLineStatus)cs;
                static int Rank(OrderLineStatus s) => s switch
                {
                    OrderLineStatus.Beklemede => 0, OrderLineStatus.MutfagaGonderildi => 1,
                    OrderLineStatus.Hazirlaniyor => 2, OrderLineStatus.Hazir => 3, OrderLineStatus.Teslim => 4, _ => 9
                };
                if (c == OrderLineStatus.Iptal) return;                                   // iptal satır KDS'ten değişmez
                if (status != OrderLineStatus.Iptal && Rank(status) < Rank(c)) return;     // geri alma yok
            }
        }
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.OrderLines SET LineStatus=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@s", (int)status);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", lineId);
        command.ExecuteNonQuery();
    }

    public void UpdateLineNote(int lineId, string? note, int? userId)
        => ExecLine("UPDATE dbo.OrderLines SET KitchenNote=@v, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", lineId, (object?)note ?? DBNull.Value, userId);

    public void SetTreat(int lineId, bool isTreat, int? userId)
        => ExecLine("UPDATE dbo.OrderLines SET IsTreat=@v, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", lineId, isTreat, userId);

    public void SetLineDiscount(int lineId, decimal amount, int? userId)
    {
        if (amount < 0) amount = 0;   // negatif iskonto yok
        // İskonto satır brütünü (Qty*UnitPrice) aşamaz → negatif satır engeli (CLAMP)
        ExecLine("UPDATE dbo.OrderLines SET DiscountAmount = CASE WHEN @v > (Qty*UnitPrice) THEN (Qty*UnitPrice) ELSE @v END, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", lineId, amount, userId);
    }

    /// <summary>Kampanyayı adisyona SATIR İSKONTOSU olarak uygular (etkilenen satırların iskontosu kampanyaya göre AYARLANIR).
    /// Geçerlilik (şube/tarih/gün/saat) kontrol edilir. Tutar bazlı kampanyalar etkilenen satırlara orantılı dağıtılır.</summary>
    public void ApplyCampaign(int orderId, int campaignId, int branchId, int? userId)
    {
        var camp = new CampaignService().GetById(campaignId)
            ?? throw new InvalidOperationException("Kampanya bulunamadı.");
        if (!camp.IsValidNow(branchId, DateTime.Now))
            throw new InvalidOperationException("Kampanya şu an bu şubede geçerli değil (tarih/gün/saat/şube).");
        if (!IsCampaignEligible(orderId, camp))
            throw new InvalidOperationException("Adisyon, bu kampanyanın kriterlerini karşılamıyor. (Kriter, ödenmemiş/açık kalemler üzerinden değerlendirilir; kısmi ödeme yapıldıysa kalan tutar/adet kriteri düşürmüş olabilir.)");

        // ödenebilir üst satırlar (iptal/ikram/menü-bileşeni/ödenmiş hariç)
        var lines = GetLines(orderId)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat && l.SettledClFicheId == null)
            .ToList();
        if (lines.Count == 0) throw new InvalidOperationException("Kampanya uygulanacak satır yok.");

        // BİRLEŞTİRME (stack) kuralı: aynı adisyona birden çok kampanya ANCAK hepsi "birleştirilebilir" ise uygulanır
        var appliedIds = GetAppliedCampaignIds(orderId);
        if (appliedIds.Contains(campaignId)) throw new InvalidOperationException("Bu kampanya bu adisyona zaten uygulanmış.");
        if (appliedIds.Count > 0)
        {
            if (!camp.Stackable)
                throw new InvalidOperationException("Bu kampanya başka kampanyayla birleştirilemez. Önce uygulanan kampanyaları kaldırın.");
            var others = appliedIds.Select(id => new CampaignService().GetById(id)).Where(x => x != null).ToList();
            if (others.Any(a => !a!.Stackable))
                throw new InvalidOperationException("Uygulanan kampanyalardan biri birleştirilemez. Önce onu kaldırın.");
        }

        decimal Gross(OrderLine l) => l.Qty * l.UnitPrice;
        decimal total = lines.Sum(Gross);

        // HEDİYE / İKRAM: koşullar sağlanırsa ikram ürün(ler) adisyona eklenir (indirim değil, yeni satır)
        if (camp.Type == CampaignType.Hediye)
        {
            if (camp.MinAmount > 0 && total < camp.MinAmount)
                throw new InvalidOperationException($"Hediye için min. tutar {camp.MinAmount:N2} ₺ gerekiyor (şu an {total:N2} ₺).");
            foreach (var cond in camp.Conditions)
            {
                decimal got = lines.Where(l => l.ProductId == cond.ProductId).Sum(l => l.Qty);
                if (got < cond.MinQty)
                    throw new InvalidOperationException($"Hediye koşulu sağlanmadı: '{cond.ProductName}' en az {cond.MinQty:0.##} adet olmalı (şu an {got:0.##}).");
            }
            if (camp.Gifts.Count == 0) throw new InvalidOperationException("Kampanyada ikram ürün tanımlı değil.");
            int zoneId = GetById(orderId)?.ZoneId ?? 0;
            foreach (var g in camp.Gifts)
            {
                var gl = AddLine(orderId, zoneId, g.ProductId, g.Qty <= 0 ? 1 : g.Qty, 0, 0, $"🎁 {camp.Name}", 0, userId, materializeMenu: false);
                SetTreat(gl.Id, true, userId);   // ikram → tutara eklenmez, "• İKRAM" görünür
                ExecLine("UPDATE dbo.OrderLines SET CampaignId=@v, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", gl.Id, campaignId, userId);   // ikram satırını kampanyaya bağla (satır-bazlı atıf)
            }
            MarkCampaignApplied(orderId, campaignId, userId);
            return;
        }

        var disc = lines.ToDictionary(l => l.Id, _ => 0m);

        // hedef kategori için ürün→kategori eşlemesi (KategoriYuzde)
        HashSet<int> catProducts = new();
        if (camp.Type == CampaignType.KategoriYuzde && camp.TargetCategoryId is int catId)
        {
            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ProductId FROM dbo.ProductCategoryLinks WHERE CategoryId=@c;";
            cmd.Parameters.AddWithValue("@c", catId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) catProducts.Add(r.GetInt32(0));
        }

        void Pct(IEnumerable<OrderLine> set, decimal pct) { foreach (var l in set) disc[l.Id] = Math.Round(Gross(l) * pct / 100m, 2); }
        void Spread(IEnumerable<OrderLine> set, decimal amount)
        {
            var ls = set.ToList(); decimal baseSum = ls.Sum(Gross);
            if (baseSum <= 0) return;
            amount = Math.Min(amount, baseSum);
            foreach (var l in ls) disc[l.Id] = Math.Round(amount * Gross(l) / baseSum, 2);
        }

        switch (camp.Type)
        {
            case CampaignType.TumSiparisYuzde: Pct(lines, camp.Value); break;
            case CampaignType.TumSiparisTutar: Spread(lines, camp.Value); break;
            case CampaignType.KategoriYuzde: Pct(lines.Where(l => catProducts.Contains(l.ProductId)), camp.Value); break;
            case CampaignType.UrunYuzde: Pct(lines.Where(l => l.ProductId == camp.TargetProductId), camp.Value); break;
            case CampaignType.MinTutarYuzde: if (total >= camp.MinAmount) Pct(lines, camp.Value); break;
            case CampaignType.MinTutarTutar: if (total >= camp.MinAmount) Spread(lines, camp.Value); break;
            case CampaignType.XAlYOde:
                var pl = lines.Where(l => l.ProductId == camp.TargetProductId).ToList();
                if (pl.Count > 0 && camp.BuyQty > 0 && camp.PayQty < camp.BuyQty)
                {
                    decimal qty = pl.Sum(l => l.Qty);
                    decimal freeUnits = Math.Floor(qty / camp.BuyQty) * (camp.BuyQty - camp.PayQty);
                    // Bedava birimleri EN UCUZ birimlerden hesapla (tek birim fiyat varsayımı yanlış; karışık fiyatlı satırlar)
                    var units = pl.SelectMany(l => Enumerable.Repeat(l.UnitPrice, (int)l.Qty)).OrderBy(p => p).ToList();
                    decimal freeAmount = units.Take((int)freeUnits).Sum();
                    Spread(pl, Math.Round(freeAmount, 2));
                }
                break;
        }

        using var con = Database.OpenCompany();
        using var tx = con.BeginTransaction();
        try
        {
            foreach (var kv in disc)
            {
                if (kv.Value <= 0) continue;
                using var cmd = con.CreateCommand(); cmd.Transaction = tx;
                // BİRLEŞTİRİLEBİLİR kampanyalar için EKLEMELİ iskonto (mevcut iskontonun üzerine), satır brütünü aşmaz + satırı kampanyaya bağla
                // BULGU 10: satıra ilk dokunan kampanya kimliğini yazar; farklı bir kampanya zaten atanmışsa çok-kampanyalı satır → NULL (PWA jenerik "KAMPANYA" rozeti). Tek-kampanya senaryosu doğru adı korur.
                cmd.CommandText = "UPDATE dbo.OrderLines SET DiscountAmount = CASE WHEN (Qty*UnitPrice) < (DiscountAmount+@v) THEN (Qty*UnitPrice) ELSE DiscountAmount+@v END, CampaignId = CASE WHEN CampaignId IS NULL THEN @cid WHEN CampaignId=@cid THEN @cid ELSE NULL END, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@v", kv.Value); cmd.Parameters.AddWithValue("@cid", campaignId); cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); cmd.Parameters.AddWithValue("@id", kv.Key);
                cmd.ExecuteNonQuery();
            }
            using (var oc = con.CreateCommand()) { oc.Transaction = tx; oc.CommandText = "UPDATE dbo.Orders SET AppliedCampaignId=@c, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;"; oc.Parameters.AddWithValue("@c", campaignId); oc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); oc.Parameters.AddWithValue("@o", orderId); oc.ExecuteNonQuery(); }
            using (var jc = con.CreateCommand()) { jc.Transaction = tx; jc.CommandText = "INSERT INTO dbo.OrderCampaigns (OrderId, CampaignId, CreatedBy) VALUES (@o,@c,@by);"; jc.Parameters.AddWithValue("@o", orderId); jc.Parameters.AddWithValue("@c", campaignId); jc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); jc.ExecuteNonQuery(); }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>ZORUNLU (IsMandatory) + şu an geçerli + kriteri sağlanan kampanyaları OTOMATİK uygular.
    /// WPF AdisyonWindow/WaiterTerminalV2Window.RefreshCampaigns paritesi: kullanıcı seçmeden uygulanır.
    /// Zaten uygulanmış / stack çakışması / yarış durumlarını sessizce atlar (WPF ile aynı). Uygulanan sayıyı döner.</summary>
    public int AutoApplyMandatoryCampaigns(int orderId, int branchId, int? userId)
    {
        var applied = GetAppliedCampaignIds(orderId).ToHashSet();
        int count = 0;
        // GetActive zaten IsValidNow (aktif/tarih/gün/saat/şube) süzer; ApplyCampaign eligibility + stack kurallarını tekrar denetler.
        foreach (var camp in new CampaignService().GetActive(branchId, DateTime.Now))
        {
            if (!camp.IsMandatory || applied.Contains(camp.Id)) continue;
            if (!IsCampaignEligible(orderId, camp)) continue;
            try { ApplyCampaign(orderId, camp.Id, branchId, userId); applied.Add(camp.Id); count++; }
            catch { /* stack çakışması / yarış: sessizce atla (WPF ile aynı) */ }
        }
        return count;
    }

    /// <summary>Adisyona uygulanmış kampanya id'si (yoksa null) — geriye dönük uyum.</summary>
    public int? GetAppliedCampaignId(int orderId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AppliedCampaignId FROM dbo.Orders WHERE Id=@o;";
        cmd.Parameters.AddWithValue("@o", orderId);
        var o = cmd.ExecuteScalar();
        return o is null || o == DBNull.Value ? null : Convert.ToInt32(o);
    }

    /// <summary>Adisyona uygulanmış TÜM kampanya id'leri (birleştirilebilir kampanyalar için).</summary>
    public List<int> GetAppliedCampaignIds(int orderId)
    {
        var list = new List<int>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CampaignId FROM dbo.OrderCampaigns WHERE OrderId=@o ORDER BY Id;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt32(0));
        return list;
    }

    /// <summary>Kampanyanın kriterleri şu anki adisyon satırlarıyla SAĞLANIYOR mu? (uygulamadan kontrol — canlı uyarı için)</summary>
    public bool IsCampaignEligible(int orderId, Campaign camp)
    {
        var lines = GetLines(orderId)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat && l.SettledClFicheId == null)
            .ToList();
        if (lines.Count == 0) return false;
        decimal Gross(OrderLine l) => l.Qty * l.UnitPrice;
        decimal total = lines.Sum(Gross);
        switch (camp.Type)
        {
            case CampaignType.TumSiparisYuzde:
            case CampaignType.TumSiparisTutar: return true;
            case CampaignType.KategoriYuzde:
                if (camp.TargetCategoryId is not int cat) return false;
                var prods = CategoryProductIds(cat);
                return lines.Any(l => prods.Contains(l.ProductId));
            case CampaignType.UrunYuzde:
                return lines.Any(l => l.ProductId == camp.TargetProductId);
            case CampaignType.MinTutarYuzde:
            case CampaignType.MinTutarTutar:
                return total >= camp.MinAmount;
            case CampaignType.XAlYOde:
                return lines.Where(l => l.ProductId == camp.TargetProductId).Sum(l => l.Qty) >= camp.BuyQty && camp.BuyQty > 0;
            case CampaignType.Hediye:
                if (camp.MinAmount > 0 && total < camp.MinAmount) return false;
                foreach (var cond in camp.Conditions)
                    if (lines.Where(l => l.ProductId == cond.ProductId).Sum(l => l.Qty) < cond.MinQty) return false;
                return camp.Gifts.Count > 0;
            default: return false;
        }
    }

    private static HashSet<int> CategoryProductIds(int categoryId)
    {
        var set = new HashSet<int>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ProductId FROM dbo.ProductCategoryLinks WHERE CategoryId=@c;";
        cmd.Parameters.AddWithValue("@c", categoryId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    private void MarkCampaignApplied(int orderId, int campaignId, int? userId)
    {
        using var c = Database.OpenCompany();
        using (var oc = c.CreateCommand()) { oc.CommandText = "UPDATE dbo.Orders SET AppliedCampaignId=@c, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;"; oc.Parameters.AddWithValue("@c", campaignId); oc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); oc.Parameters.AddWithValue("@o", orderId); oc.ExecuteNonQuery(); }
        using (var jc = c.CreateCommand()) { jc.CommandText = "INSERT INTO dbo.OrderCampaigns (OrderId, CampaignId, CreatedBy) VALUES (@o,@c,@by);"; jc.Parameters.AddWithValue("@o", orderId); jc.Parameters.AddWithValue("@c", campaignId); jc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); jc.ExecuteNonQuery(); }
    }

    /// <summary>Uygulanan kampanyayı kaldırır: ödenmemiş/iptal olmayan satırların iskontosunu sıfırlar.</summary>
    public void RemoveCampaign(int orderId, int? userId)
    {
        using var con = Database.OpenCompany();
        using var tx = con.BeginTransaction();
        try
        {
            // BULGU 3: yalnız KAMPANYA-kaynaklı iskontolu satırlar temizlenir (CampaignId IS NOT NULL). Elle iskontolu satırlar (SetLineDiscount → CampaignId=NULL) KORUNUR.
            using (var cmd = con.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "UPDATE dbo.OrderLines SET DiscountAmount=0, CampaignId=NULL, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE OrderId=@o AND IsDeleted=0 AND LineStatus<>4 AND SettledClFicheId IS NULL AND IsTreat=0 AND CampaignId IS NOT NULL;"; cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); cmd.Parameters.AddWithValue("@o", orderId); cmd.ExecuteNonQuery(); }
            // Hediye/ikram olarak EKLENEN satırları kaldır (🎁 işaretli, henüz ödenmemiş)
            using (var gc = con.CreateCommand()) { gc.Transaction = tx; gc.CommandText = "UPDATE dbo.OrderLines SET IsDeleted=1, LineStatus=4, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE OrderId=@o AND IsDeleted=0 AND SettledClFicheId IS NULL AND KitchenNote LIKE N'🎁%';"; gc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); gc.Parameters.AddWithValue("@o", orderId); gc.ExecuteNonQuery(); }
            using (var oc = con.CreateCommand()) { oc.Transaction = tx; oc.CommandText = "UPDATE dbo.Orders SET AppliedCampaignId=NULL, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;"; oc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value); oc.Parameters.AddWithValue("@o", orderId); oc.ExecuteNonQuery(); }
            using (var jc = con.CreateCommand()) { jc.Transaction = tx; jc.CommandText = "DELETE FROM dbo.OrderCampaigns WHERE OrderId=@o;"; jc.Parameters.AddWithValue("@o", orderId); jc.ExecuteNonQuery(); }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Sadece henüz gönderilmemiş (Beklemede) satırın miktarını değiştirir.</summary>
    public void ChangeQty(int lineId, decimal qty, int? userId)
    {
        if (qty <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        EnsureNotZLockedLine(lineId);
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.OrderLines SET Qty=@v, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND LineStatus=0;";
        command.Parameters.AddWithValue("@v", qty);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", lineId);
        // Yalnız Beklemede satırın miktarı değişebilir; gönderilmiş/iptal satırda sessiz no-op yerine anlamlı hata.
        if (command.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Kalem mutfağa gönderilmiş veya iptal edilmiş; miktar değiştirilemez. Düzeltme için iptal/iade kullanın.");
    }

    private static void ExecLine(string sql, int lineId, object value, int? userId)
    {
        EnsureNotZLockedLine(lineId);
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@v", value);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", lineId);
        command.ExecuteNonQuery();
    }

    /// <summary>Bekleyen satırları mutfağa gönderir: stok fişi + reçete bazlı stok çıkışı oluşturur.</summary>
    public List<KitchenTicket> SendToKitchen(int orderId, int? userId)
    {
        EnsureNotZLocked(orderId);
        List<int> sentIds;
        using (var connection = Database.OpenCompany())
        using (var tx = connection.BeginTransaction())
        {
            try
            {
                int branchId = GetOrderBranch(connection, tx, orderId);
                var pending = GetPendingLines(connection, tx, orderId);
                if (pending.Count == 0) { tx.Commit(); return new List<KitchenTicket>(); }

                var parents = GetMenuParentIds(connection, tx, orderId);
                int? stId = null;
                sentIds = new List<int>();
                foreach (var (lineId, productId, qty) in pending)
                {
                    // ÖNCE atomik claim: satır hâlâ Beklemede ise gönderildi işaretle.
                    // 0 dönerse (eşzamanlı ikinci gönderim kaptı) stok DÜŞME → mükerrer stok düşümü engeli.
                    if (MarkSentTx(connection, tx, lineId, userId) == 0) continue;
                    if (!parents.Contains(lineId))   // menü ebeveyni: stok alt satırlardan düşer
                    {
                        stId ??= CreateStFiche(connection, tx, branchId, orderId, userId);
                        PostLineStock(connection, tx, stId.Value, lineId, productId, qty, branchId, userId);
                    }
                    sentIds.Add(lineId);
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }
        return BuildKitchenTickets(orderId, sentIds);
    }

    /// <summary>Gönderilen satırları reyona göre gruplayıp mutfak fişlerini hazırlar (yazdırma çağıran tarafta).</summary>
    private List<KitchenTicket> BuildKitchenTickets(int orderId, List<int> sentTopIds)
    {
        var tickets = new Dictionary<int, KitchenTicket>();
        if (sentTopIds.Count == 0) return new List<KitchenTicket>();
        string ids = string.Join(",", sentTopIds);
        using var c = Database.OpenCompany();

        string orderCode = "", tableCodes = "";
        using (var oc = c.CreateCommand())
        {
            oc.CommandText = $"SELECT o.Code, (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) FROM dbo.Orders o WHERE o.Id=@o;";
            oc.Parameters.AddWithValue("@o", orderId);
            using var r = oc.ExecuteReader();
            if (r.Read()) { orderCode = r.GetString(0); tableCodes = r.IsDBNull(1) ? "" : r.GetString(1); }
        }

        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT l.Id, p.Name, l.Qty, l.KitchenNote, l.SeatNo, l.ParentLineId, l.ReyonId, ry.Name AS ReyonName, ry.PrinterName
FROM dbo.OrderLines l
JOIN dbo.Products p ON p.Id = l.ProductId
LEFT JOIN dbo.Reyons ry ON ry.Id = l.ReyonId
WHERE l.IsDeleted=0 AND (l.Id IN ({ids}) OR l.ParentLineId IN ({ids}))
ORDER BY l.ReyonId, l.ParentLineId, l.Id;";
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
        {
            int? reyonId = rr.IsDBNull(6) ? null : rr.GetInt32(6);
            int key = reyonId ?? 0;
            if (!tickets.TryGetValue(key, out var tk))
            {
                tk = new KitchenTicket
                {
                    ReyonId = key,
                    ReyonName = rr.IsDBNull(7) ? "(reyon atanmamış)" : rr.GetString(7),
                    PrinterName = rr.IsDBNull(8) ? null : rr.GetString(8),
                    OrderCode = orderCode, TableCodes = tableCodes
                };
                tickets[key] = tk;
            }
            tk.Lines.Add(new KitchenTicketLine
            {
                Name = rr.GetString(1),
                Qty = rr.GetDecimal(2),
                Note = rr.IsDBNull(3) ? null : rr.GetString(3),
                SeatNo = rr.IsDBNull(4) ? 0 : rr.GetInt32(4),
                IsComponent = !rr.IsDBNull(5)
            });
        }
        return tickets.Values.ToList();
    }

    /// <summary>
    /// Satırı iptal eder (menü ise alt satırları dahil). Gönderilmişse stoğu:
    /// returnToStock=true → stoğa geri alır (kullanılabilir iade); false → Fire (çöp, stoğa dönmez).
    /// İptal edilen satır hesaba girmez.
    /// </summary>
    public void CancelLine(int lineId, int? userId, bool returnToStock = true, string? reason = null)
    {
        EnsureNotZLockedLine(lineId);
        using var connection = Database.OpenCompany();
        using var tx = connection.BeginTransaction();
        try
        {
            int branchId = 0;
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT o.BranchId FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId WHERE l.Id=@id;";
                q.Parameters.AddWithValue("@id", lineId);
                var r = q.ExecuteScalar();
                if (r is null) { tx.Commit(); return; }
                branchId = (int)r;
            }

            // satır + alt satırları
            var affected = new List<(int Id, int Status)>();
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT Id, LineStatus FROM dbo.OrderLines WHERE (Id=@id OR ParentLineId=@id) AND IsDeleted=0 AND LineStatus<>4;";
                q.Parameters.AddWithValue("@id", lineId);
                using var rr = q.ExecuteReader();
                while (rr.Read()) affected.Add((rr.GetInt32(0), rr.GetInt32(1)));
            }

            foreach (var (id, status) in affected)
            {
                if (status is 1 or 2 or 3 or 5)   // mutfağa gitmiş/hazırlanıyor/hazır/teslim → stok düşmüştü; iptalde geri al
                {
                    ReverseLineStock(connection, tx, id, branchId, userId);   // satışı geri al (+)
                    if (!returnToStock) PostFireForLine(connection, tx, id, branchId, userId); // Fire (-) → stoğa dönmez
                }
                SetLineStatusTx(connection, tx, id, OrderLineStatus.Iptal, userId);
                using (var up = connection.CreateCommand())
                {
                    up.Transaction = tx;
                    up.CommandText = "UPDATE dbo.OrderLines SET CancelReason=@r, CancelledBy=@by, CancelledAt=SYSUTCDATETIME(), ReturnedToStock=@rs WHERE Id=@id;";
                    up.Parameters.AddWithValue("@r", (object?)reason ?? DBNull.Value);
                    up.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                    up.Parameters.AddWithValue("@rs", returnToStock);
                    up.Parameters.AddWithValue("@id", id);
                    up.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Bir satırın tüketim miktarını Fire (çöp) olarak kaydeder (stoğa dönmez).</summary>
    private static void PostFireForLine(SqlConnection c, SqlTransaction tx, int lineId, int branchId, int? userId)
    {
        var moves = new List<(int Pid, int Wh, decimal Qty)>();
        using (var rc = c.CreateCommand())
        {
            rc.Transaction = tx;
            rc.CommandText = "SELECT ProductId, WarehouseId, Qty FROM dbo.StockMovements WHERE OrderLineId=@l AND MovementType=0;";
            rc.Parameters.AddWithValue("@l", lineId);
            using var rr = rc.ExecuteReader();
            while (rr.Read()) moves.Add((rr.GetInt32(0), rr.GetInt32(1), rr.GetDecimal(2)));
        }
        foreach (var (pid, wh, qty) in moves)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO dbo.StockMovements (OrderLineId, ProductId, WarehouseId, Qty, MovementType, BranchId, MoveDate, CreatedBy)
VALUES (@line,@p,@w,@qty,7,@b,SYSUTCDATETIME(),@by);";   // 7 = Fire
            cmd.Parameters.AddWithValue("@line", lineId);
            cmd.Parameters.AddWithValue("@p", pid);
            cmd.Parameters.AddWithValue("@w", wh);
            cmd.Parameters.AddWithValue("@qty", qty);   // qty negatif (tüketim) → fire de çıkış
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Bir satırın stoğunu ORANSAL geri alır (kısmi iade). fire=true ise stoğa dönmez (Fire). Guard yok (tekrar edebilir).</summary>
    private static void ReversePartial(SqlConnection c, SqlTransaction tx, int lineId, int branchId, decimal ratio, bool fire, int? userId)
    {
        if (ratio <= 0) return;
        var moves = new List<(int Pid, int Wh, decimal Qty)>();
        using (var rc = c.CreateCommand())
        {
            rc.Transaction = tx;
            rc.CommandText = "SELECT ProductId, WarehouseId, Qty FROM dbo.StockMovements WHERE OrderLineId=@l AND MovementType=0;";
            rc.Parameters.AddWithValue("@l", lineId);
            using var rr = rc.ExecuteReader();
            while (rr.Read()) moves.Add((rr.GetInt32(0), rr.GetInt32(1), rr.GetDecimal(2)));
        }
        foreach (var (pid, wh, qty) in moves)
        {
            decimal part = Math.Round(qty * ratio, 3);   // qty negatif (tüketim)
            InsertStock(c, tx, lineId, pid, wh, -part, 1, branchId, userId);   // SatisIptal (+) stoğa geri
            if (fire) InsertStock(c, tx, lineId, pid, wh, part, 7, branchId, userId);   // Fire (-) → net 0, çöp kaydı
        }
    }

    private static void InsertStock(SqlConnection c, SqlTransaction tx, int lineId, int pid, int wh, decimal qty, int type, int branchId, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.StockMovements (OrderLineId, ProductId, WarehouseId, Qty, MovementType, BranchId, MoveDate, CreatedBy)
VALUES (@line,@p,@w,@qty,@t,@b,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@line", lineId);
        cmd.Parameters.AddWithValue("@p", pid);
        cmd.Parameters.AddWithValue("@w", wh);
        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@t", type);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Tüm açık satırları öder (kısmi olmayan kapanış).</summary>
    public ClFiche CloseOrder(int orderId, IEnumerable<PaymentLine> payments, int? userId)
        => SettleLines(orderId, null, payments, null, userId);

    /// <summary>
    /// Seçili üst satırları (kişi/böl bazlı) öder: stoğu düşer, KDV kırılımlı ClFiche + ClFicheLines + ödeme
    /// hareketlerini oluşturur, satırları kapatır. Tüm satırlar kapanınca adisyon Kapandı olur.
    /// topLineIds null ise tüm açık satırlar. Açık Hesap için müşteri PA olamaz.
    /// </summary>
    public ClFiche SettleLines(int orderId, IEnumerable<int>? topLineIds, IEnumerable<PaymentLine> payments, int? overrideCustomerId, int? userId)
    {
        EnsureNotZLocked(orderId);
        var payList = payments.ToList();
        using var connection = Database.OpenCompany();
        using var tx = connection.BeginTransaction();
        try
        {
            int branchId = GetOrderBranch(connection, tx, orderId);
            int orderCustomer = GetOrderCustomer(connection, tx, orderId);
            int customerId = overrideCustomerId ?? orderCustomer;

            if (payList.Any(p => p.Kind == PaymentKind.AcikHesap) && IsPaCustomer(connection, tx, customerId))
                throw new InvalidOperationException("Açık hesap PA (Müşterilerimiz) üzerine bırakılamaz; gerçek bir müşteri seçin.");
            if (payList.Any(p => p.Kind == PaymentKind.PuanSadakat) && IsPaCustomer(connection, tx, customerId))
                throw new InvalidOperationException("Puan/Sadakat ile ödeme için gerçek bir müşteri seçin (PA olamaz).");
            // PARASAL AÇIK KORUMASI (fail-closed): araç gerektiren yöntemde InstrumentId boşsa tahsilat alınmaz.
            // Aksi halde PaymentLine+cari "tahsil edildi" yazılır ama kasa/POS/banka/yemek/hediye defterine HİÇBİR ŞEY yazılmaz.
            foreach (var p in payList)
                if (p.Kind.RequiresInstrument() && p.InstrumentId is null)
                    throw new InvalidOperationException($"'{p.Kind.ToTr()}' için araç/hesap seçilmeli (kasa/POS/banka/yemek çeki/hediye çeki). Tahsilat alınmadı.");

            if (overrideCustomerId is int oc && oc != orderCustomer)
            {
                using var uc = connection.CreateCommand();
                uc.Transaction = tx;
                uc.CommandText = "UPDATE dbo.Orders SET CustomerId=@c, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;";
                uc.Parameters.AddWithValue("@c", oc);
                uc.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                uc.Parameters.AddWithValue("@o", orderId);
                uc.ExecuteNonQuery();
            }

            var all = GetUnsettledLines(connection, tx, orderId);
            var topSet = topLineIds?.ToHashSet();
            var tops = all.Where(l => l.Parent == null && (topSet == null || topSet.Contains(l.Id))).ToList();
            if (tops.Count == 0) throw new InvalidOperationException("Ödenecek satır bulunamadı.");
            var topIds = tops.Select(t => t.Id).ToHashSet();
            var selected = all.Where(l => topIds.Contains(l.Id) || (l.Parent.HasValue && topIds.Contains(l.Parent.Value))).ToList();

            // Stok: bekleyenleri düş + satırları Teslim yap
            var parents = GetMenuParentIds(connection, tx, orderId);
            int? stId = null;
            foreach (var l in selected)
            {
                if (l.Status == 0)
                {
                    stId ??= CreateStFiche(connection, tx, branchId, orderId, userId);
                    if (!parents.Contains(l.Id)) PostLineStock(connection, tx, stId.Value, l.Id, l.Pid, l.Qty, branchId, userId);
                }
                SetLineStatusTx(connection, tx, l.Id, OrderLineStatus.Teslim, userId);
            }

            // KDV kırılımı (fiyatlar KDV dahil varsayımı)
            var groups = tops.GroupBy(t => t.Rate).Select(g =>
            {
                decimal gross = g.Sum(x => x.Gross);
                decimal matrah = Math.Round(gross / (1 + g.Key / 100m), 2);
                return (Rate: g.Key, Gross: gross, Matrah: matrah, Tax: gross - matrah);
            }).ToList();
            decimal total = groups.Sum(g => g.Gross);
            decimal nonAcik = payList.Where(p => p.Kind != PaymentKind.AcikHesap).Sum(p => p.Amount);
            decimal acik = payList.Where(p => p.Kind == PaymentKind.AcikHesap).Sum(p => p.Amount);
            // Eksik ödeme: kalan tutar ya tahsil edilmeli ya da Açık Hesap ile karşılanmalı
            if (nonAcik + acik + 0.01m < total)
                throw new InvalidOperationException($"Eksik ödeme: kalan {total - nonAcik - acik:N2} ya tahsil edilmeli ya da 'Açık Hesap' ile müşteriye işlenmeli.");
            decimal collected = total - acik;                       // cari borcu düşecek gerçek tahsilat (açık hesap hariç)
            decimal change = Math.Round(nonAcik - collected, 2);    // para üstü (fazla nakit/tahsilat); >=0
            if (change < 0) change = 0;

            // TUTAR BÜTÜNLÜĞÜ (API çağrılarında istemciye güvenilmez):
            // ① Sıfır/negatif ödeme alınamaz (iadeler ayrı yoldan modellenir).
            if (payList.Any(p => p.Amount <= 0))
                throw new InvalidOperationException("Geçersiz ödeme tutarı: sıfır/negatif tutar alınamaz.");
            if (payList.Select(p => p.CurrencyId).Distinct().Count() > 1)
                throw new InvalidOperationException("Tek adisyon ödemesinde farklı dövizler karıştırılamaz.");
            // ② Fazla ödeme yalnız NAKİT para üstü kadar olabilir — kart/çek vb. ile fazla tahsilat para yaratır, engellenir.
            decimal cashSum = payList.Where(p => p.Kind == PaymentKind.Nakit).Sum(p => p.Amount);
            if (change > cashSum + 0.01m)
                throw new InvalidOperationException($"Fazla ödeme alınamaz: yalnız nakit para üstü verilebilir. Fazla {change:N2}, nakit {cashSum:N2}. (Kart/çek tutarını kalan kadar girin.)");

            string clCode = DocCounter.NextCode(connection, tx, branchId, "CL");
            // VARDİYA damgası: fiş kesilmeden önce bu terminalin açık vardiyasını al (yoksa OpeningFloat=0 ile aç).
            int shiftId = ShiftService.GetOrOpenCurrentTx(connection, tx, branchId, Helpers.Session.CurrentKioskNo, userId);
            int clId;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.ClFiches (Code, BranchId, OrderId, CustomerId, Total, Paid, FicheDate, CreatedBy, KioskNo, ShiftId)
OUTPUT INSERTED.Id VALUES (@c,@b,@o,@cust,@total,@paid,SYSUTCDATETIME(),@by,@kno,@shift);";
                cmd.Parameters.AddWithValue("@shift", shiftId);
                cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
                cmd.Parameters.AddWithValue("@c", clCode);
                cmd.Parameters.AddWithValue("@b", branchId);
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@cust", customerId);
                cmd.Parameters.AddWithValue("@total", total);
                cmd.Parameters.AddWithValue("@paid", collected);   // açık hesap hariç fiilen tahsil edilen (para üstü hariç)
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                clId = (int)cmd.ExecuteScalar()!;
            }
            foreach (var g in groups)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO dbo.ClFicheLines (ClFicheId, TaxRate, Matrah, TaxAmount, LineTotal, CreatedBy) VALUES (@cl,@r,@m,@t,@g,@by);";
                cmd.Parameters.AddWithValue("@cl", clId);
                cmd.Parameters.AddWithValue("@r", g.Rate);
                cmd.Parameters.AddWithValue("@m", g.Matrah);
                cmd.Parameters.AddWithValue("@t", g.Tax);
                cmd.Parameters.AddWithValue("@g", g.Gross);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            foreach (var p in payList)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.PaymentLines (ClFicheId, Kind, InstrumentId, CurrencyId, Amount, PayDate, CreatedBy)
VALUES (@cl,@k,@inst,@cur,@amt,SYSUTCDATETIME(),@by);";
                cmd.Parameters.AddWithValue("@cl", clId);
                cmd.Parameters.AddWithValue("@k", (int)p.Kind);
                cmd.Parameters.AddWithValue("@inst", (object?)p.InstrumentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cur", p.CurrencyId);
                cmd.Parameters.AddWithValue("@amt", p.Amount);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // Nakit ödeme -> kasaya tahsilat hareketi
                if (p.Kind == PaymentKind.Nakit && p.InstrumentId is int kasaId)
                    CashMovementService.Post(connection, tx, branchId, kasaId, p.CurrencyId, CashMovementType.Tahsilat, p.Amount, clId, null, "Adisyon tahsilatı", userId);
                // Kart-türevi ödeme (Kredi/Banka/Temassız/QR/Mobil/Online) -> POS hareketi
                else if (p.Kind.IsCardType() && p.InstrumentId is int posId)
                    PosMovementService.Post(connection, tx, branchId, posId, p.CurrencyId, BankMovementType.Tahsilat, p.Amount, customerId, null, $"Adisyon kart tahsilatı {clCode}", userId);
                // Yemek çeki ödeme -> yemek çek hesabına tahsilat hareketi (defter)
                else if (p.Kind == PaymentKind.YemekCeki && p.InstrumentId is int mvId)
                    MealVoucherMovementService.Post(connection, tx, branchId, mvId, p.CurrencyId, MealVoucherMovementType.Tahsilat, p.Amount, clId, $"Adisyon yemek çeki tahsilatı {clCode}", userId);
                // Havale/EFT ödeme -> banka hesabına tahsilat hareketi (defter)
                else if (p.Kind == PaymentKind.HavaleEft && p.InstrumentId is int bankId)
                    BankMovementService.Post(connection, tx, branchId, bankId, p.CurrencyId, BankMovementType.Tahsilat, p.Amount, customerId, null, $"Adisyon havale/EFT tahsilatı {clCode}", userId);
                // Hediye çeki ödeme -> çek bakiyesinden düş (defter); InstrumentId = GiftCard.Id
                else if (p.Kind == PaymentKind.HediyeCeki && p.InstrumentId is int giftId)
                    GiftCardService.RedeemTx(connection, tx, giftId, p.Amount, p.CurrencyId, orderId, clId, userId);
                // Puan/Sadakat ödeme -> müşteri puanından düş (defter)
                else if (p.Kind == PaymentKind.PuanSadakat)
                    LoyaltyService.RedeemTx(connection, tx, customerId, p.Amount, orderId, clId, userId);
            }

            // Para üstü: fazla nakit verildiyse kasadan çıkış (net nakit = alınan - para üstü)
            if (change > 0.01m)
            {
                var cashPay = payList.FirstOrDefault(p => p.Kind == PaymentKind.Nakit && p.InstrumentId is int);
                if (cashPay?.InstrumentId is int chId)
                    CashMovementService.Post(connection, tx, branchId, chId, cashPay.CurrencyId, CashMovementType.Cikis, change, clId, null, $"Para üstü {clCode}", userId);
            }

            // K2: POS satışı CARİ deftere işlenir (tüm ödeme tipleri).
            // Satış toplamı borç; nakit/kart/yemek-çeki tahsilatları alacak; AÇIK HESAP borç olarak kalır.
            int saleCur = payList.FirstOrDefault()?.CurrencyId ?? new CurrencyService().GetDefaultId() ?? 0;
            CariHareketService.PostTx(connection, tx, new CariHareket
            {
                BranchId = branchId, CustomerId = customerId, TxnDate = DateTime.Now,
                DocType = CariDocType.SatisFaturasi, DocRefType = "ClFiche", DocRefId = clId, DocNo = clCode,
                Description = $"Adisyon satış {clCode}", CurrencyId = saleCur, Debt = total, Credit = 0
            }, userId);
            // Tahsilat alacak kaydı yalnız fiilen tahsil edilen kadar (para üstü ve açık hesap hariç) → cari net borç = açık hesap
            decimal creditLeft = collected;
            foreach (var p in payList)
            {
                if (p.Kind == PaymentKind.AcikHesap) continue;   // açık hesap → borç olarak kalır (tahsilat yok)
                decimal credit = Math.Min(p.Amount, creditLeft);
                if (credit <= 0) continue;
                creditLeft -= credit;
                CariHareketService.PostTx(connection, tx, new CariHareket
                {
                    BranchId = branchId, CustomerId = customerId, TxnDate = DateTime.Now,
                    DocType = CariDocType.Tahsilat, DocRefType = "ClFiche", DocRefId = clId, DocNo = clCode,
                    Description = $"Adisyon tahsilat ({p.Kind.ToTr()}) {clCode}", CurrencyId = p.CurrencyId, Debt = 0, Credit = credit
                }, userId);
            }

            // Satırları kapat — ATOMİK CLAIM: yalnız HÂLÂ ödenmemiş satırlar işaretlenir.
            // Üst satırlar + onların menü alt-satırları birlikte tahsil işaretlenir (alt satır ayrı ödenmez).
            // Eşzamanlı ikinci ödeme aynı üst satırları daha önce kaptıysa (SettledClFicheId artık dolu),
            // beklenen üst satır sayısı kapanmaz → çift ödeme oluşmasın diye tüm işlem geri alınır.
            var topIdList = tops.Select(s => s.Id).ToList();
            int claimedTops;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                var ids = string.Join(",", selected.Select(s => s.Id));
                var topCsv = string.Join(",", topIdList);
                cmd.CommandText = $@"
UPDATE dbo.OrderLines SET SettledClFicheId=@cl, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHERE (Id IN ({ids}) OR ParentLineId IN ({ids})) AND SettledClFicheId IS NULL;
SELECT COUNT(1) FROM dbo.OrderLines WHERE Id IN ({topCsv}) AND SettledClFicheId=@cl;";
                cmd.Parameters.AddWithValue("@cl", clId);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                claimedTops = (int)cmd.ExecuteScalar()!;
            }
            if (claimedTops != topIdList.Count)
                throw new InvalidOperationException("Seçilen kalemlerden biri bu sırada başka bir ödemeyle tahsil edildi; ödeme alınmadı. Adisyonu yenileyip tekrar deneyin.");

            // Kalan açık satır yoksa adisyonu kapat — yalnız ÖDENEBİLİR üst satırlar (menü alt-satırı + ikram HARİÇ)
            int remaining;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.OrderLines WHERE OrderId=@o AND IsDeleted=0 AND LineStatus<>4 AND ParentLineId IS NULL AND IsTreat=0 AND SettledClFicheId IS NULL;";
                cmd.Parameters.AddWithValue("@o", orderId);
                remaining = (int)cmd.ExecuteScalar()!;
            }
            if (remaining == 0)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE dbo.Orders SET Status=1, ClosedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;";
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Puan/sadakat kazanımı: fiilen tahsil edilen tutar üzerinden (puanla ödenen kısım hariç; program aktifse + gerçek müşteri)
            decimal puanSpent = payList.Where(p => p.Kind == PaymentKind.PuanSadakat).Sum(p => p.Amount);
            decimal earnBase = collected - puanSpent;
            if (earnBase > 0 && !IsPaCustomer(connection, tx, customerId))
                LoyaltyService.EarnTx(connection, tx, customerId, earnBase, orderId, clId, userId);

            tx.Commit();
            return new ClFiche { Id = clId, Code = clCode, OrderId = orderId, BranchId = branchId, CustomerId = customerId, Total = total, Paid = collected };
        }
        catch { tx.Rollback(); throw; }
    }

    private static bool IsPaCustomer(SqlConnection c, SqlTransaction tx, int customerId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.Customers WHERE Id=@id AND Code='PA';";
        cmd.Parameters.AddWithValue("@id", customerId);
        return (int)cmd.ExecuteScalar() > 0;
    }

    private static List<(int Id, int? Parent, decimal Rate, decimal Gross, int Status, int Pid, decimal Qty)> GetUnsettledLines(SqlConnection c, SqlTransaction tx, int orderId)
    {
        var list = new List<(int, int?, decimal, decimal, int, int, decimal)>();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        // UPDLOCK+HOLDLOCK: ödenecek satırları işlem boyunca kilitle → eşzamanlı ikinci ödeme
        // aynı satırları seçemez (mükerrer ClFiche/çift tahsilat engellenir). Tahsil işaretleme de
        // ayrıca koşullu (SettledClFicheId IS NULL) yapılır → ikili güvence.
        cmd.CommandText = @"
SELECT Id, ParentLineId, TaxRate,
       CASE WHEN IsTreat=1 OR ParentLineId IS NOT NULL THEN 0 ELSE Qty*UnitPrice - DiscountAmount END AS Gross,
       LineStatus, ProductId, Qty
FROM dbo.OrderLines WITH (UPDLOCK, HOLDLOCK)
WHERE OrderId=@o AND IsDeleted=0 AND LineStatus<>4 AND SettledClFicheId IS NULL;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
            list.Add((rr.GetInt32(0), rr.IsDBNull(1) ? null : rr.GetInt32(1), rr.GetDecimal(2), rr.GetDecimal(3), rr.GetInt32(4), rr.GetInt32(5), rr.GetDecimal(6)));
        return list;
    }

    public void CancelOrder(int orderId, int? userId)
    {
        EnsureNotZLocked(orderId);
        using var connection = Database.OpenCompany();
        using var tx = connection.BeginTransaction();
        try
        {
            int branchId = GetOrderBranch(connection, tx, orderId);
            var lines = new List<(int Id, int Status)>();
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT Id, LineStatus FROM dbo.OrderLines WHERE OrderId=@o AND IsDeleted=0 AND LineStatus<>4;";
                q.Parameters.AddWithValue("@o", orderId);
                using var rr = q.ExecuteReader();
                while (rr.Read()) lines.Add((rr.GetInt32(0), rr.GetInt32(1)));
            }
            foreach (var (lineId, status) in lines)
            {
                if (status is 1 or 2 or 3 or 5) ReverseLineStock(connection, tx, lineId, branchId, userId);   // Hazirlaniyor(5) dahil — stok geri al
                SetLineStatusTx(connection, tx, lineId, OrderLineStatus.Iptal, userId);
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE dbo.Orders SET Status=2, ClosedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@o;";
                cmd.Parameters.AddWithValue("@o", orderId);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ===== iç yardımcılar =====
    private static int GetOrderBranch(SqlConnection c, SqlTransaction tx, int orderId) => ScalarInt(c, tx, "SELECT BranchId FROM dbo.Orders WHERE Id=@o;", orderId);
    private static int GetOrderCustomer(SqlConnection c, SqlTransaction tx, int orderId) => ScalarInt(c, tx, "SELECT CustomerId FROM dbo.Orders WHERE Id=@o;", orderId);

    private static int ScalarInt(SqlConnection c, SqlTransaction tx, string sql, int p)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql; cmd.Parameters.AddWithValue("@o", p);
        return (int)cmd.ExecuteScalar()!;
    }

    private static List<(int LineId, int ProductId, decimal Qty)> GetPendingLines(SqlConnection c, SqlTransaction tx, int orderId)
    {
        var list = new List<(int, int, decimal)>();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        // UPDLOCK+HOLDLOCK: bekleyen satırları işlem boyunca kilitle → eşzamanlı ikinci "mutfağa gönder"
        // aynı satırları okuyup mükerrer stok düşemez (MarkSent ayrıca koşullu → ikili güvence).
        cmd.CommandText = "SELECT Id, ProductId, Qty FROM dbo.OrderLines WITH (UPDLOCK, HOLDLOCK) WHERE OrderId=@o AND IsDeleted=0 AND LineStatus=0;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var rr = cmd.ExecuteReader();
        while (rr.Read()) list.Add((rr.GetInt32(0), rr.GetInt32(1), rr.GetDecimal(2)));
        return list;
    }

    private static void SetLineStatusTx(SqlConnection c, SqlTransaction tx, int lineId, OrderLineStatus status, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE dbo.OrderLines SET LineStatus=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@s", (int)status);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", lineId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Satırı mutfağa gönderildi işaretler + ilk baskı sayacını set eder.
    /// ATOMİK CLAIM: yalnız HÂLÂ Beklemede (LineStatus=0) satırı gönderir; etkilenen satır sayısını döner.
    /// 0 dönerse satır bu sırada başka bir gönderimle kapılmıştır → çağıran stok DÜŞMEMELİDİR (mükerrer düşüm engeli).</summary>
    private static int MarkSentTx(SqlConnection c, SqlTransaction tx, int lineId, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE dbo.OrderLines SET LineStatus=1,
    PrintCount = CASE WHEN PrintCount < 1 THEN 1 ELSE PrintCount END,
    LastPrintedAt = SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND LineStatus=0;";
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", lineId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Alt satırı olan (menü ebeveyni) satır Id'leri.</summary>
    private static HashSet<int> GetMenuParentIds(SqlConnection c, SqlTransaction tx, int orderId)
    {
        var set = new HashSet<int>();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT DISTINCT ParentLineId FROM dbo.OrderLines WHERE OrderId=@o AND ParentLineId IS NOT NULL AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@o", orderId);
        using var rr = cmd.ExecuteReader();
        while (rr.Read()) set.Add(rr.GetInt32(0));
        return set;
    }

    private static int CreateStFiche(SqlConnection c, SqlTransaction tx, int branchId, int orderId, int? userId)
    {
        string code = DocCounter.NextCode(c, tx, branchId, "ST");
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.StFiches (Code, BranchId, OrderId, FicheType, FicheDate, CreatedBy)
OUTPUT INSERTED.Id VALUES (@c,@b,@o,0,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@c", code);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@o", orderId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Bir satırın stok çıkışını reçeteyle oluşturur (reçete yoksa ürünün kendisi).</summary>
    private static void PostLineStock(SqlConnection c, SqlTransaction tx, int stId, int lineId, int productId, decimal qty, int branchId, int? userId)
    {
        var targets = new List<(int Pid, decimal Q)>();
        using (var rc = c.CreateCommand())
        {
            rc.Transaction = tx;
            rc.CommandText = "SELECT ComponentProductId, Quantity FROM dbo.ProductRecipes WHERE MenuProductId=@m AND IsDeleted=0;";
            rc.Parameters.AddWithValue("@m", productId);
            using var rr = rc.ExecuteReader();
            while (rr.Read()) targets.Add((rr.GetInt32(0), qty * rr.GetDecimal(1)));
        }
        if (targets.Count == 0) targets.Add((productId, qty));

        foreach (var (pid, q) in targets)
        {
            int? wh = ConsumeWarehouse(c, tx, pid, branchId);
            if (wh is null) continue;   // tüketim deposu yok = stok takip edilmiyor → atla
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO dbo.StockMovements (StFicheId, OrderLineId, ProductId, WarehouseId, Qty, MovementType, BranchId, MoveDate, CreatedBy)
VALUES (@st,@line,@p,@w,@qty,0,@b,SYSUTCDATETIME(),@by);";
            cmd.Parameters.AddWithValue("@st", stId);
            cmd.Parameters.AddWithValue("@line", lineId);
            cmd.Parameters.AddWithValue("@p", pid);
            cmd.Parameters.AddWithValue("@w", wh.Value);
            cmd.Parameters.AddWithValue("@qty", -q);   // çıkış (negatif)
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Bir satırın daha önce düşülen stoğunu ters kayıtla geri alır (bir kez).</summary>
    private static void ReverseLineStock(SqlConnection c, SqlTransaction tx, int lineId, int branchId, int? userId)
    {
        // zaten ters alınmışsa atla
        using (var chk = c.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(1) FROM dbo.StockMovements WHERE OrderLineId=@l AND MovementType=1;";
            chk.Parameters.AddWithValue("@l", lineId);
            if ((int)chk.ExecuteScalar() > 0) return;
        }
        var moves = new List<(int Pid, int Wh, decimal Qty)>();
        using (var rc = c.CreateCommand())
        {
            rc.Transaction = tx;
            rc.CommandText = "SELECT ProductId, WarehouseId, Qty FROM dbo.StockMovements WHERE OrderLineId=@l AND MovementType=0;";
            rc.Parameters.AddWithValue("@l", lineId);
            using var rr = rc.ExecuteReader();
            while (rr.Read()) moves.Add((rr.GetInt32(0), rr.GetInt32(1), rr.GetDecimal(2)));
        }
        foreach (var (pid, wh, qty) in moves)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO dbo.StockMovements (OrderLineId, ProductId, WarehouseId, Qty, MovementType, BranchId, MoveDate, CreatedBy)
VALUES (@line,@p,@w,@qty,1,@b,SYSUTCDATETIME(),@by);";
            cmd.Parameters.AddWithValue("@line", lineId);
            cmd.Parameters.AddWithValue("@p", pid);
            cmd.Parameters.AddWithValue("@w", wh);
            cmd.Parameters.AddWithValue("@qty", -qty);   // ters (çıkışı geri al)
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static int? ConsumeWarehouse(SqlConnection c, SqlTransaction tx, int productId, int branchId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT TOP 1 ConsumeWarehouseId FROM dbo.ProductBranchSettings WHERE ProductId=@p AND BranchId=@b AND IsDeleted=0 AND ConsumeWarehouseId IS NOT NULL;";
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@b", branchId);
        var r = cmd.ExecuteScalar();
        return r is int id ? id : null;
    }

    private static Order MapOrder(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        BranchId = r.GetInt32(2),
        ZoneId = r.GetInt32(3),
        ZoneName = r.IsDBNull(4) ? null : r.GetString(4),
        CustomerId = r.GetInt32(5),
        CustomerName = r.IsDBNull(6) ? null : r.GetString(6),
        Status = (OrderStatus)r.GetInt32(7),
        Note = r.IsDBNull(8) ? null : r.GetString(8),
        OpenedAt = r.GetDateTime(9),
        ClosedAt = r.IsDBNull(10) ? null : r.GetDateTime(10),
        TableCodes = r.IsDBNull(11) ? null : r.GetString(11),
        Total = r.GetDecimal(12),
        OrderType = (OrderType)r.GetInt32(13),
        CoverCount = r.GetInt32(14),
        DiscountAmount = r.GetDecimal(15),
        ReservationId = r.IsDBNull(16) ? null : r.GetInt32(16),
        Platform = (DeliveryPlatform)r.GetInt32(17),
        DeliveryStatus = (DeliveryStatus)r.GetInt32(18),
        CourierId = r.IsDBNull(19) ? null : r.GetInt32(19),
        CourierName = r.IsDBNull(20) ? null : r.GetString(20),
        DeliveryName = r.IsDBNull(21) ? null : r.GetString(21),
        DeliveryPhone = r.IsDBNull(22) ? null : r.GetString(22),
        DeliveryAddress = r.IsDBNull(23) ? null : r.GetString(23),
        AllergyNote = r.IsDBNull(24) ? null : r.GetString(24),
        Priority = !r.IsDBNull(25) && r.GetBoolean(25),
        IsVip = !r.IsDBNull(26) && r.GetBoolean(26)
    };
}

/// <summary>WaiterReady alarmının payload kaynağı: kalem → sipariş sahibi (garson), masa kodu, ürün adı.</summary>
public record LineWaiterInfo(int? OwnerUserId, string? ProductName, string? TableCode);
