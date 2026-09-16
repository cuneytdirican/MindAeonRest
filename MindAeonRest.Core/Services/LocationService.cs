using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Ülke / Şehir / İlçe lookup verileri (kademeli combo'lar için) ve tanımlama.</summary>
public class LocationService
{
    public int CreateCountry(string? code, string name, int? by)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ülke adı boş olamaz.");
        using var connection = Database.OpenConnection();
        EnsureUnique(connection, "Countries", "Name", name.Trim(), null, "Bu ülke zaten tanımlı.");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.Countries (Code, Name, CreatedBy) OUTPUT INSERTED.Id VALUES (@code, @name, @by);";
        command.Parameters.AddWithValue("@code", string.IsNullOrWhiteSpace(code) ? (object)DBNull.Value : CodeHelper.Normalize(code));
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@by", (object?)by ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }

    public int CreateCity(int countryId, string name, string? plateCode, int? by)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Şehir adı boş olamaz.");
        using var connection = Database.OpenConnection();
        EnsureUnique(connection, "Cities", "Name", name.Trim(), countryId, "Bu şehir zaten tanımlı.", "CountryId");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.Cities (CountryId, Name, PlateCode, CreatedBy) OUTPUT INSERTED.Id VALUES (@c, @name, @plate, @by);";
        command.Parameters.AddWithValue("@c", countryId);
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@plate", string.IsNullOrWhiteSpace(plateCode) ? (object)DBNull.Value : plateCode.Trim());
        command.Parameters.AddWithValue("@by", (object?)by ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }

    public int CreateDistrict(int cityId, string name, int? by)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("İlçe adı boş olamaz.");
        using var connection = Database.OpenConnection();
        EnsureUnique(connection, "Districts", "Name", name.Trim(), cityId, "Bu ilçe zaten tanımlı.", "CityId");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.Districts (CityId, Name, CreatedBy) OUTPUT INSERTED.Id VALUES (@c, @name, @by);";
        command.Parameters.AddWithValue("@c", cityId);
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@by", (object?)by ?? DBNull.Value);
        return (int)command.ExecuteScalar()!;
    }

    private static void EnsureUnique(Microsoft.Data.SqlClient.SqlConnection connection, string table, string col, string value, int? parentId, string message, string? parentCol = null)
    {
        using var command = connection.CreateCommand();
        string parentClause = parentCol is null ? "" : $" AND {parentCol}=@p";
        command.CommandText = $"SELECT COUNT(1) FROM dbo.{table} WHERE {col}=@v AND IsDeleted=0{parentClause};";
        command.Parameters.AddWithValue("@v", value);
        if (parentCol is not null) command.Parameters.AddWithValue("@p", parentId!);
        if ((int)command.ExecuteScalar() > 0) throw new InvalidOperationException(message);
    }

    public List<Country> GetCountries(bool includeDeleted = false)
    {
        var list = new List<Country>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Name, IsDeleted FROM dbo.Countries WHERE (@inc = 1 OR IsDeleted = 0) ORDER BY Name;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new Country { Id = reader.GetInt32(0), Code = reader.IsDBNull(1) ? null : reader.GetString(1), Name = reader.GetString(2), IsDeleted = reader.GetBoolean(3) });
        return list;
    }

    public List<City> GetCities(int countryId, bool includeDeleted = false)
    {
        var list = new List<City>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CountryId, Name, PlateCode, IsDeleted FROM dbo.Cities WHERE CountryId=@c AND (@inc = 1 OR IsDeleted = 0) ORDER BY Name;";
        command.Parameters.AddWithValue("@c", countryId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new City { Id = reader.GetInt32(0), CountryId = reader.GetInt32(1), Name = reader.GetString(2), PlateCode = reader.IsDBNull(3) ? null : reader.GetString(3), IsDeleted = reader.GetBoolean(4) });
        return list;
    }

    public List<District> GetDistricts(int cityId, bool includeDeleted = false)
    {
        var list = new List<District>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, CityId, Name, IsDeleted FROM dbo.Districts WHERE CityId=@c AND (@inc = 1 OR IsDeleted = 0) ORDER BY Name;";
        command.Parameters.AddWithValue("@c", cityId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new District { Id = reader.GetInt32(0), CityId = reader.GetInt32(1), Name = reader.GetString(2), IsDeleted = reader.GetBoolean(3) });
        return list;
    }
}
