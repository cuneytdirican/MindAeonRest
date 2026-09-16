using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Adisyon/Mutfak ekran sürümü ayarı (şirket DB, tek satır id=1).
/// Satır yoksa Varsayılan döner (mevcut kurulumları bozmaz). Yeni kurulumlar Create'te V2'ye set edilir.</summary>
public class CompanyUiSettingsService
{
    public CompanyUiSettings Get()
    {
        using var c = Database.OpenCompany();
        return Read(c);
    }

    /// <summary>Belirli bir şirket DB'sinden okur (oturum dışı bağlamlar için).</summary>
    public static CompanyUiSettings GetFor(string databaseName)
    {
        using var c = Database.OpenConnection(databaseName);
        return Read(c);
    }

    private static CompanyUiSettings Read(SqlConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT AdisyonVersion, MutfakVersion FROM dbo.CompanyUiSettings WHERE Id=1;";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new CompanyUiSettings();   // satır yok → Varsayılan
        return new CompanyUiSettings
        {
            AdisyonVersion = UiVersions.Normalize(r.IsDBNull(0) ? null : r.GetString(0)),
            MutfakVersion = UiVersions.Normalize(r.IsDBNull(1) ? null : r.GetString(1))
        };
    }

    public void Save(CompanyUiSettings s, int? userId)
    {
        using var c = Database.OpenCompany();
        Upsert(c, s, userId);
    }

    /// <summary>Yeni şirket kurulumunda sürümleri set eder (oturum dışı; DB adıyla).</summary>
    public static void SetVersions(string databaseName, string adisyon, string mutfak, int? userId)
    {
        using var c = Database.OpenConnection(databaseName);
        Upsert(c, new CompanyUiSettings
        {
            AdisyonVersion = UiVersions.Normalize(adisyon),
            MutfakVersion = UiVersions.Normalize(mutfak)
        }, userId);
    }

    private static void Upsert(SqlConnection c, CompanyUiSettings s, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.CompanyUiSettings WHERE Id=1)
    UPDATE dbo.CompanyUiSettings SET AdisyonVersion=@a, MutfakVersion=@m, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=1;
ELSE
    INSERT INTO dbo.CompanyUiSettings (Id, AdisyonVersion, MutfakVersion, CreatedBy) VALUES (1,@a,@m,@by);";
        cmd.Parameters.AddWithValue("@a", UiVersions.Normalize(s.AdisyonVersion));
        cmd.Parameters.AddWithValue("@m", UiVersions.Normalize(s.MutfakVersion));
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
