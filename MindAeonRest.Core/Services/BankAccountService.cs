using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Banka Hesapları (şirket DB). Döviz zorunlu; banka local, döviz cross-db.</summary>
public class BankAccountService
{
    private static string SelectBase => $@"
SELECT a.Id, a.BankId, bk.Name AS BankName, a.Code, a.Name, a.CurrencyId, cur.Code AS CurrencyCode, a.IsActive, a.IsDeleted
FROM dbo.BankAccounts a
LEFT JOIN dbo.Banks bk ON bk.Id = a.BankId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = a.CurrencyId";

    public List<BankAccount> GetAll(bool includeDeleted = false)
    {
        var list = new List<BankAccount>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR a.IsDeleted=0) ORDER BY a.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new BankAccount
            {
                Id = reader.GetInt32(0), BankId = reader.GetInt32(1), BankName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Code = reader.GetString(3), Name = reader.GetString(4), CurrencyId = reader.GetInt32(5),
                CurrencyCode = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetBoolean(7), IsDeleted = reader.GetBoolean(8)
            });
        return list;
    }

    /// <summary>Tek kayıt (silinmiş dahil). Bulunamazsa null.</summary>
    public BankAccount? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE a.Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new BankAccount
        {
            Id = reader.GetInt32(0), BankId = reader.GetInt32(1), BankName = reader.IsDBNull(2) ? null : reader.GetString(2),
            Code = reader.GetString(3), Name = reader.GetString(4), CurrencyId = reader.GetInt32(5),
            CurrencyCode = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetBoolean(7), IsDeleted = reader.GetBoolean(8)
        };
    }

    public BankAccount Save(BankAccount a, int? userId)
    {
        string code = CodeHelper.Normalize(a.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir hesap kodu giriniz.");
        if (string.IsNullOrWhiteSpace(a.Name)) throw new ArgumentException("Hesap adı boş olamaz.");
        if (a.BankId == 0) throw new ArgumentException("Banka seçiniz.");
        if (a.CurrencyId == 0) throw new ArgumentException("Döviz tipi seçiniz (zorunlu).");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.BankAccounts WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", a.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu banka hesabı zaten var.");
        }

        using var command = connection.CreateCommand();
        if (a.Id == 0)
            command.CommandText = "INSERT INTO dbo.BankAccounts (BankId, Code, Name, CurrencyId, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Bank,@Code,@Name,@Cur,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.BankAccounts SET BankId=@Bank, Code=@Code, Name=@Name, CurrencyId=@Cur, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", a.Id);
        }
        command.Parameters.AddWithValue("@Bank", a.BankId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", a.Name.Trim());
        command.Parameters.AddWithValue("@Cur", a.CurrencyId);
        command.Parameters.AddWithValue("@Active", a.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (a.Id == 0) a.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        a.Code = code;
        return a;
    }
}
