using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Kuryeler (şirket DB).</summary>
public class CourierService
{
    public List<Courier> GetAll(bool includeDeleted = false)
    {
        var list = new List<Courier>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, Phone, IsActive, IsDeleted FROM dbo.Couriers WHERE (@inc=1 OR IsDeleted=0) ORDER BY Name;";
        cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Courier { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), Phone = r.IsDBNull(3) ? null : r.GetString(3), IsActive = r.GetBoolean(4), IsDeleted = r.GetBoolean(5) });
        return list;
    }

    public Courier? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, Phone, IsActive, IsDeleted FROM dbo.Couriers WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Courier { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), Phone = r.IsDBNull(3) ? null : r.GetString(3), IsActive = r.GetBoolean(4), IsDeleted = r.GetBoolean(5) };
    }

    public Courier Save(Courier k, int? userId)
    {
        string code = CodeHelper.Normalize(k.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kurye kodu giriniz.");
        if (string.IsNullOrWhiteSpace(k.Name)) throw new ArgumentException("Kurye adı boş olamaz.");
        using var c = Database.OpenCompany();
        using (var chk = c.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(1) FROM dbo.Couriers WHERE Code=@c AND Id<>@id;";
            chk.Parameters.AddWithValue("@c", code); chk.Parameters.AddWithValue("@id", k.Id);
            if ((int)chk.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu kurye zaten var.");
        }
        using var cmd = c.CreateCommand();
        if (k.Id == 0)
            cmd.CommandText = "INSERT INTO dbo.Couriers (Code, Name, Phone, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Phone,@Active,@By);";
        else { cmd.CommandText = "UPDATE dbo.Couriers SET Code=@Code, Name=@Name, Phone=@Phone, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;"; cmd.Parameters.AddWithValue("@Id", k.Id); }
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", k.Name.Trim());
        CompanyService.AddNullable(cmd, "@Phone", k.Phone);
        cmd.Parameters.AddWithValue("@Active", k.IsActive);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        if (k.Id == 0) k.Id = (int)cmd.ExecuteScalar()!; else cmd.ExecuteNonQuery();
        k.Code = code;
        return k;
    }
}
