using System.Linq;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Yemek çek hesabı hareketleri: adisyon tahsilatı (defter), manuel giriş/çıkış, bakiye.</summary>
public class MealVoucherMovementService
{
    /// <summary>Transaction içinde hareket ekler (ör. ödeme sırasında). amount pozitif verilir; işaret tipe göre.</summary>
    internal static void Post(SqlConnection c, SqlTransaction tx, int branchId, int accountId, int currencyId,
        MealVoucherMovementType type, decimal amount, int? refClFicheId, string? note, int? userId)
    {
        decimal signed = type is MealVoucherMovementType.Iade or MealVoucherMovementType.Cikis ? -Math.Abs(amount) : Math.Abs(amount);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.MealVoucherMovements (BranchId, MealVoucherAccountId, CurrencyId, Type, Amount, RefClFicheId, Note, MoveDate, CreatedBy)
VALUES (@b,@acc,@cur,@t,@a,@ref,@note,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@acc", accountId);
        cmd.Parameters.AddWithValue("@cur", currencyId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@a", signed);
        cmd.Parameters.AddWithValue("@ref", (object?)refClFicheId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static (int Branch, int Currency) AccInfo(SqlConnection c, int accountId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BranchId, CurrencyId FROM dbo.MealVoucherAccounts WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", accountId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Yemek çek hesabı bulunamadı.");
        return (r.GetInt32(0), r.GetInt32(1));
    }

    public void AddManual(int accountId, MealVoucherMovementType type, decimal amount, string? note, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (type is not (MealVoucherMovementType.Giris or MealVoucherMovementType.Cikis)) throw new ArgumentException("Yalnızca giriş/çıkış.");
        using var c = Database.OpenCompany();
        var (branch, cur) = AccInfo(c, accountId);
        using var tx = c.BeginTransaction();
        try { Post(c, tx, branch, accountId, cur, type, amount, null, note, userId); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    public List<(int AccountId, string Name, decimal Balance)> GetBalances(int branchId)
    {
        var list = new List<(int, string, decimal)>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT a.Id, a.Name, (SELECT ISNULL(SUM(m.Amount),0) FROM dbo.MealVoucherMovements m WHERE m.MealVoucherAccountId=a.Id)
FROM dbo.MealVoucherAccounts a WHERE a.BranchId=@b AND a.IsDeleted=0 ORDER BY a.Code;";
        cmd.Parameters.AddWithValue("@b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetString(1), r.GetDecimal(2)));
        return list;
    }

    /// <summary>Defter bakiyeleri (XAML/rapor için isimli model). GetBalances tuple'ını sarmalar.</summary>
    public List<MealVoucherBalance> GetBalanceList(int branchId) =>
        GetBalances(branchId).Select(t => new MealVoucherBalance { AccountId = t.AccountId, Name = t.Name, Balance = t.Balance }).ToList();

    public List<MealVoucherMovement> GetMovements(int accountId, int top = 200)
    {
        var list = new List<MealVoucherMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@n) Id, BranchId, MealVoucherAccountId, CurrencyId, Type, Amount, RefClFicheId, Note, MoveDate
FROM dbo.MealVoucherMovements WHERE MealVoucherAccountId=@a ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@n", top);
        cmd.Parameters.AddWithValue("@a", accountId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MealVoucherMovement
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), MealVoucherAccountId = r.GetInt32(2), CurrencyId = r.GetInt32(3),
                Type = (MealVoucherMovementType)r.GetInt32(4), Amount = r.GetDecimal(5),
                RefClFicheId = r.IsDBNull(6) ? null : r.GetInt32(6), Note = r.IsDBNull(7) ? null : r.GetString(7), MoveDate = r.GetDateTime(8)
            });
        return list;
    }
}
