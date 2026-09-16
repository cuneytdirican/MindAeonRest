using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Depolar (şirket DB). Şube adı sistem DB'sinden cross-db join ile.</summary>
public class WarehouseService
{
    private static string SelectBase => $@"
SELECT w.Id, w.Code, w.Name, w.BranchId, w.IsActive, b.Name AS BranchName, w.IsDeleted
FROM dbo.Warehouses w
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = w.BranchId";

    public List<Warehouse> GetAll(bool includeDeleted = false)
    {
        var list = new List<Warehouse>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc = 1 OR w.IsDeleted = 0) ORDER BY w.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Warehouse Save(Warehouse w, int? userId)
    {
        string code = CodeHelper.Normalize(w.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir depo kodu giriniz.");
        if (string.IsNullOrWhiteSpace(w.Name)) throw new ArgumentException("Depo adı boş olamaz.");
        if (w.BranchId == 0) throw new ArgumentException("Şube seçiniz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Warehouses WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", w.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu depo zaten var (kod firma genelinde tekildir).");
        }

        using var command = connection.CreateCommand();
        if (w.Id == 0)
        {
            command.CommandText = @"
INSERT INTO dbo.Warehouses (Code, Name, BranchId, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code, @Name, @Branch, @Active, @By);";
        }
        else
        {
            command.CommandText = @"
UPDATE dbo.Warehouses SET Code=@Code, Name=@Name, BranchId=@Branch, IsActive=@Active,
    UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", w.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", w.Name.Trim());
        command.Parameters.AddWithValue("@Branch", w.BranchId);
        command.Parameters.AddWithValue("@Active", w.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (w.Id == 0) w.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        w.Code = code;
        return w;
    }

    private static Warehouse Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        BranchId = r.GetInt32(3),
        IsActive = r.GetBoolean(4),
        BranchName = r.IsDBNull(5) ? null : r.GetString(5),
        IsDeleted = r.GetBoolean(6)
    };
}
