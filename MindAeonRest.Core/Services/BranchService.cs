using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class BranchService
{
    private const string SelectBase = @"
SELECT b.Id, b.CompanyId, b.Code, b.Name, b.IsActive,
       b.Address, b.CountryId, b.CityId, b.DistrictId, b.TaxOffice, b.TaxNumber, b.Phone,
       co.Name AS CountryName, ci.Name AS CityName, di.Name AS DistrictName,
       b.CreatedAt, c.Name AS CompanyName, b.IsDeleted, b.DayStartMinutes
FROM dbo.CompanyBranches b
INNER JOIN dbo.Companies c ON c.Id = b.CompanyId
LEFT JOIN dbo.Countries co ON co.Id = b.CountryId
LEFT JOIN dbo.Cities    ci ON ci.Id = b.CityId
LEFT JOIN dbo.Districts di ON di.Id = b.DistrictId";

    public List<CompanyBranch> GetByCompany(int companyId, bool includeDeleted = false)
    {
        var list = new List<CompanyBranch>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE b.CompanyId = @CompanyId AND (@inc = 1 OR b.IsDeleted = 0) ORDER BY b.Code;";
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>Yalnızca aktif şubeler (login şube seçimi için).</summary>
    public List<CompanyBranch> GetActiveByCompany(int companyId)
        => GetByCompany(companyId).Where(b => b.IsActive).ToList();

    public CompanyBranch? GetById(int branchId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE b.Id = @Id AND b.IsDeleted = 0;";
        command.Parameters.AddWithValue("@Id", branchId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>LİSANS: aktif şube sayısı paket limitine ulaştıysa yeni şube engellenir (Enterprise sınırsız).</summary>
    private static void EnforceBranchLimit(Microsoft.Data.SqlClient.SqlConnection connection, int companyId)
    {
        var lic = Helpers.Session.CurrentLicense;
        if (lic is null || lic.MaxBranches == int.MaxValue) return;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.CompanyBranches WHERE CompanyId=@c AND IsActive=1;";
        cmd.Parameters.AddWithValue("@c", companyId);
        int count = Convert.ToInt32(cmd.ExecuteScalar());
        if (count >= lic.MaxBranches)
            throw new InvalidOperationException($"Lisans paketiniz ({lic.PackageName}) en fazla {lic.MaxBranches} şubeye izin veriyor. Çoklu şube için Enterprise paketine yükseltin.");
    }

    public CompanyBranch Create(CompanyBranch b, int? createdByUserId)
    {
        string code = CodeHelper.Normalize(b.Code);
        Validate(code, b.Name);

        using var connection = Database.OpenConnection();
        EnsureCodeFree(connection, b.CompanyId, code, excludeId: 0);
        EnforceBranchLimit(connection, b.CompanyId);

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO dbo.CompanyBranches
    (CompanyId, Code, Name, IsActive, Address, CountryId, CityId, DistrictId, TaxOffice, TaxNumber, Phone, DayStartMinutes, CreatedBy)
OUTPUT INSERTED.Id, INSERTED.CreatedAt
VALUES (@CompanyId, @Code, @Name, @IsActive, @Address, @CountryId, @CityId, @DistrictId, @TaxOffice, @TaxNumber, @Phone, @DayStartMinutes, @CreatedBy);";
        BindFields(command, b, code);
        command.Parameters.AddWithValue("@CreatedBy", (object?)createdByUserId ?? DBNull.Value);

        using var reader = command.ExecuteReader();
        reader.Read();
        b.Id = reader.GetInt32(0);
        b.Code = code;
        b.CreatedAt = reader.GetDateTime(1);
        return b;
    }

    public void Update(CompanyBranch b, int? updatedByUserId)
    {
        string code = CodeHelper.Normalize(b.Code);
        Validate(code, b.Name);

        using var connection = Database.OpenConnection();
        EnsureCodeFree(connection, b.CompanyId, code, excludeId: b.Id);

        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.CompanyBranches SET
    Code=@Code, Name=@Name, IsActive=@IsActive, Address=@Address,
    CountryId=@CountryId, CityId=@CityId, DistrictId=@DistrictId,
    TaxOffice=@TaxOffice, TaxNumber=@TaxNumber, Phone=@Phone, DayStartMinutes=@DayStartMinutes,
    UpdatedBy=@UpdatedBy, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
        BindFields(command, b, code);
        command.Parameters.AddWithValue("@UpdatedBy", (object?)updatedByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", b.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Şirketin hiç şubesi yoksa varsayılan "MERKEZ" şubesini oluşturur.</summary>
    public void EnsureDefaultBranch(int companyId, int? createdByUserId)
    {
        using var connection = Database.OpenConnection();
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM dbo.CompanyBranches WHERE CompanyId=@c AND IsDeleted=0;";
        check.Parameters.AddWithValue("@c", companyId);
        if ((int)check.ExecuteScalar() > 0) return;

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO dbo.CompanyBranches (CompanyId, Code, Name, CreatedBy)
VALUES (@CompanyId, N'MERKEZ', N'Merkez', @CreatedBy);";
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@CreatedBy", (object?)createdByUserId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void Validate(string code, string name)
    {
        if (code.Length == 0) throw new ArgumentException("Geçerli bir şube kodu giriniz.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Şube adı boş olamaz.");
    }

    private static void EnsureCodeFree(SqlConnection connection, int companyId, string code, int excludeId)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM dbo.CompanyBranches WHERE CompanyId=@c AND Code=@code AND Id<>@ex;";
        check.Parameters.AddWithValue("@c", companyId);
        check.Parameters.AddWithValue("@code", code);
        check.Parameters.AddWithValue("@ex", excludeId);
        if ((int)check.ExecuteScalar() > 0)
            throw new InvalidOperationException($"Bu şirkette '{code}' kodlu şube zaten var.");
    }

    private static void BindFields(SqlCommand cmd, CompanyBranch b, string code)
    {
        cmd.Parameters.AddWithValue("@CompanyId", b.CompanyId);
        cmd.Parameters.AddWithValue("@Code", code);
        cmd.Parameters.AddWithValue("@Name", b.Name.Trim());
        cmd.Parameters.AddWithValue("@IsActive", b.IsActive);
        CompanyService.AddNullable(cmd, "@Address", b.Address);
        cmd.Parameters.AddWithValue("@CountryId", (object?)b.CountryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CityId", (object?)b.CityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DistrictId", (object?)b.DistrictId ?? DBNull.Value);
        CompanyService.AddNullable(cmd, "@TaxOffice", b.TaxOffice);
        CompanyService.AddNullable(cmd, "@TaxNumber", b.TaxNumber);
        CompanyService.AddNullable(cmd, "@Phone", b.Phone);
        cmd.Parameters.AddWithValue("@DayStartMinutes", b.DayStartMinutes);
    }

    private static CompanyBranch Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CompanyId = r.GetInt32(1),
        Code = r.GetString(2),
        Name = r.GetString(3),
        IsActive = r.GetBoolean(4),
        Address = r.IsDBNull(5) ? null : r.GetString(5),
        CountryId = r.IsDBNull(6) ? null : r.GetInt32(6),
        CityId = r.IsDBNull(7) ? null : r.GetInt32(7),
        DistrictId = r.IsDBNull(8) ? null : r.GetInt32(8),
        TaxOffice = r.IsDBNull(9) ? null : r.GetString(9),
        TaxNumber = r.IsDBNull(10) ? null : r.GetString(10),
        Phone = r.IsDBNull(11) ? null : r.GetString(11),
        CountryName = r.IsDBNull(12) ? null : r.GetString(12),
        CityName = r.IsDBNull(13) ? null : r.GetString(13),
        DistrictName = r.IsDBNull(14) ? null : r.GetString(14),
        CreatedAt = r.GetDateTime(15),
        CompanyName = r.GetString(16),
        IsDeleted = r.GetBoolean(17),
        DayStartMinutes = r.IsDBNull(18) ? 0 : r.GetInt32(18)
    };

    /// <summary>İş günü kesimini (dakika) tek kolon hızlı SELECT ile döndürür; şube bulunamazsa 0 (gece yarısı).</summary>
    public int GetDayStartMinutes(int branchId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DayStartMinutes FROM dbo.CompanyBranches WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Id", branchId);
        var result = command.ExecuteScalar();
        return (result is null || result is DBNull) ? 0 : Convert.ToInt32(result);
    }
}
