using Microsoft.Data.SqlClient;

namespace MindAeonRest.Services;

/// <summary>
/// Şube bazlı, eşzamanlılığa dayanıklı belge numarası üretici.
/// Aynı transaction içinde atomik artış (UPDLOCK/HOLDLOCK) → yarış olmadan sıralı numara.
/// </summary>
public static class DocCounter
{
    public static int Next(SqlConnection connection, SqlTransaction tx, int branchId, string docType)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.DocumentCounters WITH (UPDLOCK, HOLDLOCK) WHERE BranchId=@b AND DocType=@t)
    INSERT INTO dbo.DocumentCounters (BranchId, DocType, NextNumber) VALUES (@b, @t, 0);
UPDATE dbo.DocumentCounters SET NextNumber = NextNumber + 1
OUTPUT INSERTED.NextNumber
WHERE BranchId=@b AND DocType=@t;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@t", docType);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Şube bazlı sıralı, global tekil kod: {prefix}{branchId}-{n} (ör. ADS2-15).</summary>
    public static string NextCode(SqlConnection connection, SqlTransaction tx, int branchId, string prefix)
        => $"{prefix}{branchId}-{Next(connection, tx, branchId, prefix)}";
}
