using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Nakit Kasalar (şirket DB). Şube + döviz; ikisi de cross-db.</summary>
public class CashRegisterService
{
    private static string SelectBase => $@"
SELECT k.Id, k.BranchId, b.Name AS BranchName, k.Code, k.Name, k.CurrencyId, cur.Code AS CurrencyCode, k.IsActive, k.IsDeleted,
       k.Scope, k.KioskId, ks.Name AS KioskName
FROM dbo.CashRegisters k
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = k.BranchId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = k.CurrencyId
LEFT JOIN dbo.Kiosks ks ON ks.Id = k.KioskId";

    public List<CashRegister> GetAll(bool includeDeleted = false)
    {
        var list = new List<CashRegister>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR k.IsDeleted=0) ORDER BY k.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new CashRegister
            {
                Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Code = reader.GetString(3), Name = reader.GetString(4), CurrencyId = reader.GetInt32(5),
                CurrencyCode = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetBoolean(7), IsDeleted = reader.GetBoolean(8),
                Scope = (FinanceScope)reader.GetInt32(9), KioskId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                KioskName = reader.IsDBNull(11) ? null : reader.GetString(11)
            });
        return list;
    }

    /// <summary>Tek kasa (Id ile); bulunamazsa null. Web Edit formu için additive.</summary>
    public CashRegister? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE k.Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new CashRegister
        {
            Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Code = reader.GetString(3), Name = reader.GetString(4), CurrencyId = reader.GetInt32(5),
            CurrencyCode = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetBoolean(7), IsDeleted = reader.GetBoolean(8),
            Scope = (FinanceScope)reader.GetInt32(9), KioskId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            KioskName = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }

    public CashRegister Save(CashRegister k, int? userId)
    {
        string code = CodeHelper.Normalize(k.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kasa kodu giriniz.");
        if (string.IsNullOrWhiteSpace(k.Name)) throw new ArgumentException("Kasa adı boş olamaz.");
        if (k.BranchId == 0) throw new ArgumentException("Şube seçiniz.");
        if (k.CurrencyId == 0) throw new ArgumentException("Döviz tipi seçiniz (zorunlu).");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.CashRegisters WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", k.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu kasa zaten var.");
        }

        using var command = connection.CreateCommand();
        if (k.Id == 0)
            command.CommandText = "INSERT INTO dbo.CashRegisters (BranchId, Code, Name, CurrencyId, Scope, KioskId, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Branch,@Code,@Name,@Cur,@Scope,@Kiosk,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.CashRegisters SET BranchId=@Branch, Code=@Code, Name=@Name, CurrencyId=@Cur, Scope=@Scope, KioskId=@Kiosk, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", k.Id);
        }
        command.Parameters.AddWithValue("@Branch", k.BranchId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", k.Name.Trim());
        command.Parameters.AddWithValue("@Cur", k.CurrencyId);
        command.Parameters.AddWithValue("@Scope", (int)k.Scope);
        command.Parameters.AddWithValue("@Kiosk", k.Scope == FinanceScope.Kiosk ? (object?)k.KioskId ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Active", k.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (k.Id == 0) k.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        k.Code = code;
        return k;
    }
}
