using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Masalar ve yerleşim planı koordinatları (şirket DB). Kod ŞUBE BAZLI unique + artan.</summary>
public class TableService
{
    public List<Table> GetByZone(int zoneId, bool includeDeleted = false)
    {
        var list = new List<Table>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, ZoneId, BranchId, Code, Name, Shape, Seats, X, Y, Width, Height, IsActive
FROM dbo.Tables WHERE ZoneId=@z AND (@inc=1 OR IsDeleted=0) ORDER BY Id;";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new Table
            {
                Id = reader.GetInt32(0),
                ZoneId = reader.GetInt32(1),
                BranchId = reader.GetInt32(2),
                Code = reader.GetString(3),
                Name = reader.IsDBNull(4) ? null : reader.GetString(4),
                Shape = (TableShape)reader.GetInt32(5),
                Seats = reader.GetInt32(6),
                X = reader.GetDouble(7),
                Y = reader.GetDouble(8),
                Width = reader.GetDouble(9),
                Height = reader.GetDouble(10),
                IsActive = reader.GetBoolean(11)
            });
        return list;
    }

    /// <summary>Tek masa (Id ile). Public/QR akışında masanın zone+şubesini çözmek için (aktif+silinmemiş).</summary>
    public Table? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, ZoneId, BranchId, Code, Name, Shape, Seats, X, Y, Width, Height, IsActive
FROM dbo.Tables WHERE Id=@id AND IsDeleted=0;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Table
        {
            Id = reader.GetInt32(0),
            ZoneId = reader.GetInt32(1),
            BranchId = reader.GetInt32(2),
            Code = reader.GetString(3),
            Name = reader.IsDBNull(4) ? null : reader.GetString(4),
            Shape = (TableShape)reader.GetInt32(5),
            Seats = reader.GetInt32(6),
            X = reader.GetDouble(7),
            Y = reader.GetDouble(8),
            Width = reader.GetDouble(9),
            Height = reader.GetDouble(10),
            IsActive = reader.GetBoolean(11)
        };
    }

    /// <summary>Zone'un yerleşim planını topluca kaydeder. Kod şube bazlı tekil olmalı.</summary>
    public void SaveLayout(int zoneId, int branchId, IEnumerable<Table> tables, IEnumerable<int> deletedIds, int? userId)
    {
        var list = tables.ToList();
        foreach (var t in list)
        {
            t.Code = CodeHelper.Normalize(t.Code);
            if (t.Code.Length == 0) throw new ArgumentException("Her masanın bir kodu olmalı.");
        }
        if (list.Select(t => t.Code).Distinct().Count() != list.Count)
            throw new InvalidOperationException("Plan içinde tekrar eden masa kodu var.");

        using var connection = Database.OpenCompany();

        foreach (int delId in deletedIds.Distinct())
        {
            using var del = connection.CreateCommand();
            del.CommandText = "UPDATE dbo.Tables SET IsDeleted=1, DeletedBy=@by, DeletedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            del.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@id", delId);
            del.ExecuteNonQuery();
        }

        foreach (var t in list)
        {
            using (var check = connection.CreateCommand())
            {
                // şube bazlı tekillik
                check.CommandText = "SELECT COUNT(1) FROM dbo.Tables WHERE BranchId=@b AND Code=@c AND Id<>@id;";
                check.Parameters.AddWithValue("@b", branchId);
                check.Parameters.AddWithValue("@c", t.Code);
                check.Parameters.AddWithValue("@id", t.Id);
                if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{t.Code}' kodlu masa bu şubede zaten var.");
            }

            using var command = connection.CreateCommand();
            if (t.Id == 0)
                command.CommandText = @"
INSERT INTO dbo.Tables (ZoneId, BranchId, Code, Name, Shape, Seats, X, Y, Width, Height, IsActive, CreatedBy)
VALUES (@z,@b,@code,@name,@shape,@seats,@x,@y,@w,@h,@active,@by);";
            else
            {
                command.CommandText = @"
UPDATE dbo.Tables SET ZoneId=@z, BranchId=@b, Code=@code, Name=@name, Shape=@shape, Seats=@seats, X=@x, Y=@y, Width=@w, Height=@h, IsActive=@active,
    UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                command.Parameters.AddWithValue("@id", t.Id);
            }
            command.Parameters.AddWithValue("@z", zoneId);
            command.Parameters.AddWithValue("@b", branchId);
            command.Parameters.AddWithValue("@code", t.Code);
            CompanyService.AddNullable(command, "@name", t.Name);
            command.Parameters.AddWithValue("@shape", (int)t.Shape);
            command.Parameters.AddWithValue("@seats", t.Seats);
            command.Parameters.AddWithValue("@x", t.X);
            command.Parameters.AddWithValue("@y", t.Y);
            command.Parameters.AddWithValue("@w", t.Width);
            command.Parameters.AddWithValue("@h", t.Height);
            command.Parameters.AddWithValue("@active", t.IsActive);
            command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Şube genelinde sıradaki MASA{n} numarası (MASA ile başlayan kodların en büyüğü + 1).</summary>
    public int NextTableNumberForBranch(int branchId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(Code, 5, 20))), 0)
FROM dbo.Tables
WHERE BranchId=@b AND IsDeleted=0 AND Code LIKE 'MASA%' AND TRY_CONVERT(INT, SUBSTRING(Code, 5, 20)) IS NOT NULL;";
        command.Parameters.AddWithValue("@b", branchId);
        return (int)command.ExecuteScalar()! + 1;
    }
}
