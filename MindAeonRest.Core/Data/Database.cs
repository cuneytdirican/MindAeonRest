using Microsoft.Data.SqlClient;
using MindAeonRest.Helpers;

namespace MindAeonRest.Data;

/// <summary>
/// MindAeonRest veritabanına doğrudan (ADO.NET) erişim için ince yardımcı katman.
/// </summary>
public static class Database
{
    public static SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(AppConfig.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Belirtilen veritabanına (ör. şirket DB'si) bağlantı açar.</summary>
    public static SqlConnection OpenConnection(string databaseName)
    {
        var connection = new SqlConnection(AppConfig.GetConnectionString(databaseName));
        connection.Open();
        return connection;
    }

    /// <summary>Oturumdaki aktif şirketin veritabanına bağlantı açar.</summary>
    public static SqlConnection OpenCompany()
    {
        string db = Session.CurrentCompany?.DatabaseName
            ?? throw new InvalidOperationException("Aktif şirket bulunamadı.");
        return OpenConnection(db);
    }

    public static SqlCommand CreateCommand(SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>Bağlantıyı test eder; başarısızsa hata mesajını döndürür.</summary>
    public static (bool Ok, string? Error) TestConnection()
    {
        try
        {
            using var connection = new SqlConnection(AppConfig.MasterConnectionString);
            connection.Open();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
