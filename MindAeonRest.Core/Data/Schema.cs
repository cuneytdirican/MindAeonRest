using Microsoft.Data.SqlClient;
using MindAeonRest.Helpers;

namespace MindAeonRest.Data;

/// <summary>
/// Şema (DDL) yardımcıları: veritabanı/kolon "yoksa oluştur" işlemleri.
/// Tüm tablolarda standart audit kolonları kullanılır.
/// </summary>
public static class Schema
{
    /// <summary>CREATE TABLE içine eklenecek standart audit kolonları (en sona, virgülle).</summary>
    public const string AuditColumns = @"
    CreatedBy INT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT(SYSUTCDATETIME()),
    UpdatedBy INT NULL,
    UpdatedAt DATETIME2 NULL,
    IsDeleted BIT NOT NULL DEFAULT(0),
    DeletedBy INT NULL,
    DeletedAt DATETIME2 NULL";

    public static void Exec(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Master üzerinden veritabanını yoksa oluşturur.</summary>
    public static void EnsureDatabase(string databaseName)
    {
        using var connection = new SqlConnection(AppConfig.MasterConnectionString);
        connection.Open();
        Exec(connection, $"IF DB_ID(N'{databaseName.Replace("'", "''")}') IS NULL CREATE DATABASE [{databaseName}];");
    }

    public static bool DatabaseExists(string databaseName)
    {
        using var connection = new SqlConnection(AppConfig.MasterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.AddWithValue("@db", databaseName);
        return (int)command.ExecuteScalar() == 1;
    }

    /// <summary>Tabloda kolon yoksa ekler (audit kolonlarını geriye dönük eklemek için).</summary>
    public static void EnsureColumn(SqlConnection connection, string table, string column, string definition)
    {
        Exec(connection,
            $"IF COL_LENGTH('dbo.{table}', '{column}') IS NULL ALTER TABLE dbo.{table} ADD {column} {definition};");
    }

    /// <summary>Mevcut bir tabloya tüm audit kolonlarını yoksa ekler.</summary>
    public static void EnsureAuditColumns(SqlConnection connection, string table)
    {
        EnsureColumn(connection, table, "CreatedBy", "INT NULL");
        EnsureColumn(connection, table, "CreatedAt", "DATETIME2 NOT NULL CONSTRAINT DF_" + table + "_CreatedAt DEFAULT(SYSUTCDATETIME())");
        EnsureColumn(connection, table, "UpdatedBy", "INT NULL");
        EnsureColumn(connection, table, "UpdatedAt", "DATETIME2 NULL");
        EnsureColumn(connection, table, "IsDeleted", "BIT NOT NULL CONSTRAINT DF_" + table + "_IsDeleted DEFAULT(0)");
        EnsureColumn(connection, table, "DeletedBy", "INT NULL");
        EnsureColumn(connection, table, "DeletedAt", "DATETIME2 NULL");
    }

    /// <summary>Bir kaydı soft-delete eder / geri alır (IsDeleted + DeletedBy/DeletedAt). Yalnızca SysAdmin çağırmalı.</summary>
    public static void SetDeleted(SqlConnection connection, string table, int id, bool deleted, int? userId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = deleted
            ? $"UPDATE dbo.{table} SET IsDeleted=1, DeletedBy=@by, DeletedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;"
            : $"UPDATE dbo.{table} SET IsDeleted=0, DeletedBy=NULL, DeletedAt=NULL, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>IsDeleted kolonu olan tüm tablolara DeletedBy/DeletedAt kolonlarını ekler (tek seferde, idempotent).</summary>
    public static void EnsureDeleteAuditOnAllTables(SqlConnection connection)
    {
        Exec(connection, @"
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql +
    'IF COL_LENGTH(''dbo.' + t.name + ''',''DeletedBy'') IS NULL ALTER TABLE dbo.' + QUOTENAME(t.name) + ' ADD DeletedBy INT NULL;' +
    'IF COL_LENGTH(''dbo.' + t.name + ''',''DeletedAt'') IS NULL ALTER TABLE dbo.' + QUOTENAME(t.name) + ' ADD DeletedAt DATETIME2 NULL;'
FROM sys.tables t
WHERE EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = 'IsDeleted');
IF LEN(@sql) > 0 EXEC sys.sp_executesql @sql;");
    }
}
