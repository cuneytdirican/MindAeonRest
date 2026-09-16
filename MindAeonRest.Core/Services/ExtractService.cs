using System;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Hesap ekstreleri (Nakit Kasa / POS / Banka / Yemek Çeki) — açılış + yürüyen bakiye.</summary>
public class ExtractService
{
    // UTC saklanan tarihi yerele çevirir (gün filtresi + gösterim ortak)
    private static string Local(string col) => $"DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), {col})";

    public ExtractResult GetCashExtract(int id, DateTime from, DateTime to)
        => BuildSigned("dbo.CashMovements", "CashRegisterId", "MoveDate", "Type", "Amount", "Note", id, from, to, t => ((CashMovementType)t).ToTr());

    public ExtractResult GetPosExtract(int id, DateTime from, DateTime to)
        => BuildSigned("dbo.PosMovements", "PosId", "MoveDate", "Type", "Amount", "Note", id, from, to, t => ((BankMovementType)t).ToTr());

    public ExtractResult GetBankExtract(int id, DateTime from, DateTime to)
        => BuildSigned("dbo.BankMovements", "BankAccountId", "MoveDate", "Type", "Amount", "Note", id, from, to, t => ((BankMovementType)t).ToTr());

    /// <summary>Yemek çeki ekstresi: hareket tablosu yok, fişlerdeki yemek çeki tahsilatlarından (PaymentLines Kind=YemekCeki) üretilir.</summary>
    public ExtractResult GetVoucherExtract(int accountId, DateTime from, DateTime to)
    {
        var res = new ExtractResult();
        string pl = Local("p.PayDate");
        using var c = Database.OpenCompany();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT CAST(ISNULL(SUM(p.Amount),0) AS DECIMAL(18,2)) FROM dbo.PaymentLines p WHERE p.Kind=2 AND p.InstrumentId=@id AND CAST({pl} AS DATE) < @from;";
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            res.Opening = Convert.ToDecimal(cmd.ExecuteScalar());
        }
        decimal run = res.Opening;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT {pl} AS LD, p.Amount, f.Code
FROM dbo.PaymentLines p
LEFT JOIN dbo.ClFiches f ON f.Id = p.ClFicheId
WHERE p.Kind=2 AND p.InstrumentId=@id AND CAST({pl} AS DATE) BETWEEN @from AND @to
ORDER BY p.PayDate ASC, p.Id ASC;";
            cmd.Parameters.AddWithValue("@id", accountId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal amt = r.GetDecimal(1);
                run += amt;
                res.Rows.Add(new ExtractRow
                {
                    Date = r.GetDateTime(0),
                    TypeText = "Yemek Çeki Tahsilat",
                    Note = r.IsDBNull(2) ? "" : $"Fiş {r.GetString(2)}",
                    In = amt > 0 ? amt : 0,
                    Out = amt < 0 ? -amt : 0,
                    Running = run
                });
            }
        }
        res.TotalIn = res.Rows.Sum(x => x.In);
        res.TotalOut = res.Rows.Sum(x => x.Out);
        res.Closing = run;
        return res;
    }

    private ExtractResult BuildSigned(string table, string accCol, string dateCol, string typeCol, string amtCol, string noteCol,
        int id, DateTime from, DateTime to, Func<int, string> typeName)
    {
        var res = new ExtractResult();
        string local = Local(dateCol);
        using var c = Database.OpenCompany();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT CAST(ISNULL(SUM({amtCol}),0) AS DECIMAL(18,2)) FROM {table} WHERE {accCol}=@id AND CAST({local} AS DATE) < @from;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@from", from.Date);
            res.Opening = Convert.ToDecimal(cmd.ExecuteScalar());
        }
        decimal run = res.Opening;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT {local} AS LD, {typeCol} AS Typ, {amtCol} AS Amt, {noteCol} AS Nt
FROM {table}
WHERE {accCol}=@id AND CAST({local} AS DATE) BETWEEN @from AND @to
ORDER BY {dateCol} ASC, Id ASC;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal amt = r.GetDecimal(2);
                run += amt;
                res.Rows.Add(new ExtractRow
                {
                    Date = r.GetDateTime(0),
                    TypeText = typeName(r.GetInt32(1)),
                    Note = r.IsDBNull(3) ? "" : r.GetString(3),
                    In = amt > 0 ? amt : 0,
                    Out = amt < 0 ? -amt : 0,
                    Running = run
                });
            }
        }
        res.TotalIn = res.Rows.Sum(x => x.In);
        res.TotalOut = res.Rows.Sum(x => x.Out);
        res.Closing = run;
        return res;
    }
}
