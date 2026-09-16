using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Nakit kasa hareketleri: tahsilat (ödemeden), manuel giriş/çıkış, kasalar arası transfer, bakiye.</summary>
public class CashMovementService
{
    /// <summary>Transaction içinde hareket ekler (ör. ödeme sırasında). amount pozitif verilir; işaret tipe göre.</summary>
    internal static void Post(SqlConnection c, SqlTransaction tx, int branchId, int cashRegisterId, int currencyId,
        CashMovementType type, decimal amount, int? refClFicheId, int? counterId, string? note, int? userId)
    {
        decimal signed = type is CashMovementType.Cikis or CashMovementType.TransferCikis ? -Math.Abs(amount) : Math.Abs(amount);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.CashMovements (BranchId, CashRegisterId, CurrencyId, Type, Amount, RefClFicheId, CounterCashRegisterId, Note, MoveDate, CreatedBy, KioskNo)
VALUES (@b,@k,@cur,@t,@a,@ref,@cnt,@note,SYSUTCDATETIME(),@by,@kno);";
        cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@k", cashRegisterId);
        cmd.Parameters.AddWithValue("@cur", currencyId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@a", signed);
        cmd.Parameters.AddWithValue("@ref", (object?)refClFicheId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cnt", (object?)counterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static (int Branch, int Currency) KasaInfo(SqlConnection c, SqlTransaction? tx, int cashRegisterId)
    {
        using var cmd = c.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT BranchId, CurrencyId FROM dbo.CashRegisters WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", cashRegisterId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Kasa bulunamadı.");
        return (r.GetInt32(0), r.GetInt32(1));
    }

    public void AddManual(int cashRegisterId, CashMovementType type, decimal amount, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (type is not (CashMovementType.Giris or CashMovementType.Cikis)) throw new ArgumentException("Yalnızca giriş/çıkış.");
        using var c = Database.OpenCompany();
        var (branch, cur) = KasaInfo(c, null, cashRegisterId);
        using var tx = c.BeginTransaction();
        try { Post(c, tx, branch, cashRegisterId, cur, type, amount, null, null, note, userId); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Müşteriden nakit tahsilat / tedarikçiye nakit ödeme — kasa hareketi + cari hareketi (aynı tx).</summary>
    public void AddCari(int cashRegisterId, bool tahsilat, decimal amount, int customerId, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (customerId == 0) throw new ArgumentException("Cari seçilmeli.");
        using var c = Database.OpenCompany();
        var (branch, cur) = KasaInfo(c, null, cashRegisterId);
        using var tx = c.BeginTransaction();
        try
        {
            Post(c, tx, branch, cashRegisterId, cur, tahsilat ? CashMovementType.Tahsilat : CashMovementType.Cikis, amount, null, null, note, userId);
            CariHareketService.PostTx(c, tx, new CariHareket
            {
                BranchId = branch, CustomerId = customerId, TxnDate = DateTime.Now,
                DocType = tahsilat ? CariDocType.Tahsilat : CariDocType.Odeme,
                DocRefType = "Kasa", Description = $"Kasa {(tahsilat ? "tahsilat" : "ödeme")}" + (note is null ? "" : $" · {note}"),
                CurrencyId = cur, Debt = tahsilat ? 0 : amount, Credit = tahsilat ? amount : 0
            }, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Kasalar arası transfer (vardiya/ana kasa). Aynı döviz olmalı.</summary>
    public void Transfer(int fromCashId, int toCashId, decimal amount, string? note, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try { Transfer(c, tx, fromCashId, toCashId, amount, note, userId); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Mevcut bağlantı+transaction altında kasalar arası transfer (atomik akışlar için, ör. Z devri).</summary>
    public void Transfer(SqlConnection c, SqlTransaction tx, int fromCashId, int toCashId, decimal amount, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (fromCashId == toCashId) throw new ArgumentException("Kaynak ve hedef kasa aynı olamaz.");
        var f = KasaInfo(c, tx, fromCashId);
        var t = KasaInfo(c, tx, toCashId);
        if (f.Currency != t.Currency) throw new InvalidOperationException("Transfer için kasaların dövizi aynı olmalı.");
        Post(c, tx, f.Branch, fromCashId, f.Currency, CashMovementType.TransferCikis, amount, null, toCashId, note, userId);
        Post(c, tx, t.Branch, toCashId, t.Currency, CashMovementType.TransferGiris, amount, null, fromCashId, note, userId);
    }

    public List<CashBalance> GetBalances(int branchId)
    {
        var list = new List<CashBalance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT k.Id, k.Code, k.Name, k.CurrencyId, cur.Code,
       (SELECT ISNULL(SUM(m.Amount),0) FROM dbo.CashMovements m WHERE m.CashRegisterId=k.Id)
FROM dbo.CashRegisters k
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id=k.CurrencyId
WHERE k.BranchId=@b AND k.IsDeleted=0 ORDER BY k.Code;";
        cmd.Parameters.AddWithValue("@b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CashBalance
            {
                CashRegisterId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                CurrencyId = r.GetInt32(3), CurrencyCode = r.IsDBNull(4) ? null : r.GetString(4),
                Balance = r.GetDecimal(5)
            });
        return list;
    }

    public List<CashMovement> GetMovements(int cashRegisterId, int top = 100)
    {
        var list = new List<CashMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT TOP {top} Id, BranchId, CashRegisterId, CurrencyId, Type, Amount, RefClFicheId, CounterCashRegisterId, Note, MoveDate
FROM dbo.CashMovements WHERE CashRegisterId=@k ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@k", cashRegisterId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CashMovement
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), CashRegisterId = r.GetInt32(2), CurrencyId = r.GetInt32(3),
                Type = (CashMovementType)r.GetInt32(4), Amount = r.GetDecimal(5),
                RefClFicheId = r.IsDBNull(6) ? null : r.GetInt32(6), CounterCashRegisterId = r.IsDBNull(7) ? null : r.GetInt32(7),
                Note = r.IsDBNull(8) ? null : r.GetString(8), MoveDate = r.GetDateTime(9)
            });
        return list;
    }
}
