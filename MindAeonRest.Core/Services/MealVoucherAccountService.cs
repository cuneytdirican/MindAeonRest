using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Yemek Çek Hesapları (şirket DB). Şube + yemek çeki + döviz.</summary>
public class MealVoucherAccountService
{
    private static string SelectBase => $@"
SELECT a.Id, a.BranchId, b.Name AS BranchName, a.MealVoucherId, mv.Name AS VoucherName, a.Code, a.Name, a.CurrencyId, cur.Code AS CurrencyCode, a.IsActive, a.IsDeleted,
       a.Scope, a.KioskId, ks.Name AS KioskName
FROM dbo.MealVoucherAccounts a
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = a.BranchId
LEFT JOIN dbo.MealVouchers mv ON mv.Id = a.MealVoucherId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = a.CurrencyId
LEFT JOIN dbo.Kiosks ks ON ks.Id = a.KioskId";

    public List<MealVoucherAccount> GetAll(bool includeDeleted = false)
    {
        var list = new List<MealVoucherAccount>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR a.IsDeleted=0) ORDER BY a.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new MealVoucherAccount
            {
                Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
                MealVoucherId = reader.GetInt32(3), MealVoucherName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Code = reader.GetString(5), Name = reader.GetString(6), CurrencyId = reader.GetInt32(7),
                CurrencyCode = reader.IsDBNull(8) ? null : reader.GetString(8), IsActive = reader.GetBoolean(9), IsDeleted = reader.GetBoolean(10),
                Scope = (FinanceScope)reader.GetInt32(11), KioskId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                KioskName = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        return list;
    }

    public MealVoucherAccount? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE a.Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new MealVoucherAccount
        {
            Id = reader.GetInt32(0), BranchId = reader.GetInt32(1), BranchName = reader.IsDBNull(2) ? null : reader.GetString(2),
            MealVoucherId = reader.GetInt32(3), MealVoucherName = reader.IsDBNull(4) ? null : reader.GetString(4),
            Code = reader.GetString(5), Name = reader.GetString(6), CurrencyId = reader.GetInt32(7),
            CurrencyCode = reader.IsDBNull(8) ? null : reader.GetString(8), IsActive = reader.GetBoolean(9), IsDeleted = reader.GetBoolean(10),
            Scope = (FinanceScope)reader.GetInt32(11), KioskId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            KioskName = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    public MealVoucherAccount Save(MealVoucherAccount a, int? userId)
    {
        string code = CodeHelper.Normalize(a.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir hesap kodu giriniz.");
        if (string.IsNullOrWhiteSpace(a.Name)) throw new ArgumentException("Hesap adı boş olamaz.");
        if (a.BranchId == 0) throw new ArgumentException("Şube seçiniz.");
        if (a.MealVoucherId == 0) throw new ArgumentException("Yemek çeki seçiniz.");
        if (a.CurrencyId == 0) throw new ArgumentException("Döviz tipi seçiniz (zorunlu).");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.MealVoucherAccounts WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", a.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu yemek çek hesabı zaten var.");
        }

        using var command = connection.CreateCommand();
        if (a.Id == 0)
            command.CommandText = "INSERT INTO dbo.MealVoucherAccounts (BranchId, MealVoucherId, Code, Name, CurrencyId, Scope, KioskId, IsActive, CreatedBy) OUTPUT INSERTED.Id VALUES (@Branch,@Voucher,@Code,@Name,@Cur,@Scope,@Kiosk,@Active,@By);";
        else
        {
            command.CommandText = "UPDATE dbo.MealVoucherAccounts SET BranchId=@Branch, MealVoucherId=@Voucher, Code=@Code, Name=@Name, CurrencyId=@Cur, Scope=@Scope, KioskId=@Kiosk, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", a.Id);
        }
        command.Parameters.AddWithValue("@Branch", a.BranchId);
        command.Parameters.AddWithValue("@Voucher", a.MealVoucherId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", a.Name.Trim());
        command.Parameters.AddWithValue("@Cur", a.CurrencyId);
        command.Parameters.AddWithValue("@Scope", (int)a.Scope);
        command.Parameters.AddWithValue("@Kiosk", a.Scope == FinanceScope.Kiosk ? (object?)a.KioskId ?? DBNull.Value : DBNull.Value);
        command.Parameters.AddWithValue("@Active", a.IsActive);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

        if (a.Id == 0) a.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        a.Code = code;
        return a;
    }
}
