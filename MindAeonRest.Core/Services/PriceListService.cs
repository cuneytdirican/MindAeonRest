using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class PriceListService
{
    public List<PriceList> GetAll(bool includeDeleted = false)
    {
        var list = new List<PriceList>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.PriceLists WHERE (@inc = 1 OR IsDeleted = 0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new PriceList { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public PriceList? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.PriceLists WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new PriceList { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) };
    }

    public void Update(int id, string code, string name, bool isActive, int? userId)
    {
        code = CodeHelper.Normalize(code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kod giriniz.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ad boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.PriceLists WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu fiyat listesi zaten var.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.PriceLists SET Code=@Code, Name=@Name, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name.Trim());
        command.Parameters.AddWithValue("@Active", isActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();
    }

    public int Create(string code, string name, int? userId)
    {
        code = CodeHelper.Normalize(code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kod giriniz.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ad boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.PriceLists WHERE Code=@c;";
            check.Parameters.AddWithValue("@c", code);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu fiyat listesi zaten var.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.PriceLists (Code, Name, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code, @Name, @By);";
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name.Trim());
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }
}
