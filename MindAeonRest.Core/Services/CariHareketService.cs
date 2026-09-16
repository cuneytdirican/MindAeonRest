using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Cari hesap hareketleri (defter): ekstre, bakiye, manuel/açılış girişi.
/// Faturalar ve ödeme/tahsilatlar kendi transaction'ları içinde PostTx ile yazar.</summary>
public class CariHareketService
{
    /// <summary>Fatura/ödeme transaction'ı içinde cari hareket ekler (aynı bağlantı/tx).</summary>
    public static int PostTx(SqlConnection c, SqlTransaction tx, CariHareket h, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.CariHareketler (BranchId, CustomerId, TxnDate, DocType, DocRefType, DocRefId, DocNo, Description, CurrencyId, Debt, Credit, ProjectId, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Br,@Cust,@Date,@Type,@RefT,@RefId,@No,@Desc,@Cur,@Debt,@Credit,@Prj,@By);";
        Bind(cmd, h, userId);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Manuel/açılış cari hareketi (kendi bağlantısı).</summary>
    public int AddManual(CariHareket h, int? userId)
    {
        if (h.CustomerId == 0) throw new ArgumentException("Cari seçiniz.");
        if (h.Debt < 0 || h.Credit < 0) throw new ArgumentException("Borç/Alacak negatif olamaz.");
        if (h.Debt == 0 && h.Credit == 0) throw new ArgumentException("Borç veya Alacak giriniz.");
        if (h.TxnDate == default) h.TxnDate = DateTime.Now;
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.CariHareketler (BranchId, CustomerId, TxnDate, DocType, DocRefType, DocRefId, DocNo, Description, CurrencyId, Debt, Credit, ProjectId, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@Br,@Cust,@Date,@Type,@RefT,@RefId,@No,@Desc,@Cur,@Debt,@Credit,@Prj,@By);";
        Bind(cmd, h, userId);
        return (int)cmd.ExecuteScalar()!;
    }

    private static void Bind(SqlCommand cmd, CariHareket h, int? userId)
    {
        cmd.Parameters.AddWithValue("@Br", h.BranchId);
        cmd.Parameters.AddWithValue("@Cust", h.CustomerId);
        cmd.Parameters.AddWithValue("@Date", h.TxnDate);
        cmd.Parameters.AddWithValue("@Type", (int)h.DocType);
        cmd.Parameters.AddWithValue("@RefT", (object?)h.DocRefType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RefId", (object?)h.DocRefId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@No", (object?)h.DocNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Desc", (object?)h.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Cur", h.CurrencyId);
        cmd.Parameters.AddWithValue("@Debt", h.Debt);
        cmd.Parameters.AddWithValue("@Credit", h.Credit);
        cmd.Parameters.AddWithValue("@Prj", (object?)h.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
    }

    /// <summary>Cari ekstre: devir (from öncesi bakiye) + dönem hareketleri yürüyen bakiye ile.</summary>
    public (decimal Opening, List<CariHareket> Rows) GetExtract(int customerId, DateTime from, DateTime to)
    {
        decimal opening;
        var rows = new List<CariHareket>();
        DateTime toExclusive = to.Date.AddDays(1);
        using var c = Database.OpenCompany();

        // Devir: döviz bazında (DEVİR satırında toplam gösterilir; yürüyen bakiye döviz bazında ilerler)
        var openingByCur = new Dictionary<int, decimal>();
        using (var op = c.CreateCommand())
        {
            op.CommandText = "SELECT CurrencyId, ISNULL(SUM(Debt-Credit),0) FROM dbo.CariHareketler WHERE CustomerId=@c AND TxnDate < @from GROUP BY CurrencyId;";
            op.Parameters.AddWithValue("@c", customerId);
            op.Parameters.AddWithValue("@from", from.Date);
            using var orr = op.ExecuteReader();
            while (orr.Read()) openingByCur[orr.GetInt32(0)] = orr.GetDecimal(1);
        }
        opening = openingByCur.Values.Sum();
        var running = new Dictionary<int, decimal>(openingByCur);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT h.Id, h.TxnDate, h.DocType, h.DocNo, h.Description, h.Debt, h.Credit, p.Code AS PrjCode, h.CurrencyId, cur.Code AS CurCode
FROM dbo.CariHareketler h
LEFT JOIN dbo.Projects p ON p.Id = h.ProjectId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = h.CurrencyId
WHERE h.CustomerId=@c AND h.TxnDate >= @from AND h.TxnDate < @to
ORDER BY h.TxnDate, h.Id;";
            cmd.Parameters.AddWithValue("@c", customerId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", toExclusive);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var h = new CariHareket
                {
                    Id = r.GetInt32(0), TxnDate = r.GetDateTime(1), DocType = (CariDocType)r.GetInt32(2),
                    DocNo = r.IsDBNull(3) ? null : r.GetString(3),
                    Description = r.IsDBNull(4) ? null : r.GetString(4),
                    Debt = r.GetDecimal(5), Credit = r.GetDecimal(6),
                    ProjectCode = r.IsDBNull(7) ? null : r.GetString(7),
                    CurrencyId = r.GetInt32(8),
                    CurrencyCode = r.IsDBNull(9) ? null : r.GetString(9)
                };
                decimal run = (running.TryGetValue(h.CurrencyId, out var v) ? v : 0) + h.Net;
                running[h.CurrencyId] = run;
                h.RunningBalance = run;
                rows.Add(h);
            }
        }
        return (opening, rows);
    }

    /// <summary>Tüm carilerin bakiye listesi (opsiyonel cari tipi/tarih filtresi).</summary>
    public List<CariBalance> GetBalances(DateTime? from = null, DateTime? to = null)
    {
        var list = new List<CariBalance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT c.Id, c.Code, c.Name, c.CustomerType, h.CurrencyId, cur.Code AS CurCode,
       SUM(h.Debt), SUM(h.Credit)
FROM dbo.Customers c
JOIN dbo.CariHareketler h ON h.CustomerId = c.Id
    AND (@from IS NULL OR h.TxnDate >= @from) AND (@to IS NULL OR h.TxnDate < @to)
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = h.CurrencyId
WHERE c.IsDeleted = 0
GROUP BY c.Id, c.Code, c.Name, c.CustomerType, h.CurrencyId, cur.Code
HAVING SUM(h.Debt) <> 0 OR SUM(h.Credit) <> 0
ORDER BY c.Code, h.CurrencyId;";
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CariBalance
            {
                CustomerId = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
                TypeText = ((CustomerType)r.GetInt32(3)).ToTr(),
                CurrencyId = r.GetInt32(4), CurrencyCode = r.IsDBNull(5) ? null : r.GetString(5),
                Debt = r.GetDecimal(6), Credit = r.GetDecimal(7)
            });
        return list;
    }

    /// <summary>Son cari hareketler (liste ekranı).</summary>
    public List<CariHareket> GetRecent(int take = 200)
    {
        var list = new List<CariHareket>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@n) h.Id, h.TxnDate, h.DocType, h.DocNo, h.Description, h.Debt, h.Credit, cu.Name, p.Code
FROM dbo.CariHareketler h
JOIN dbo.Customers cu ON cu.Id = h.CustomerId
LEFT JOIN dbo.Projects p ON p.Id = h.ProjectId
ORDER BY h.TxnDate DESC, h.Id DESC;";
        cmd.Parameters.AddWithValue("@n", take);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CariHareket
            {
                Id = r.GetInt32(0), TxnDate = r.GetDateTime(1), DocType = (CariDocType)r.GetInt32(2),
                DocNo = r.IsDBNull(3) ? null : r.GetString(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4),
                Debt = r.GetDecimal(5), Credit = r.GetDecimal(6),
                CustomerName = r.GetString(7),
                ProjectCode = r.IsDBNull(8) ? null : r.GetString(8)
            });
        return list;
    }
}
