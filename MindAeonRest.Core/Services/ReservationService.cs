using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Rezervasyonlar (şirket DB). Bir müşteri, zaman aralığı, bir/çok masa, opsiyonel etiket. Çakışma kontrollü.</summary>
public class ReservationService
{
    private const string SelectBase = @"
SELECT r.Id, r.Code, r.BranchId, r.ZoneId, z.Name AS ZoneName, r.CustomerId, c.Name AS CustomerName,
       r.TagId, tg.Name AS TagName, tg.Color AS TagColor, r.Note, r.StartTime, r.EndTime, r.PartySize, r.Status, r.IsActive,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.ReservationTables rt JOIN dbo.Tables t ON t.Id=rt.TableId WHERE rt.ReservationId=r.Id) AS TableCodes
FROM dbo.Reservations r
LEFT JOIN dbo.Zones z ON z.Id = r.ZoneId
LEFT JOIN dbo.Customers c ON c.Id = r.CustomerId
LEFT JOIN dbo.ReservationTags tg ON tg.Id = r.TagId";

    public List<Reservation> GetByDate(int zoneId, DateTime date)
    {
        var list = new List<Reservation>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE r.ZoneId=@z AND r.IsDeleted=0 AND CAST(r.StartTime AS DATE)=@d ORDER BY r.StartTime;";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@d", date.Date);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        foreach (var r in list) r.TableIds = GetTableIds(r.Id);
        return list;
    }

    /// <summary>Back-office liste: firma geneli (zone bağımsız), opsiyonel tarih + silinenler. TableIds doldurulur.</summary>
    public List<Reservation> GetForList(DateTime? date = null, bool includeDeleted = false)
    {
        var list = new List<Reservation>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase +
            " WHERE (@inc=1 OR r.IsDeleted=0) AND (@d IS NULL OR CAST(r.StartTime AS DATE)=@d) ORDER BY r.StartTime DESC;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        command.Parameters.AddWithValue("@d", (object?)date?.Date ?? DBNull.Value);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) list.Add(Map(reader));
        foreach (var r in list) r.TableIds = GetTableIds(r.Id);
        return list;
    }

    /// <summary>Tek rezervasyon (silinmiş dahil) — düzenleme ekranı için. TableIds doldurulur.</summary>
    public Reservation? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE r.Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        Reservation? res;
        using (var reader = command.ExecuteReader())
            res = reader.Read() ? Map(reader) : null;
        if (res is not null) res.TableIds = GetTableIds(res.Id);
        return res;
    }

    public List<int> GetTableIds(int reservationId)
    {
        var ids = new List<int>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TableId FROM dbo.ReservationTables WHERE ReservationId=@id;";
        command.Parameters.AddWithValue("@id", reservationId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    /// <summary>Verilen zaman aralığında (iptal/gelmedi hariç) dolu masa Id'leri.</summary>
    public HashSet<int> GetReservedTableIds(int zoneId, DateTime start, DateTime end, int excludeReservationId = 0)
    {
        var set = new HashSet<int>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT DISTINCT rt.TableId
FROM dbo.Reservations r JOIN dbo.ReservationTables rt ON rt.ReservationId = r.Id
WHERE r.ZoneId=@z AND r.IsDeleted=0 AND r.Status NOT IN (4,5) AND r.Id<>@ex
  AND r.StartTime < @end AND @start < r.EndTime;";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@ex", excludeReservationId);
        command.Parameters.AddWithValue("@start", start);
        command.Parameters.AddWithValue("@end", end);
        using var reader = command.ExecuteReader();
        while (reader.Read()) set.Add(reader.GetInt32(0));
        return set;
    }

    public Reservation Save(Reservation r, IEnumerable<int> tableIds, int? userId)
    {
        var tables = tableIds.Distinct().ToList();
        if (r.CustomerId == 0) throw new ArgumentException("Müşteri seçiniz.");
        if (r.ZoneId == 0) throw new ArgumentException("Lokasyon seçiniz.");
        if (tables.Count == 0) throw new ArgumentException("En az bir masa seçiniz.");
        if (r.EndTime <= r.StartTime) throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.");

        // Çakışma kontrolü
        var reserved = GetReservedTableIds(r.ZoneId, r.StartTime, r.EndTime, r.Id);
        var conflict = tables.Where(reserved.Contains).ToList();
        if (conflict.Count > 0)
        {
            using var c2 = Database.OpenCompany();
            using var q = c2.CreateCommand();
            q.CommandText = $"SELECT STRING_AGG(Code, ', ') FROM dbo.Tables WHERE Id IN ({string.Join(",", conflict)});";
            var codes = q.ExecuteScalar() as string ?? string.Join(",", conflict);
            throw new InvalidOperationException($"Seçilen zaman aralığında şu masa(lar) dolu: {codes}");
        }

        using var connection = Database.OpenCompany();
        if (string.IsNullOrWhiteSpace(r.Code)) r.Code = NextCode(connection);

        using (var command = connection.CreateCommand())
        {
            if (r.Id == 0)
                command.CommandText = @"
INSERT INTO dbo.Reservations (Code, BranchId, ZoneId, CustomerId, TagId, Note, StartTime, EndTime, PartySize, Status, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code,@Branch,@Zone,@Cust,@Tag,@Note,@Start,@End,@Party,@Status,1,@By);";
            else
            {
                command.CommandText = @"
UPDATE dbo.Reservations SET BranchId=@Branch, ZoneId=@Zone, CustomerId=@Cust, TagId=@Tag, Note=@Note,
    StartTime=@Start, EndTime=@End, PartySize=@Party, Status=@Status, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
                command.Parameters.AddWithValue("@Id", r.Id);
            }
            command.Parameters.AddWithValue("@Code", r.Code);
            command.Parameters.AddWithValue("@Branch", r.BranchId);
            command.Parameters.AddWithValue("@Zone", r.ZoneId);
            command.Parameters.AddWithValue("@Cust", r.CustomerId);
            command.Parameters.AddWithValue("@Tag", (object?)r.TagId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Note", (object?)r.Note ?? DBNull.Value);
            command.Parameters.AddWithValue("@Start", r.StartTime);
            command.Parameters.AddWithValue("@End", r.EndTime);
            command.Parameters.AddWithValue("@Party", r.PartySize);
            command.Parameters.AddWithValue("@Status", (int)r.Status);
            command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

            if (r.Id == 0) r.Id = (int)command.ExecuteScalar()!;
            else command.ExecuteNonQuery();
        }

        // Masa bağlantılarını yenile
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "DELETE FROM dbo.ReservationTables WHERE ReservationId=@id;";
            del.Parameters.AddWithValue("@id", r.Id);
            del.ExecuteNonQuery();
        }
        foreach (int tid in tables)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = "INSERT INTO dbo.ReservationTables (ReservationId, TableId) VALUES (@r,@t);";
            ins.Parameters.AddWithValue("@r", r.Id);
            ins.Parameters.AddWithValue("@t", tid);
            ins.ExecuteNonQuery();
        }
        return r;
    }

    /// <summary>ONLINE (public) rezervasyon için: verilen zaman aralığında şube genelinde MÜSAİT masaları döndürür
    /// (aktif, silinmemiş, çakışmayan; kişi sayısına yeten en iyi eşleşme önce). Çakışma motoru mevcut mantıkla
    /// (StartTime &lt; end AND start &lt; EndTime, Status NOT IN (İptal,Gelmedi)) — sadece şube geneline genişletildi.</summary>
    public List<Table> GetAvailableTables(int branchId, DateTime start, DateTime end, int partySize)
    {
        // 1) Şube genelinde bu aralıkta dolu masa Id'leri (tüm zone'lar).
        var reserved = new HashSet<int>();
        using var connection = Database.OpenCompany();
        using (var rc = connection.CreateCommand())
        {
            rc.CommandText = @"
SELECT DISTINCT rt.TableId
FROM dbo.Reservations r JOIN dbo.ReservationTables rt ON rt.ReservationId = r.Id
WHERE r.BranchId=@b AND r.IsDeleted=0 AND r.Status NOT IN (4,5)
  AND r.StartTime < @end AND @start < r.EndTime;";
            rc.Parameters.AddWithValue("@b", branchId);
            rc.Parameters.AddWithValue("@start", start);
            rc.Parameters.AddWithValue("@end", end);
            using var rr = rc.ExecuteReader();
            while (rr.Read()) reserved.Add(rr.GetInt32(0));
        }

        // 2) Şubenin aktif masaları; dolu olanları ele; kişi sayısına yeten en iyi eşleşme önce.
        var list = new List<Table>();
        using (var tc = connection.CreateCommand())
        {
            tc.CommandText = @"
SELECT t.Id, t.ZoneId, t.BranchId, t.Code, t.Name, t.Seats
FROM dbo.Tables t JOIN dbo.Zones z ON z.Id = t.ZoneId
WHERE t.BranchId=@b AND t.IsActive=1 AND t.IsDeleted=0 AND z.IsActive=1 AND z.IsDeleted=0
ORDER BY t.Seats;";
            tc.Parameters.AddWithValue("@b", branchId);
            using var tr = tc.ExecuteReader();
            while (tr.Read())
            {
                int id = tr.GetInt32(0);
                if (reserved.Contains(id)) continue;
                list.Add(new Table
                {
                    Id = id,
                    ZoneId = tr.GetInt32(1),
                    BranchId = tr.GetInt32(2),
                    Code = tr.GetString(3),
                    Name = tr.IsDBNull(4) ? null : tr.GetString(4),
                    Seats = tr.GetInt32(5)
                });
            }
        }

        // Kişi sayısına yeten masalar önce (en yakın kapasite), sonra kalanlar. partySize<=0 ise dokunma.
        if (partySize > 0)
            return list.OrderBy(t => t.Seats >= partySize ? 0 : 1)
                       .ThenBy(t => Math.Abs(t.Seats - partySize)).ToList();
        return list;
    }

    /// <summary>ONLINE (public) rezervasyon oluşturur: uygun masayı otomatik seçer (en iyi kapasite eşleşmesi),
    /// telefona göre müşteriyi bulur/oluşturur, mevcut çakışma-kontrollü <see cref="Save"/> ile kaydeder.
    /// UYGUN MASA YOKSA <c>null</c> döner (çağıran bekleme listesine alır). Şube kapsamlı; userId null (anonim).</summary>
    public Reservation? CreatePublicReservation(int branchId, string name, string phone, int partySize,
        DateTime start, DateTime end, string? note)
    {
        if (partySize <= 0) partySize = 1;
        var available = GetAvailableTables(branchId, start, end, partySize);
        // Kişi sayısına yeten hiç masa yoksa → bekleme listesine (null). Yeten yoksa ama boş masa varsa da
        // (küçük masalara sığmıyor) doluluk sayılır → null.
        var table = available.FirstOrDefault(t => t.Seats >= partySize) ?? available.FirstOrDefault();
        if (table is null) return null;

        int customerId = EnsurePublicCustomer(name, phone);
        var r = new Reservation
        {
            BranchId = branchId,
            ZoneId = table.ZoneId,
            CustomerId = customerId,
            StartTime = start,
            EndTime = end,
            PartySize = partySize,
            Note = note,
            Status = ReservationStatus.Beklemede
        };
        return Save(r, new[] { table.Id }, null);
    }

    /// <summary>Public rezervasyonda telefona göre müşteriyi bulur; yoksa "ONLINE" prefixli benzersiz kodla oluşturur.
    /// Cari kodu benzersizliği korunur; elle giriş yerine deterministik kod üretilir (form kuralı).</summary>
    private static int EnsurePublicCustomer(string name, string phone)
    {
        using var connection = Database.OpenCompany();

        // 1) Telefona göre mevcut (silinmemiş) müşteriyi bul.
        if (!string.IsNullOrWhiteSpace(phone))
        {
            using var find = connection.CreateCommand();
            find.CommandText = "SELECT TOP 1 Id FROM dbo.Customers WHERE Phone=@p AND IsDeleted=0 ORDER BY Id;";
            find.Parameters.AddWithValue("@p", phone.Trim());
            if (find.ExecuteScalar() is int existingId) return existingId;
        }

        // 2) Benzersiz kod üret (ONLINE + telefon rakamları / zaman damgası).
        string digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        string baseCode = "ONLINE" + (digits.Length > 0 ? digits : DateTime.UtcNow.ToString("yyMMddHHmmss"));
        string code = baseCode;
        int suffix = 1;
        while (true)
        {
            using var chk = connection.CreateCommand();
            chk.CommandText = "SELECT COUNT(1) FROM dbo.Customers WHERE Code=@c;";
            chk.Parameters.AddWithValue("@c", code);
            if ((int)chk.ExecuteScalar()! == 0) break;
            code = baseCode + "-" + (++suffix);
        }

        using var ins = connection.CreateCommand();
        ins.CommandText = @"INSERT INTO dbo.Customers (Code, Name, Phone, CustomerType, IsActive, CreatedBy)
                            OUTPUT INSERTED.Id VALUES (@c,@n,@p,0,1,NULL);";
        ins.Parameters.AddWithValue("@c", code);
        ins.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(name) ? "Online Müşteri" : name.Trim());
        ins.Parameters.AddWithValue("@p", string.IsNullOrWhiteSpace(phone) ? (object)DBNull.Value : phone.Trim());
        return (int)ins.ExecuteScalar()!;
    }

    public void SetStatus(int reservationId, ReservationStatus status, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Reservations SET Status=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@s", (int)status);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", reservationId);
        command.ExecuteNonQuery();
    }

    private static string NextCode(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(Code, 4, 20))), 0) + 1
FROM dbo.Reservations WHERE Code LIKE 'REZ%' AND TRY_CONVERT(INT, SUBSTRING(Code, 4, 20)) IS NOT NULL;";
        int n = (int)command.ExecuteScalar()!;
        return $"REZ{n}";
    }

    private static Reservation Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        BranchId = r.GetInt32(2),
        ZoneId = r.GetInt32(3),
        ZoneName = r.IsDBNull(4) ? null : r.GetString(4),
        CustomerId = r.GetInt32(5),
        CustomerName = r.IsDBNull(6) ? null : r.GetString(6),
        TagId = r.IsDBNull(7) ? null : r.GetInt32(7),
        TagName = r.IsDBNull(8) ? null : r.GetString(8),
        TagColor = r.IsDBNull(9) ? null : r.GetString(9),
        Note = r.IsDBNull(10) ? null : r.GetString(10),
        StartTime = r.GetDateTime(11),
        EndTime = r.GetDateTime(12),
        PartySize = r.GetInt32(13),
        Status = (ReservationStatus)r.GetInt32(14),
        IsActive = r.GetBoolean(15),
        TableCodes = r.IsDBNull(16) ? null : r.GetString(16)
    };
}
