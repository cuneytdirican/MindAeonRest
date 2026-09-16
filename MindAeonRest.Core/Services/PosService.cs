using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Kredi Kartı POS'ları (şirket DB). Şube + banka + döviz.</summary>
public class PosService
{
    private static string SelectBase => $@"
SELECT p.Id, p.BranchId, b.Name AS BranchName, p.BankId, bk.Name AS BankName, p.Code, p.Name, p.CurrencyId, cur.Code AS CurrencyCode, p.IsActive, p.IsDeleted,
       p.Scope, p.KioskId, ks.Name AS KioskName
FROM dbo.CreditCardPos p
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = p.BranchId
LEFT JOIN dbo.Banks bk ON bk.Id = p.BankId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = p.CurrencyId
LEFT JOIN dbo.Kiosks ks ON ks.Id = p.KioskId";

    public List<CreditCardPos> GetAll(bool includeDeleted = false)
    {
        var list = new List<CreditCardPos>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR p.IsDeleted=0) ORDER BY p.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new CreditCardPos
            {
                Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
                BankId = reader.GetInt32(3), BankName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Code = reader.GetString(5), Name = reader.GetString(6), CurrencyId = reader.GetInt32(7),
                CurrencyCode = reader.IsDBNull(8) ? null : reader.GetString(8), IsActive = reader.GetBoolean(9), IsDeleted = reader.GetBoolean(10),
                Scope = (FinanceScope)reader.GetInt32(11), KioskId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                KioskName = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        return list;
    }

    public CreditCardPos? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE p.Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new CreditCardPos
        {
            Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
            BankId = reader.GetInt32(3), BankName = reader.IsDBNull(4) ? null : reader.GetString(4),
            Code = reader.GetString(5), Name = reader.GetString(6), CurrencyId = reader.GetInt32(7),
            CurrencyCode = reader.IsDBNull(8) ? null : reader.GetString(8), IsActive = reader.GetBoolean(9), IsDeleted = reader.GetBoolean(10),
            Scope = (FinanceScope)reader.GetInt32(11), KioskId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            KioskName = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    public CreditCardPos Save(CreditCardPos p, int? userId)
    {
        string code = CodeHelper.Normalize(p.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir POS kodu giriniz.");
        if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("POS adı boş olamaz.");
        if (p.BranchId == 0) throw new ArgumentException("Şube seçiniz.");
        if (p.BankId == 0) throw new ArgumentException("Banka seçiniz.");
        if (p.CurrencyId == 0) throw new ArgumentException("Döviz tipi seçiniz (zorunlu).");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.CreditCardPos WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", p.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu POS zaten var.");
        }

        using var command = connection.CreateCommand();
        if (p.Id == 0)
            command.CommandText = "INSERT INTO dbo.CreditCardPos (BranchId, BankId, Code, Name, CurrencyId, Scope, KioskId, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Branch,@Bank,@Code,@Name,@Cur,@Scope,@Kiosk,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.CreditCardPos SET BranchId=@Branch, BankId=@Bank, Code=@Code, Name=@Name, CurrencyId=@Cur, Scope=@Scope, KioskId=@Kiosk, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", p.Id);
        }
        command.Parameters.AddWithValue("@Branch", p.BranchId);
        command.Parameters.AddWithValue("@Bank", p.BankId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", p.Name.Trim());
        command.Parameters.AddWithValue("@Cur", p.CurrencyId);
        command.Parameters.AddWithValue("@Scope", (int)p.Scope);
        command.Parameters.AddWithValue("@Kiosk", p.Scope == FinanceScope.Kiosk ? (object?)p.KioskId ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Active", p.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (p.Id == 0) p.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        p.Code = code;
        return p;
    }
}
