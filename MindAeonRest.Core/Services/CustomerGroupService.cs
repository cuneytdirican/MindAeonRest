using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class CustomerGroupService
{
    public List<CustomerGroup> GetAll(bool includeDeleted = false)
    {
        var list = new List<CustomerGroup>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.CustomerGroups WHERE (@inc = 1 OR IsDeleted = 0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new CustomerGroup { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public CustomerGroup? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.CustomerGroups WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new CustomerGroup { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) };
    }

    public int Create(string code, string name, int? userId)
    {
        code = CodeHelper.Normalize(code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir grup kodu giriniz.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Grup adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.CustomerGroups WHERE Code=@c;";
            check.Parameters.AddWithValue("@c", code);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu grup zaten var.");
        }
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.CustomerGroups (Code, Name, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code, @Name, @By);";
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name.Trim());
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }

    public void Update(int id, string name, bool isActive, int? userId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Grup adı boş olamaz.");
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.CustomerGroups SET Name=@n, IsActive=@a, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@n", name.Trim());
        command.Parameters.AddWithValue("@a", isActive);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }
}
