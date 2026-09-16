using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Personel kartları + izinleri (şirket DB; şube adı sistem DB'sinden join).</summary>
public class PersonnelService
{
    private static readonly string SelectBase = $@"
SELECT p.Id, p.Code, p.FullName, p.Title, p.Department, p.NationalId, p.Phone, p.BranchId,
       p.HireDate, p.Salary, p.IsActive, p.IsDeleted, b.Name AS BranchName
FROM dbo.Personnel p
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = p.BranchId";

    public List<Personnel> GetAll(bool includeDeleted = false)
    {
        var list = new List<Personnel>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE (@inc=1 OR p.IsDeleted=0) ORDER BY p.Code;";
        cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public Personnel? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE p.Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    private static Personnel Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Code = r.GetString(1), FullName = r.GetString(2),
        Title = r.IsDBNull(3) ? null : r.GetString(3),
        Department = r.IsDBNull(4) ? null : r.GetString(4),
        NationalId = r.IsDBNull(5) ? null : r.GetString(5),
        Phone = r.IsDBNull(6) ? null : r.GetString(6),
        BranchId = r.IsDBNull(7) ? null : r.GetInt32(7),
        HireDate = r.IsDBNull(8) ? null : r.GetDateTime(8),
        Salary = r.GetDecimal(9),
        IsActive = r.GetBoolean(10),
        IsDeleted = r.GetBoolean(11),
        BranchName = r.IsDBNull(12) ? null : r.GetString(12)
    };

    public Personnel Save(Personnel p, int? userId)
    {
        string code = CodeHelper.Normalize(p.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir personel kodu giriniz.");
        if (string.IsNullOrWhiteSpace(p.FullName)) throw new ArgumentException("Personel adı boş olamaz.");

        using var c = Database.OpenCompany();
        using (var chk = c.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(1) FROM dbo.Personnel WHERE Code=@c AND Id<>@id;";
            chk.Parameters.AddWithValue("@c", code);
            chk.Parameters.AddWithValue("@id", p.Id);
            if ((int)chk.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu personel zaten var.");
        }

        using var cmd = c.CreateCommand();
        if (p.Id == 0)
            cmd.CommandText = @"
INSERT INTO dbo.Personnel (Code, FullName, Title, Department, NationalId, Phone, BranchId, HireDate, Salary, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code,@Name,@Title,@Dep,@Tc,@Ph,@Br,@Hire,@Sal,@Active,@By);";
        else
        {
            cmd.CommandText = @"
UPDATE dbo.Personnel SET Code=@Code, FullName=@Name, Title=@Title, Department=@Dep, NationalId=@Tc, Phone=@Ph,
    BranchId=@Br, HireDate=@Hire, Salary=@Sal, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            cmd.Parameters.AddWithValue("@Id", p.Id);
        }
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", p.FullName.Trim());
        cmd.Parameters.AddWithValue("@Title", (object?)p.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dep", (object?)p.Department ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tc", (object?)p.NationalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ph", (object?)p.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Br", (object?)p.BranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Hire", (object?)p.HireDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sal", p.Salary);
        cmd.Parameters.AddWithValue("@Active", p.IsActive);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        if (p.Id == 0) p.Id = (int)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
        p.Code = code;
        return p;
    }

    // ---- İzinler ----
    public List<PersonnelLeave> GetLeaves(int personnelId)
    {
        var list = new List<PersonnelLeave>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT Id, PersonnelId, LeaveType, StartDate, EndDate, Days, Note
FROM dbo.PersonnelLeaves WHERE PersonnelId=@p AND IsDeleted=0 ORDER BY StartDate DESC;";
        cmd.Parameters.AddWithValue("@p", personnelId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PersonnelLeave
            {
                Id = r.GetInt32(0), PersonnelId = r.GetInt32(1), LeaveType = (LeaveType)r.GetInt32(2),
                StartDate = r.GetDateTime(3), EndDate = r.GetDateTime(4), Days = r.GetInt32(5),
                Note = r.IsDBNull(6) ? null : r.GetString(6)
            });
        return list;
    }

    public PersonnelLeave SaveLeave(PersonnelLeave l, int? userId)
    {
        if (l.PersonnelId == 0) throw new ArgumentException("Personel seçiniz.");
        if (l.EndDate < l.StartDate) throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
        l.Days = (l.EndDate.Date - l.StartDate.Date).Days + 1;

        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        if (l.Id == 0)
            cmd.CommandText = @"
INSERT INTO dbo.PersonnelLeaves (PersonnelId, LeaveType, StartDate, EndDate, Days, Note, CreatedBy)
OUTPUT INSERTED.Id VALUES (@P,@T,@S,@E,@D,@N,@By);";
        else
        {
            cmd.CommandText = "UPDATE dbo.PersonnelLeaves SET LeaveType=@T, StartDate=@S, EndDate=@E, Days=@D, Note=@N, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            cmd.Parameters.AddWithValue("@Id", l.Id);
        }
        cmd.Parameters.AddWithValue("@P", l.PersonnelId);
        cmd.Parameters.AddWithValue("@T", (int)l.LeaveType);
        cmd.Parameters.AddWithValue("@S", l.StartDate.Date);
        cmd.Parameters.AddWithValue("@E", l.EndDate.Date);
        cmd.Parameters.AddWithValue("@D", l.Days);
        cmd.Parameters.AddWithValue("@N", (object?)l.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        if (l.Id == 0) l.Id = (int)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
        return l;
    }

    public void DeleteLeave(int leaveId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.PersonnelLeaves SET IsDeleted=1, DeletedBy=@By, DeletedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        cmd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", leaveId);
        cmd.ExecuteNonQuery();
    }
}
