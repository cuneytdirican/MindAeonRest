using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class CurrencyService
{
    public List<Currency> GetAll()
    {
        var list = new List<Currency>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, Symbol, IsActive FROM dbo.Currencies WHERE IsDeleted=0 ORDER BY Code;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new Currency
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Symbol = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        return list;
    }

    /// <summary>Varsayılan döviz (TRY) Id'si; yoksa ilk kaydın Id'si; hiç yoksa null.</summary>
    public int? GetDefaultId()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1 Id FROM dbo.Currencies WHERE IsDeleted=0
ORDER BY CASE WHEN Code='TRY' THEN 0 ELSE 1 END, Id;";
        var result = command.ExecuteScalar();
        return result is int id ? id : null;
    }
}
