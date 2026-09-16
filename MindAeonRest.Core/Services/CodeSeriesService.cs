using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Otomatik kod serileri (aktif şirket DB). Stok/Müşteri + evrak tipleri için sıradaki kodu üretir.
/// Evrak serileri tip + şube bazlı parametriktir; şube-özel yoksa varsayılana (BranchId NULL) düşer.</summary>
public class CodeSeriesService
{
    private static readonly string Cols = "Id, EntityType, BranchId, Prefix, Padding, NextNumber, IsActive, IsDeleted";

    public List<CodeSeries> GetAll(bool includeDeleted = false)
    {
        var list = new List<CodeSeries>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT s.Id, s.EntityType, s.BranchId, s.Prefix, s.Padding, s.NextNumber, s.IsActive, s.IsDeleted, b.Name AS BranchName
FROM dbo.CodeSeries s
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = s.BranchId
WHERE (@inc=1 OR s.IsDeleted=0) ORDER BY s.EntityType, s.BranchId;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) { var s = Map(reader); s.BranchName = reader.IsDBNull(8) ? null : reader.GetString(8); list.Add(s); }
        return list;
    }

    /// <summary>Tek kayıt (Id ile) — back-office düzenleme formu için. Şube adı gösterim amaçlı doldurulur.</summary>
    public CodeSeries? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT s.Id, s.EntityType, s.BranchId, s.Prefix, s.Padding, s.NextNumber, s.IsActive, s.IsDeleted, b.Name AS BranchName
FROM dbo.CodeSeries s
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = s.BranchId
WHERE s.Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var s = Map(reader);
        s.BranchName = reader.IsDBNull(8) ? null : reader.GetString(8);
        return s;
    }

    public CodeSeries? Get(CodeSeriesType type, int? branchId = null)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Cols} FROM dbo.CodeSeries WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0;";
        command.Parameters.AddWithValue("@t", (int)type);
        command.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Stok/müşteri kartı için sıradaki kod (varsayılan seri).</summary>
    public string? PeekNext(CodeSeriesType type)
    {
        var s = Get(type);
        return s is { IsActive: true } ? s.Format(s.NextNumber) : null;
    }

    public void Advance(CodeSeriesType type)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.CodeSeries SET NextNumber = NextNumber + 1, UpdatedAt = SYSUTCDATETIME() WHERE EntityType=@t AND BranchId IS NULL AND IsDeleted=0 AND IsActive=1;";
        command.Parameters.AddWithValue("@t", (int)type);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Evrak için ATOMİK sıradaki FişNo. Şube-özel seri varsa onu, yoksa varsayılanı (BranchId NULL) kullanır;
    /// hiç yoksa varsayılan seriyi otomatik oluşturur. Belge transaction'ı içinde çağrılır.
    /// </summary>
    public static string NextDocCode(SqlConnection c, SqlTransaction tx, CodeSeriesType type, int? branchId, int? userId)
    {
        // 1) şube-özel, 2) varsayılan; UPDLOCK ile kilitle
        int? useBranch = SeriesExists(c, tx, type, branchId) ? branchId
                       : SeriesExists(c, tx, type, null) ? (int?)null
                       : EnsureDefault(c, tx, type, userId);

        using var sel = c.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = "SELECT Prefix, Padding, NextNumber FROM dbo.CodeSeries WITH (UPDLOCK, HOLDLOCK) WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0;";
        sel.Parameters.AddWithValue("@t", (int)type);
        sel.Parameters.AddWithValue("@b", (object?)useBranch ?? DBNull.Value);
        string? prefix; int padding; int number;
        using (var r = sel.ExecuteReader())
        {
            if (!r.Read()) throw new InvalidOperationException("Seri bulunamadı.");
            prefix = r.IsDBNull(0) ? null : r.GetString(0);
            padding = r.GetInt32(1);
            number = r.GetInt32(2);
        }
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.CodeSeries SET NextNumber=NextNumber+1, UpdatedAt=SYSUTCDATETIME() WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0;";
            upd.Parameters.AddWithValue("@t", (int)type);
            upd.Parameters.AddWithValue("@b", (object?)useBranch ?? DBNull.Value);
            upd.ExecuteNonQuery();
        }
        return $"{prefix}{number.ToString().PadLeft(Math.Max(1, padding), '0')}";
    }

    private static bool SeriesExists(SqlConnection c, SqlTransaction tx, CodeSeriesType type, int? branchId)
    {
        if (branchId is null && type is CodeSeriesType.Product or CodeSeriesType.Customer) { /* allow */ }
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.CodeSeries WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0 AND IsActive=1;";
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    private static int? EnsureDefault(SqlConnection c, SqlTransaction tx, CodeSeriesType type, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.CodeSeries (EntityType, BranchId, Prefix, Padding, NextNumber, IsActive, CreatedBy)
VALUES (@t, NULL, @p, 6, 1, 1, @by);";
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@p", type.DefaultPrefix());
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return null;   // varsayılan (BranchId NULL)
    }

    public void Save(CodeSeries s, int? userId)
    {
        if (s.Padding < 1) s.Padding = 1;
        if (s.NextNumber < 1) s.NextNumber = 1;

        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.CodeSeries WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0)
    UPDATE dbo.CodeSeries SET Prefix=@p, Padding=@pad, NextNumber=@n, IsActive=@a, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
        WHERE EntityType=@t AND ISNULL(BranchId,-1)=ISNULL(@b,-1) AND IsDeleted=0;
ELSE
    INSERT INTO dbo.CodeSeries (EntityType, BranchId, Prefix, Padding, NextNumber, IsActive, CreatedBy)
        VALUES (@t, @b, @p, @pad, @n, @a, @by);";
        command.Parameters.AddWithValue("@t", (int)s.EntityType);
        command.Parameters.AddWithValue("@b", (object?)s.BranchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@p", (object?)s.Prefix ?? DBNull.Value);
        command.Parameters.AddWithValue("@pad", s.Padding);
        command.Parameters.AddWithValue("@n", s.NextNumber);
        command.Parameters.AddWithValue("@a", s.IsActive);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static CodeSeries Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        EntityType = (CodeSeriesType)r.GetInt32(1),
        BranchId = r.IsDBNull(2) ? null : r.GetInt32(2),
        Prefix = r.IsDBNull(3) ? null : r.GetString(3),
        Padding = r.GetInt32(4),
        NextNumber = r.GetInt32(5),
        IsActive = r.GetBoolean(6),
        IsDeleted = r.GetBoolean(7)
    };
}
