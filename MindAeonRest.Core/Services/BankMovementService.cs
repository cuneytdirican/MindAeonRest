using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Banka hesabı hareketleri: tahsilat/ödeme (cari entegre), manuel giriş/çıkış, hesaplar arası transfer, bakiye.</summary>
public class BankMovementService
{
    private static (int Currency, string Name) AccountInfo(SqlConnection c, SqlTransaction? tx, int bankAccountId)
    {
        using var cmd = c.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT CurrencyId, Name FROM dbo.BankAccounts WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", bankAccountId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Banka hesabı bulunamadı.");
        return (r.GetInt32(0), r.GetString(1));
    }

    internal static void Post(SqlConnection c, SqlTransaction tx, int branchId, int bankAccountId, int currencyId,
        BankMovementType type, decimal amount, int? customerId, int? counterId, string? note, int? userId)
    {
        decimal signed = type.IsInflow() ? Math.Abs(amount) : -Math.Abs(amount);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.BankMovements (BranchId, BankAccountId, CurrencyId, Type, Amount, CustomerId, CounterBankAccountId, Note, MoveDate, CreatedBy)
VALUES (@b,@acc,@cur,@t,@a,@cust,@cnt,@note,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@acc", bankAccountId);
        cmd.Parameters.AddWithValue("@cur", currencyId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@a", signed);
        cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cnt", (object?)counterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Manuel hareket. Tahsilat/Ödeme'de customerId verilirse cari hareket de yazılır.</summary>
    public void AddManual(int bankAccountId, BankMovementType type, decimal amount, int? customerId, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (type is BankMovementType.TransferGiris or BankMovementType.TransferCikis) throw new ArgumentException("Transfer için Transfer fonksiyonunu kullanın.");
        if (type is BankMovementType.Tahsilat or BankMovementType.Odeme && customerId is null or 0)
            throw new ArgumentException("Tahsilat/Ödeme için cari seçilmeli.");

        int branch = Session_BranchId();
        using var c = Database.OpenCompany();
        var (cur, name) = AccountInfo(c, null, bankAccountId);
        using var tx = c.BeginTransaction();
        try
        {
            Post(c, tx, branch, bankAccountId, cur, type, amount, customerId, null, note, userId);
            if (customerId is int cid && cid > 0 && type is BankMovementType.Tahsilat or BankMovementType.Odeme)
            {
                bool tahsilat = type == BankMovementType.Tahsilat;
                CariHareketService.PostTx(c, tx, new CariHareket
                {
                    BranchId = branch, CustomerId = cid, TxnDate = DateTime.Now,
                    DocType = tahsilat ? CariDocType.Tahsilat : CariDocType.Odeme,
                    DocRefType = "Banka", Description = $"Banka {type.ToTr()} · {name}" + (note is null ? "" : $" · {note}"),
                    CurrencyId = cur,
                    Debt = tahsilat ? 0 : amount,     // ödeme → cari borç (alacağı azalır)
                    Credit = tahsilat ? amount : 0    // tahsilat → cari alacak (borcu azalır)
                }, userId);
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Banka hesapları arası transfer (aynı döviz).</summary>
    public void Transfer(int fromId, int toId, decimal amount, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (fromId == toId) throw new ArgumentException("Kaynak ve hedef hesap aynı olamaz.");
        int branch = Session_BranchId();
        using var c = Database.OpenCompany();
        var f = AccountInfo(c, null, fromId);
        var t = AccountInfo(c, null, toId);
        if (f.Currency != t.Currency) throw new InvalidOperationException("Transfer için hesapların dövizi aynı olmalı.");
        using var tx = c.BeginTransaction();
        try
        {
            Post(c, tx, branch, fromId, f.Currency, BankMovementType.TransferCikis, amount, null, toId, note, userId);
            Post(c, tx, branch, toId, t.Currency, BankMovementType.TransferGiris, amount, null, fromId, note, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public List<BankBalance> GetBalances()
    {
        var list = new List<BankBalance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT a.Id, a.Code, a.Name, bk.Name AS BankName, a.CurrencyId, cur.Code,
       (SELECT ISNULL(SUM(m.Amount),0) FROM dbo.BankMovements m WHERE m.BankAccountId=a.Id)
FROM dbo.BankAccounts a
LEFT JOIN dbo.Banks bk ON bk.Id=a.BankId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id=a.CurrencyId
WHERE a.IsDeleted=0 ORDER BY a.Code;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BankBalance
            {
                BankAccountId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                BankName = r.IsDBNull(3) ? null : r.GetString(3),
                CurrencyId = r.GetInt32(4), CurrencyCode = r.IsDBNull(5) ? null : r.GetString(5),
                Balance = r.GetDecimal(6)
            });
        return list;
    }

    public List<BankMovement> GetMovements(int bankAccountId, int top = 200)
    {
        var list = new List<BankMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT TOP {top} m.Id, m.BranchId, m.BankAccountId, m.CurrencyId, m.Type, m.Amount, m.CustomerId, m.CounterBankAccountId, m.Note, m.MoveDate, cu.Name
FROM dbo.BankMovements m
LEFT JOIN dbo.Customers cu ON cu.Id=m.CustomerId
WHERE m.BankAccountId=@a ORDER BY m.Id DESC;";
        cmd.Parameters.AddWithValue("@a", bankAccountId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BankMovement
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), BankAccountId = r.GetInt32(2), CurrencyId = r.GetInt32(3),
                Type = (BankMovementType)r.GetInt32(4), Amount = r.GetDecimal(5),
                CustomerId = r.IsDBNull(6) ? null : r.GetInt32(6),
                CounterBankAccountId = r.IsDBNull(7) ? null : r.GetInt32(7),
                Note = r.IsDBNull(8) ? null : r.GetString(8), MoveDate = r.GetDateTime(9),
                CustomerName = r.IsDBNull(10) ? null : r.GetString(10)
            });
        return list;
    }

    private static int Session_BranchId() => Helpers.Session.CurrentBranch?.Id ?? 0;
}
