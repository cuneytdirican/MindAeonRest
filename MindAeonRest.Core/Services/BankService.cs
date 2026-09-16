using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Bankalar (şirket DB lookup).</summary>
public class BankService
{
    public List<Bank> GetAll(bool includeDeleted = false)
    {
        var list = new List<Bank>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.Banks WHERE (@inc=1 OR IsDeleted=0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new Bank { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public Bank? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.Banks WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Bank { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) };
    }

    public Bank Save(Bank b, int? userId)
    {
        string code = CodeHelper.Normalize(b.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir banka kodu giriniz.");
        if (string.IsNullOrWhiteSpace(b.Name)) throw new ArgumentException("Banka adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Banks WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", b.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu banka zaten var.");
        }

        using var command = connection.CreateCommand();
        if (b.Id == 0)
            command.CommandText = "INSERT INTO dbo.Banks (Code, Name, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.Banks SET Code=@Code, Name=@Name, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", b.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", b.Name.Trim());
        command.Parameters.AddWithValue("@Active", b.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (b.Id == 0) b.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        b.Code = code;
        return b;
    }
}
