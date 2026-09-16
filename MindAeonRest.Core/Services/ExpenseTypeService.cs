using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Gider tipleri (şirket DB). Maaş tipi → gider fişinde personel istenir.</summary>
public class ExpenseTypeService
{
    public List<ExpenseType> GetAll(bool includeDeleted = false)
    {
        var list = new List<ExpenseType>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, IsSalary, IsActive, IsDeleted FROM dbo.ExpenseTypes WHERE (@inc=1 OR IsDeleted=0) ORDER BY Code;";
        cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ExpenseType { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), IsSalary = r.GetBoolean(3), IsActive = r.GetBoolean(4), IsDeleted = r.GetBoolean(5) });
        return list;
    }

    public ExpenseType? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, IsSalary, IsActive, IsDeleted FROM dbo.ExpenseTypes WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ExpenseType { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), IsSalary = r.GetBoolean(3), IsActive = r.GetBoolean(4), IsDeleted = r.GetBoolean(5) };
    }

    public ExpenseType Save(ExpenseType x, int? userId)
    {
        string code = CodeHelper.Normalize(x.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir gider tipi kodu giriniz.");
        if (string.IsNullOrWhiteSpace(x.Name)) throw new ArgumentException("Gider tipi adı boş olamaz.");

        using var c = Database.OpenCompany();
        using (var chk = c.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(1) FROM dbo.ExpenseTypes WHERE Code=@c AND Id<>@id;";
            chk.Parameters.AddWithValue("@c", code);
            chk.Parameters.AddWithValue("@id", x.Id);
            if ((int)chk.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu gider tipi zaten var.");
        }

        using var cmd = c.CreateCommand();
        if (x.Id == 0)
            cmd.CommandText = "INSERT INTO dbo.ExpenseTypes (Code, Name, IsSalary, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Sal,@Active,@By);";
        else
        {
            cmd.CommandText = "UPDATE dbo.ExpenseTypes SET Code=@Code, Name=@Name, IsSalary=@Sal, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            cmd.Parameters.AddWithValue("@Id", x.Id);
        }
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", x.Name.Trim());
        cmd.Parameters.AddWithValue("@Sal", x.IsSalary);
        cmd.Parameters.AddWithValue("@Active", x.IsActive);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        if (x.Id == 0) x.Id = (int)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
        x.Code = code;
        return x;
    }
}
