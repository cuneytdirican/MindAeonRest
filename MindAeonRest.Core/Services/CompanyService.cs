using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class CompanyService
{
    private readonly BranchService _branchService = new();

    private const string SelectBase = @"
SELECT c.Id, c.Code, c.Name, c.DatabaseName, c.IsActive,
       c.Address, c.CountryId, c.CityId, c.DistrictId, c.TaxOffice, c.TaxNumber, c.Phone, c.CurrencyId,
       co.Name AS CountryName, ci.Name AS CityName, di.Name AS DistrictName,
       cur.Code AS CurrencyCode, cur.Name AS CurrencyName, c.CreatedAt
FROM dbo.Companies c
LEFT JOIN dbo.Countries  co  ON co.Id  = c.CountryId
LEFT JOIN dbo.Cities     ci  ON ci.Id  = c.CityId
LEFT JOIN dbo.Districts  di  ON di.Id  = c.DistrictId
LEFT JOIN dbo.Currencies cur ON cur.Id = c.CurrencyId";

    public List<Company> GetAll()
    {
        var list = new List<Company>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE c.IsDeleted = 0 ORDER BY c.Code;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Company? GetById(int id)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE c.Id = @Id AND c.IsDeleted = 0;";
        command.Parameters.AddWithValue("@Id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Company? GetByCode(string code)
    {
        string normalized = CodeHelper.Normalize(code);
        if (normalized.Length == 0) return null;

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE c.Code = @Code AND c.IsDeleted = 0;";
        command.Parameters.AddWithValue("@Code", normalized);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool CodeExists(string code)
    {
        string normalized = CodeHelper.Normalize(code);
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Companies WHERE Code = @Code;";
        command.Parameters.AddWithValue("@Code", normalized);
        return (int)command.ExecuteScalar() > 0;
    }

    /// <summary>
    /// Yeni şirket oluşturur: kaydı ekler, MindAeonRest_{Kod} veritabanını kurar,
    /// şemasını hazırlar ve varsayılan "MERKEZ" şubesini açar.
    /// </summary>
    public Company Create(string code, string name, int? currencyId, int? createdByUserId)
    {
        string normalized = CodeHelper.Normalize(code);
        if (normalized.Length == 0)
            throw new ArgumentException("Geçerli bir şirket kodu giriniz (yalnızca harf/rakam).");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Şirket adı boş olamaz.");
        if (CodeExists(normalized))
            throw new InvalidOperationException($"'{normalized}' kodlu bir şirket zaten mevcut.");

        currencyId ??= new CurrencyService().GetDefaultId();
        string databaseName = CompanyDatabase.NameFor(normalized);

        Company company;
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
INSERT INTO dbo.Companies (Code, Name, DatabaseName, CurrencyId, CreatedBy)
OUTPUT INSERTED.Id, INSERTED.CreatedAt
VALUES (@Code, @Name, @DatabaseName, @CurrencyId, @CreatedBy);";
            command.Parameters.AddWithValue("@Code", normalized);
            command.Parameters.AddWithValue("@Name", name.Trim());
            command.Parameters.AddWithValue("@DatabaseName", databaseName);
            command.Parameters.AddWithValue("@CurrencyId", (object?)currencyId ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatedBy", (object?)createdByUserId ?? DBNull.Value);

            using var reader = command.ExecuteReader();
            reader.Read();
            company = new Company
            {
                Id = reader.GetInt32(0),
                Code = normalized,
                Name = name.Trim(),
                DatabaseName = databaseName,
                IsActive = true,
                CurrencyId = currencyId,
                CreatedAt = reader.GetDateTime(1),
                CreatedBy = createdByUserId
            };
        }

        CompanyDatabase.Provision(company);
        // Yeni kurulum: Adisyon/Mutfak ekranları V2 ile başlasın (yeni ekranlardan talep edildi).
        // Mevcut kurulumlar satırsız kaldığından Varsayılan'da kalır.
        try { CompanyUiSettingsService.SetVersions(databaseName, UiVersions.V2, UiVersions.V2, createdByUserId); } catch { }
        _branchService.EnsureDefaultBranch(company.Id, createdByUserId);
        return company;
    }

    /// <summary>Şirket tanım bilgilerini günceller (kod ve veritabanı değişmez).</summary>
    public void Update(Company c, int? updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(c.Name))
            throw new ArgumentException("Şirket adı boş olamaz.");

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.Companies SET
    Name=@Name, IsActive=@IsActive, Address=@Address,
    CountryId=@CountryId, CityId=@CityId, DistrictId=@DistrictId,
    TaxOffice=@TaxOffice, TaxNumber=@TaxNumber, Phone=@Phone, CurrencyId=@CurrencyId,
    UpdatedBy=@UpdatedBy, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Name", c.Name.Trim());
        command.Parameters.AddWithValue("@IsActive", c.IsActive);
        AddNullable(command, "@Address", c.Address);
        command.Parameters.AddWithValue("@CountryId", (object?)c.CountryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CityId", (object?)c.CityId ?? DBNull.Value);
        command.Parameters.AddWithValue("@DistrictId", (object?)c.DistrictId ?? DBNull.Value);
        AddNullable(command, "@TaxOffice", c.TaxOffice);
        AddNullable(command, "@TaxNumber", c.TaxNumber);
        AddNullable(command, "@Phone", c.Phone);
        command.Parameters.AddWithValue("@CurrencyId", (object?)c.CurrencyId ?? DBNull.Value);
        command.Parameters.AddWithValue("@UpdatedBy", (object?)updatedByUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", c.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Şirketin aktiflik durumunu değiştirir. Pasif şirkete yalnızca SysAdmin giriş yapabilir.</summary>
    public void SetActive(int companyId, bool isActive, int? byUserId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.Companies SET
    IsActive=@IsActive, UpdatedBy=@UpdatedBy, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
        command.Parameters.AddWithValue("@IsActive", isActive);
        command.Parameters.AddWithValue("@UpdatedBy", (object?)byUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", companyId);
        command.ExecuteNonQuery();
    }

    /// <summary>Şirketin renkli ve mono logolarını getirir (yoksa null).</summary>
    public (byte[]? Color, byte[]? Mono) GetLogos(int companyId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LogoColor, LogoMono FROM dbo.Companies WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Id", companyId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (null, null);
        byte[]? color = reader.IsDBNull(0) ? null : (byte[])reader[0];
        byte[]? mono = reader.IsDBNull(1) ? null : (byte[])reader[1];
        return (color, mono);
    }

    /// <summary>Şirket logolarını kaydeder (null = değiştirme yok için ayrı; burada doğrudan set eder).</summary>
    public void SaveLogos(int companyId, byte[]? color, byte[]? mono, int? userId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Companies SET LogoColor=@Color, LogoMono=@Mono, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        var pc = new SqlParameter("@Color", System.Data.SqlDbType.VarBinary, -1) { Value = (object?)color ?? DBNull.Value };
        var pm = new SqlParameter("@Mono", System.Data.SqlDbType.VarBinary, -1) { Value = (object?)mono ?? DBNull.Value };
        command.Parameters.Add(pc);
        command.Parameters.Add(pm);
        command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", companyId);
        command.ExecuteNonQuery();
    }

    internal static void AddNullable(SqlCommand cmd, string name, string? value)
        => cmd.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static Company Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        DatabaseName = r.GetString(3),
        IsActive = r.GetBoolean(4),
        Address = r.IsDBNull(5) ? null : r.GetString(5),
        CountryId = r.IsDBNull(6) ? null : r.GetInt32(6),
        CityId = r.IsDBNull(7) ? null : r.GetInt32(7),
        DistrictId = r.IsDBNull(8) ? null : r.GetInt32(8),
        TaxOffice = r.IsDBNull(9) ? null : r.GetString(9),
        TaxNumber = r.IsDBNull(10) ? null : r.GetString(10),
        Phone = r.IsDBNull(11) ? null : r.GetString(11),
        CurrencyId = r.IsDBNull(12) ? null : r.GetInt32(12),
        CountryName = r.IsDBNull(13) ? null : r.GetString(13),
        CityName = r.IsDBNull(14) ? null : r.GetString(14),
        DistrictName = r.IsDBNull(15) ? null : r.GetString(15),
        CurrencyCode = r.IsDBNull(16) ? null : r.GetString(16),
        CurrencyName = r.IsDBNull(17) ? null : r.GetString(17),
        CreatedAt = r.GetDateTime(18)
    };
}
