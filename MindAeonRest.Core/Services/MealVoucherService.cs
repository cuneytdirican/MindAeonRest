using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Yemek Çekleri (şirket DB lookup).</summary>
public class MealVoucherService
{
    public List<MealVoucher> GetAll(bool includeDeleted = false)
    {
        var list = new List<MealVoucher>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.MealVouchers WHERE (@inc=1 OR IsDeleted=0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new MealVoucher { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public MealVoucher? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.MealVouchers WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new MealVoucher { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) };
    }

    public MealVoucher Save(MealVoucher v, int? userId)
    {
        string code = CodeHelper.Normalize(v.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir yemek çeki kodu giriniz.");
        if (string.IsNullOrWhiteSpace(v.Name)) throw new ArgumentException("Yemek çeki adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.MealVouchers WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", v.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu yemek çeki zaten var.");
        }

        using var command = connection.CreateCommand();
        if (v.Id == 0)
            command.CommandText = "INSERT INTO dbo.MealVouchers (Code, Name, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.MealVouchers SET Code=@Code, Name=@Name, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", v.Id);
        }
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", v.Name.Trim());
        command.Parameters.AddWithValue("@Active", v.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (v.Id == 0) v.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        v.Code = code;
        return v;
    }
}
