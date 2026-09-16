using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Rezervasyon etiketleri (şirket DB lookup, renkli).</summary>
public class ReservationTagService
{
    public List<ReservationTag> GetAll(bool includeDeleted = false)
    {
        var list = new List<ReservationTag>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, Color, IsActive, IsDeleted FROM dbo.ReservationTags WHERE (@inc=1 OR IsDeleted=0) ORDER BY Name;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ReservationTag
            {
                Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2),
                Color = reader.IsDBNull(3) ? null : reader.GetString(3), IsActive = reader.GetBoolean(4), IsDeleted = reader.GetBoolean(5)
            });
        return list;
    }

    /// <summary>Tek etiket (silinmiş dahil) — düzenleme ekranı için.</summary>
    public ReservationTag? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, Color, IsActive, IsDeleted FROM dbo.ReservationTags WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new ReservationTag
        {
            Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2),
            Color = reader.IsDBNull(3) ? null : reader.GetString(3), IsActive = reader.GetBoolean(4), IsDeleted = reader.GetBoolean(5)
        };
    }

    public ReservationTag Save(ReservationTag t, int? userId)
    {
        string code = CodeHelper.Normalize(t.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir etiket kodu giriniz.");
        if (string.IsNullOrWhiteSpace(t.Name)) throw new ArgumentException("Etiket adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.ReservationTags WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", t.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu etiket zaten var.");
        }

        using var command = connection.CreateCommand();
        if (t.Id == 0)
            command.CommandText = "INSERT INTO dbo.ReservationTags (Code, Name, Color, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Color,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.ReservationTags SET Code=@Code, Name=@Name, Color=@Color, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", t.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", t.Name.Trim());
        CompanyService.AddNullable(command, "@Color", t.Color);
        command.Parameters.AddWithValue("@Active", t.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (t.Id == 0) t.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        t.Code = code;
        return t;
    }
}
