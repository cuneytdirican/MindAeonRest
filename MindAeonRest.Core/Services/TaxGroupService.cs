using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Vergi grupları ve tarih aralıklı oranları (aktif şirket DB'sinde).</summary>
public class TaxGroupService
{
    public List<TaxGroup> GetAll(bool includeDeleted = false)
    {
        var list = new List<TaxGroup>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.TaxGroups WHERE (@inc = 1 OR IsDeleted = 0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new TaxGroup { Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), IsActive = reader.GetBoolean(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public TaxGroup? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsActive, IsDeleted FROM dbo.TaxGroups WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new TaxGroup
        {
            Id = reader.GetInt32(0),
            Code = reader.GetString(1),
            Name = reader.GetString(2),
            IsActive = reader.GetBoolean(3),
            IsDeleted = reader.GetBoolean(4)
        };
    }

    public List<TaxGroupRate> GetRates(int taxGroupId)
    {
        var list = new List<TaxGroupRate>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, TaxGroupId, VatType, Rate, StartDate, EndDate
FROM dbo.TaxGroupRates WHERE TaxGroupId=@g AND IsDeleted=0
ORDER BY VatType, StartDate;";
        command.Parameters.AddWithValue("@g", taxGroupId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new TaxGroupRate
            {
                Id = reader.GetInt32(0),
                TaxGroupId = reader.GetInt32(1),
                VatType = (VatType)reader.GetInt32(2),
                Rate = reader.GetDecimal(3),
                StartDate = reader.GetDateTime(4),
                EndDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
            });
        return list;
    }

    /// <summary>Grubu ve oranlarını topluca kaydeder (oranlar tümüyle yeniden yazılır).</summary>
    public TaxGroup Save(TaxGroup group, IEnumerable<TaxGroupRate> rates, int? userId)
    {
        string code = CodeHelper.Normalize(group.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir grup kodu giriniz.");
        if (string.IsNullOrWhiteSpace(group.Name)) throw new ArgumentException("Grup adı boş olamaz.");

        using var connection = Database.OpenCompany();

        // Kod tekilliği
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.TaxGroups WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", group.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu vergi grubu zaten var.");
        }

        if (group.Id == 0)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.TaxGroups (Code, Name, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code, @Name, @Active, @By);";
            ins.Parameters.AddWithValue("@Code", code);
            ins.Parameters.AddWithValue("@Name", group.Name.Trim());
            ins.Parameters.AddWithValue("@Active", group.IsActive);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            group.Id = (int)ins.ExecuteScalar()!;
        }
        else
        {
            using var upd = connection.CreateCommand();
            upd.CommandText = @"
UPDATE dbo.TaxGroups SET Code=@Code, Name=@Name, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            upd.Parameters.AddWithValue("@Code", code);
            upd.Parameters.AddWithValue("@Name", group.Name.Trim());
            upd.Parameters.AddWithValue("@Active", group.IsActive);
            upd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Id", group.Id);
            upd.ExecuteNonQuery();
        }

        // Oranları yeniden yaz (eskileri soft-delete, yenileri ekle)
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.TaxGroupRates SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE TaxGroupId=@g AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@g", group.Id);
            del.ExecuteNonQuery();
        }
        foreach (var r in rates)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.TaxGroupRates (TaxGroupId, VatType, Rate, StartDate, EndDate, CreatedBy)
VALUES (@g, @t, @r, @s, @e, @By);";
            ins.Parameters.AddWithValue("@g", group.Id);
            ins.Parameters.AddWithValue("@t", (int)r.VatType);
            ins.Parameters.AddWithValue("@r", r.Rate);
            ins.Parameters.AddWithValue("@s", r.StartDate.Date);
            ins.Parameters.AddWithValue("@e", (object?)r.EndDate?.Date ?? DBNull.Value);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }

        group.Code = code;
        return group;
    }
}
