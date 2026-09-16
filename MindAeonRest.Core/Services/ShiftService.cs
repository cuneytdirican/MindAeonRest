using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// KIOSK (terminal) bazlı vardiya: aç/kapa, açık vardiyayı bul-veya-aç, vardiya raporu, liste.
/// Bir KIOSK'ta tek AÇIK vardiya garanti edilir (uygulama seviyesinde + filtered unique index).
/// Nakit fiziksel devri YAPILMAZ — kapanışta yalnız sayılan nakit mutabakat için işaretlenir; kasa terminalde kalır.
/// </summary>
public class ShiftService
{
    /// <summary>Reader'dan Shift nesnesi kurar (kolon sırası sabit SELECT'lere bağlı).</summary>
    private static Shift Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        BranchId = r.GetInt32(1),
        KioskNo = r.GetInt32(2),
        OpenedBy = r.IsDBNull(3) ? null : r.GetInt32(3),
        OpenedAt = r.GetDateTime(4),
        ClosedBy = r.IsDBNull(5) ? null : r.GetInt32(5),
        ClosedAt = r.IsDBNull(6) ? null : r.GetDateTime(6),
        OpeningFloat = r.GetDecimal(7),
        ClosingCounted = r.IsDBNull(8) ? null : r.GetDecimal(8),
        Status = (ShiftStatus)r.GetInt32(9),
        Note = r.IsDBNull(10) ? null : r.GetString(10)
    };

    private const string SelectCols =
        "Id, BranchId, KioskNo, OpenedBy, OpenedAt, ClosedBy, ClosedAt, OpeningFloat, ClosingCounted, Status, Note";
    // OUTPUT yan tümcesinde HER kolon INSERTED. ile nitelenmeli; aksi halde "INSERTED.{SelectCols}" yalnız ilk kolonu niteler
    // ve kalanlar öneksiz kalıp geçersiz olur (SQL 207). Bu yüzden OUTPUT için ayrı, tam-nitelenmiş liste kullanılır.
    private static readonly string OutputInsertedCols =
        string.Join(", ", SelectCols.Split(',').Select(c => "INSERTED." + c.Trim()));

    /// <summary>Belirtilen terminalde AÇIK vardiyayı döndürür (yoksa null). Aynı tx altında kullanım için.</summary>
    private static Shift? GetOpenShiftTx(SqlConnection c, SqlTransaction? tx, int branchId, int kioskNo)
    {
        using var cmd = c.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectCols} FROM dbo.Shifts WHERE BranchId=@b AND KioskNo=@k AND Status=0 AND IsDeleted=0 ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@k", kioskNo);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    /// <summary>Terminalde yeni vardiya açar. Açık vardiya zaten varsa hata. Aynı tx altında kullanım için.</summary>
    private static Shift InsertOpenTx(SqlConnection c, SqlTransaction tx, int branchId, int kioskNo, decimal openingFloat, int? userId)
    {
        if (GetOpenShiftTx(c, tx, branchId, kioskNo) != null)
            throw new InvalidOperationException("Bu terminalde açık vardiya var.");
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $@"
INSERT INTO dbo.Shifts (BranchId, KioskNo, OpenedBy, OpenedAt, OpeningFloat, Status, CreatedBy)
OUTPUT {OutputInsertedCols}
VALUES (@b,@k,@by,SYSUTCDATETIME(),@float,0,@by);";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@k", kioskNo);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@float", openingFloat);
        using var r = cmd.ExecuteReader();
        r.Read();
        return Map(r);
    }

    /// <summary>Terminalde yeni vardiya açar; açık vardiya zaten varsa InvalidOperationException.</summary>
    public Shift OpenShift(int branchId, int kioskNo, decimal openingFloat, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try { var s = InsertOpenTx(c, tx, branchId, kioskNo, openingFloat, userId); tx.Commit(); return s; }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Terminaldeki AÇIK vardiyayı döndürür (yoksa null).</summary>
    public Shift? GetOpenShift(int branchId, int kioskNo)
    {
        using var c = Database.OpenCompany();
        return GetOpenShiftTx(c, null, branchId, kioskNo);
    }

    /// <summary>Açık vardiya varsa onu döndürür; yoksa OpeningFloat=0 ile açar (otomatik vardiya).</summary>
    public Shift GetOrOpenCurrent(int branchId, int kioskNo, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var open = GetOpenShiftTx(c, tx, branchId, kioskNo);
            var s = open ?? InsertOpenTx(c, tx, branchId, kioskNo, 0m, userId);
            tx.Commit();
            return s;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>
    /// Aynı bağlantı+transaction altında (ör. SettleLines) geçerli vardiyanın Id'sini döndürür;
    /// açık vardiya yoksa OpeningFloat=0 ile açıp Id'sini döndürür. ClFiche INSERT'ten ÖNCE çağrılır.
    /// </summary>
    internal static int GetOrOpenCurrentTx(SqlConnection c, SqlTransaction tx, int branchId, int kioskNo, int? userId)
    {
        var open = GetOpenShiftTx(c, tx, branchId, kioskNo);
        return (open ?? InsertOpenTx(c, tx, branchId, kioskNo, 0m, userId)).Id;
    }

    /// <summary>Vardiyayı kapatır: sayılan nakit + not işaretlenir. Zaten kapalıysa hata. (Nakit fiziksel devri yapılmaz.)</summary>
    public void CloseShift(int shiftId, decimal countedCash, string? note, int? userId)
    {
        using var c = Database.OpenCompany();

        // AUDIT: kapanış öncesi vardiya durumu (para mutabakatı izi) — UPDATE'ten ÖNCE snapshot.
        Shift? before = null;
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = $"SELECT {SelectCols} FROM dbo.Shifts WHERE Id=@id;";
            sel.Parameters.AddWithValue("@id", shiftId);
            using var rr = sel.ExecuteReader();
            if (rr.Read()) before = Map(rr);
        }

        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.Shifts
SET Status=1, ClosedAt=SYSUTCDATETIME(), ClosedBy=@by, ClosingCounted=@counted, Note=@note,
    UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND Status=0;";
        cmd.Parameters.AddWithValue("@id", shiftId);
        cmd.Parameters.AddWithValue("@counted", countedCash);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Vardiya bulunamadı veya zaten kapalı.");

        // AUDIT (best-effort): vardiya kapanışı (sayılan nakit + not).
        AuditService.Write("Shift", shiftId, "CloseShift",
            before is null ? null : new { before.KioskNo, before.OpeningFloat, before.Status },
            new { KioskNo = before?.KioskNo, ClosingCounted = countedCash, Note = note },
            userId);
    }

    /// <summary>
    /// Vardiya raporu: o vardiyanın fişleri (ClFiches WHERE ShiftId) üzerinden ödeme yöntemi kırılımı,
    /// tahsil eden kullanıcı (ClFiche.CreatedBy) kırılımı, nakit tahsilat ve beklenen kasa.
    /// </summary>
    public ShiftReport GetReport(int shiftId)
    {
        var rep = new ShiftReport { ShiftId = shiftId };
        using var c = Database.OpenCompany();

        // Vardiya başlığı (OpenedAt/ClosedAt/OpeningFloat)
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT OpenedAt, ClosedAt, OpeningFloat FROM dbo.Shifts WHERE Id=@s;";
            cmd.Parameters.AddWithValue("@s", shiftId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("Vardiya bulunamadı.");
            rep.OpenedAt = r.GetDateTime(0);
            rep.ClosedAt = r.IsDBNull(1) ? null : r.GetDateTime(1);
            rep.OpeningFloat = r.GetDecimal(2);
        }

        // Ödeme yöntemi (Kind) kırılımı
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT p.Kind, SUM(p.Amount) FROM dbo.PaymentLines p
JOIN dbo.ClFiches f ON f.Id=p.ClFicheId
WHERE f.IsDeleted=0 AND f.ShiftId=@s
GROUP BY p.Kind;";
            cmd.Parameters.AddWithValue("@s", shiftId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kind = (PaymentKind)r.GetInt32(0);
                decimal amt = r.IsDBNull(1) ? 0 : r.GetDecimal(1);
                rep.Payments.Add(new ReportPay { Kind = kind, Amount = amt });
                rep.Total += amt;
            }
        }

        // Beklenen kasa için NET nakit: fişe bağlı kasa hareketlerinin İŞARETLİ toplamı (Tahsilat + para üstü Çıkış).
        // PaymentLines brüt (tendered) nakit tutarı tutar; para üstü Çıkış düşülmezse beklenen kasa fazla görünürdü.
        // Yalnız RefClFicheId dolu (satış kaynaklı) hareketler; manuel giriş/çıkış ve kasa transferleri hariç.
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ISNULL(SUM(cm.Amount),0) FROM dbo.CashMovements cm
JOIN dbo.ClFiches f ON f.Id = cm.RefClFicheId
WHERE f.IsDeleted=0 AND f.ShiftId=@s;";
            cmd.Parameters.AddWithValue("@s", shiftId);
            rep.CashCollected = (decimal)cmd.ExecuteScalar()!;
        }

        // Tahsil eden kullanıcı (ClFiche.CreatedBy) kırılımı
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT f.CreatedBy, SUM(p.Amount) FROM dbo.PaymentLines p
JOIN dbo.ClFiches f ON f.Id=p.ClFicheId
WHERE f.IsDeleted=0 AND f.ShiftId=@s
GROUP BY f.CreatedBy;";
            cmd.Parameters.AddWithValue("@s", shiftId);
            var users = new UserService().GetAll().ToDictionary(u => u.Id, u => u.FullName);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int? uid = r.IsDBNull(0) ? null : r.GetInt32(0);
                decimal amt = r.IsDBNull(1) ? 0 : r.GetDecimal(1);
                rep.ByUser.Add(new ShiftUserLine
                {
                    UserId = uid ?? 0,
                    UserName = uid is int u && users.TryGetValue(u, out var n) ? n : "-",
                    Amount = amt
                });
            }
        }
        return rep;
    }

    /// <summary>Vardiya listesi — şube + (opsiyonel) açılış tarihi aralığı (yerel gün) filtreli.</summary>
    public List<Shift> ListShifts(int branchId, DateTime? from, DateTime? to)
    {
        var list = new List<Shift>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT {SelectCols} FROM dbo.Shifts
WHERE BranchId=@b AND IsDeleted=0
  AND (@from IS NULL OR OpenedAt >= @from)
  AND (@to   IS NULL OR OpenedAt <  @to)
ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }
}
