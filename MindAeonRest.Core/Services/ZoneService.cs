using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Lokasyonlar (Zone) - şube altında (şirket DB). Şube adı sistem DB'sinden cross-db.</summary>
public class ZoneService
{
    private static string SelectBase => $@"
SELECT z.Id, z.Code, z.Name, z.BranchId, b.Name AS BranchName, z.ReceiptPrinterName, z.IsActive, z.IsDeleted
FROM dbo.Zones z
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = z.BranchId";

    public List<Zone> GetAll(bool includeDeleted = false)
    {
        var list = new List<Zone>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR z.IsDeleted=0) ORDER BY z.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>Tek kayıt (Id ile) — back-office düzenleme formu için. Şube adı gösterim amaçlı doldurulur.</summary>
    public Zone? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE z.Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Zone Save(Zone z, int? userId)
    {
        string code = CodeHelper.Normalize(z.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir lokasyon kodu giriniz.");
        if (string.IsNullOrWhiteSpace(z.Name)) throw new ArgumentException("Lokasyon adı boş olamaz.");
        if (z.BranchId == 0) throw new ArgumentException("Şube seçiniz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Zones WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", z.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu lokasyon zaten var.");
        }

        using var command = connection.CreateCommand();
        if (z.Id == 0)
            command.CommandText = "INSERT INTO dbo.Zones (Code, Name, BranchId, ReceiptPrinterName, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Branch,@Printer,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.Zones SET Code=@Code, Name=@Name, BranchId=@Branch, ReceiptPrinterName=@Printer, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", z.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", z.Name.Trim());
        command.Parameters.AddWithValue("@Branch", z.BranchId);
        CompanyService.AddNullable(command, "@Printer", z.ReceiptPrinterName);
        command.Parameters.AddWithValue("@Active", z.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (z.Id == 0) z.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        z.Code = code;
        return z;
    }

    private static Zone Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        BranchId = r.GetInt32(3),
        BranchName = r.IsDBNull(4) ? null : r.GetString(4),
        ReceiptPrinterName = r.IsDBNull(5) ? null : r.GetString(5),
        IsActive = r.GetBoolean(6),
        IsDeleted = r.GetBoolean(7)
    };
}
