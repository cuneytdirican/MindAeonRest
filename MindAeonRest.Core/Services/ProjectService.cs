using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Projeler (şirket DB). Evraklarda zorunlu seçilir.</summary>
public class ProjectService
{
    public List<Project> GetAll(bool includeDeleted = false)
    {
        var list = new List<Project>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.Projects WHERE (@inc=1 OR IsDeleted=0) ORDER BY Code;";
        cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Project { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), IsActive = r.GetBoolean(3), IsDeleted = r.GetBoolean(4) });
        return list;
    }

    public Project? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.Projects WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Project { Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2), IsActive = r.GetBoolean(3), IsDeleted = r.GetBoolean(4) };
    }

    public Project Save(Project p, int? userId)
    {
        string code = CodeHelper.Normalize(p.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir proje kodu giriniz.");
        if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("Proje adı boş olamaz.");

        using var c = Database.OpenCompany();
        using (var chk = c.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(1) FROM dbo.Projects WHERE Code=@c AND Id<>@id;";
            chk.Parameters.AddWithValue("@c", code);
            chk.Parameters.AddWithValue("@id", p.Id);
            if ((int)chk.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu proje zaten var.");
        }

        using var cmd = c.CreateCommand();
        if (p.Id == 0)
            cmd.CommandText = "INSERT INTO dbo.Projects (Code, Name, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Code,@Name,@Active,@By);";
        else
        {
            cmd.CommandText = "UPDATE dbo.Projects SET Code=@Code, Name=@Name, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            cmd.Parameters.AddWithValue("@Id", p.Id);
        }
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", p.Name.Trim());
        cmd.Parameters.AddWithValue("@Active", p.IsActive);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        if (p.Id == 0) p.Id = (int)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
        p.Code = code;
        return p;
    }
}
