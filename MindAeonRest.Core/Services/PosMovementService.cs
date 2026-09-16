using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Kredi kartı POS hareketleri: müşteriden tahsilat / karttan ödeme (cari entegre), transfer, bakiye.</summary>
public class PosMovementService
{
    private static (int Currency, string Name) PosInfo(SqlConnection c, int posId, SqlTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT CurrencyId, Name FROM dbo.CreditCardPos WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", posId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("POS bulunamadı.");
        return (r.GetInt32(0), r.GetString(1));
    }

    internal static void Post(SqlConnection c, SqlTransaction tx, int branchId, int posId, int currencyId,
        BankMovementType type, decimal amount, int? customerId, int? counterId, string? note, int? userId)
    {
        decimal signed = type.IsInflow() ? Math.Abs(amount) : -Math.Abs(amount);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.PosMovements (BranchId, PosId, CurrencyId, Type, Amount, CustomerId, CounterPosId, Note, MoveDate, CreatedBy, KioskNo)
VALUES (@b,@pos,@cur,@t,@a,@cust,@cnt,@note,SYSUTCDATETIME(),@by,@kno);";
        cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@pos", posId);
        cmd.Parameters.AddWithValue("@cur", currencyId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@a", signed);
        cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cnt", (object?)counterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void AddManual(int posId, BankMovementType type, decimal amount, int? customerId, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (type is BankMovementType.TransferGiris or BankMovementType.TransferCikis) throw new ArgumentException("Transfer için Transfer fonksiyonunu kullanın.");
        if (type is BankMovementType.Tahsilat or BankMovementType.Odeme && customerId is null or 0)
            throw new ArgumentException("Tahsilat/Ödeme için cari seçilmeli.");

        int branch = Session.CurrentBranch?.Id ?? 0;
        using var c = Database.OpenCompany();
        var (cur, name) = PosInfo(c, posId);
        using var tx = c.BeginTransaction();
        try
        {
            Post(c, tx, branch, posId, cur, type, amount, customerId, null, note, userId);
            if (customerId is int cid && cid > 0 && type is BankMovementType.Tahsilat or BankMovementType.Odeme)
            {
                bool tahsilat = type == BankMovementType.Tahsilat;
                CariHareketService.PostTx(c, tx, new CariHareket
                {
                    BranchId = branch, CustomerId = cid, TxnDate = DateTime.Now,
                    DocType = tahsilat ? CariDocType.Tahsilat : CariDocType.Odeme,
                    DocRefType = "POS", Description = $"Kart {type.ToTr()} · {name}" + (note is null ? "" : $" · {note}"),
                    CurrencyId = cur, Debt = tahsilat ? 0 : amount, Credit = tahsilat ? amount : 0
                }, userId);
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public void Transfer(int fromId, int toId, decimal amount, string? note, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try { Transfer(c, tx, fromId, toId, amount, note, userId); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Mevcut bağlantı+transaction altında POS'lar arası transfer (atomik akışlar için, ör. Z devri).</summary>
    public void Transfer(SqlConnection c, SqlTransaction tx, int fromId, int toId, decimal amount, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (fromId == toId) throw new ArgumentException("Kaynak ve hedef POS aynı olamaz.");
        int branch = Session.CurrentBranch?.Id ?? 0;
        var f = PosInfo(c, fromId, tx);
        var t = PosInfo(c, toId, tx);
        if (f.Currency != t.Currency) throw new InvalidOperationException("Transfer için POS'ların dövizi aynı olmalı.");
        Post(c, tx, branch, fromId, f.Currency, BankMovementType.TransferCikis, amount, null, toId, note, userId);
        Post(c, tx, branch, toId, t.Currency, BankMovementType.TransferGiris, amount, null, fromId, note, userId);
    }

    public List<PosBalance> GetBalances()
    {
        var list = new List<PosBalance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT p.Id, p.Code, p.Name, bk.Name, p.CurrencyId, cur.Code,
       (SELECT ISNULL(SUM(m.Amount),0) FROM dbo.PosMovements m WHERE m.PosId=p.Id)
FROM dbo.CreditCardPos p
LEFT JOIN dbo.Banks bk ON bk.Id=p.BankId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id=p.CurrencyId
WHERE p.IsDeleted=0 ORDER BY p.Code;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PosBalance
            {
                PosId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                BankName = r.IsDBNull(3) ? null : r.GetString(3),
                CurrencyId = r.GetInt32(4), CurrencyCode = r.IsDBNull(5) ? null : r.GetString(5),
                Balance = r.GetDecimal(6)
            });
        return list;
    }

    public List<PosMovement> GetMovements(int posId, int top = 200)
    {
        var list = new List<PosMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT TOP {top} m.Id, m.BranchId, m.PosId, m.CurrencyId, m.Type, m.Amount, m.CustomerId, m.CounterPosId, m.Note, m.MoveDate, cu.Name
FROM dbo.PosMovements m
LEFT JOIN dbo.Customers cu ON cu.Id=m.CustomerId
WHERE m.PosId=@p ORDER BY m.Id DESC;";
        cmd.Parameters.AddWithValue("@p", posId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PosMovement
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), PosId = r.GetInt32(2), CurrencyId = r.GetInt32(3),
                Type = (BankMovementType)r.GetInt32(4), Amount = r.GetDecimal(5),
                CustomerId = r.IsDBNull(6) ? null : r.GetInt32(6),
                CounterPosId = r.IsDBNull(7) ? null : r.GetInt32(7),
                Note = r.IsDBNull(8) ? null : r.GetString(8), MoveDate = r.GetDateTime(9),
                CustomerName = r.IsDBNull(10) ? null : r.GetString(10)
            });
        return list;
    }
}
