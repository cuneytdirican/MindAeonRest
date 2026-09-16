using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Reyonlar (Mutfak/hazırlık birimi) - zone altında (şirket DB).</summary>
public class ReyonService
{
    private const string SelectBase = @"
SELECT r.Id, r.Code, r.Name, r.ZoneId, z.Name AS ZoneName, r.PrinterName, r.IsActive, r.IsDeleted, r.StationType
FROM dbo.Reyons r
LEFT JOIN dbo.Zones z ON z.Id = r.ZoneId";

    public List<Reyon> GetAll(bool includeDeleted = false)
    {
        var list = new List<Reyon>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR r.IsDeleted=0) ORDER BY r.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Reyon? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE r.Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Reyon Save(Reyon r, int? userId)
    {
        string code = CodeHelper.Normalize(r.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir reyon kodu giriniz.");
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ArgumentException("Reyon adı boş olamaz.");
        if (r.ZoneId == 0) throw new ArgumentException("Lokasyon (zone) seçiniz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Reyons WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", r.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu reyon zaten var.");
        }

        using var command = connection.CreateCommand();
        if (r.Id == 0)
            command.CommandText = "INSERT INTO dbo.Reyons (Code, Name, ZoneId, PrinterName, StationType, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Zone,@Printer,@Station,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.Reyons SET Code=@Code, Name=@Name, ZoneId=@Zone, PrinterName=@Printer, StationType=@Station, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", r.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", r.Name.Trim());
        command.Parameters.AddWithValue("@Zone", r.ZoneId);
        CompanyService.AddNullable(command, "@Printer", r.PrinterName);
        command.Parameters.AddWithValue("@Station", (int)r.StationType);
        command.Parameters.AddWithValue("@Active", r.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (r.Id == 0) r.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        r.Code = code;
        return r;
    }

    private static Reyon Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        ZoneId = r.GetInt32(3),
        ZoneName = r.IsDBNull(4) ? null : r.GetString(4),
        PrinterName = r.IsDBNull(5) ? null : r.GetString(5),
        IsActive = r.GetBoolean(6),
        IsDeleted = r.GetBoolean(7),
        StationType = (StationType)r.GetInt32(8)
    };
}
