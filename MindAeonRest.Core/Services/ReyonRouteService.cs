using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Ürün→Reyon yönlendirme kuralları + çözümleme (en özelden genele).</summary>
public class ReyonRouteService
{
    private const string SelectBase = @"
SELECT r.Id, r.SourceZoneId, z.Name AS ZoneName, r.ProductId, p.Name AS ProductName, r.CategoryId, c.Name AS CategoryName, r.ReyonId, ry.Name AS ReyonName, r.IsDeleted
FROM dbo.ReyonRoutes r
LEFT JOIN dbo.Zones z ON z.Id = r.SourceZoneId
LEFT JOIN dbo.Products p ON p.Id = r.ProductId
LEFT JOIN dbo.ProductCategories c ON c.Id = r.CategoryId
LEFT JOIN dbo.Reyons ry ON ry.Id = r.ReyonId";

    public List<ReyonRoute> GetAll(bool includeDeleted = false)
    {
        var list = new List<ReyonRoute>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc=1 OR r.IsDeleted=0) ORDER BY r.Id;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ReyonRoute
            {
                Id = reader.GetInt32(0),
                SourceZoneId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                SourceZoneName = reader.IsDBNull(2) ? null : reader.GetString(2),
                ProductId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ProductName = reader.IsDBNull(4) ? null : reader.GetString(4),
                CategoryId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                CategoryName = reader.IsDBNull(6) ? null : reader.GetString(6),
                ReyonId = reader.GetInt32(7),
                ReyonName = reader.IsDBNull(8) ? null : reader.GetString(8),
                IsDeleted = reader.GetBoolean(9)
            });
        return list;
    }

    public ReyonRoute Save(ReyonRoute r, int? userId)
    {
        if (r.ReyonId == 0) throw new ArgumentException("Reyon seçiniz.");
        if (r.ProductId is not null && r.CategoryId is not null)
            throw new ArgumentException("Kural ya ürün ya da kategori bazlı olmalı (ikisi birden değil).");

        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        if (r.Id == 0)
            command.CommandText = "INSERT INTO dbo.ReyonRoutes (SourceZoneId, ProductId, CategoryId, ReyonId, CreatedBy) OUTPUT INSERTED.Id VALUES (@z,@p,@c,@r,@by);";
        else
        {
            command.CommandText = "UPDATE dbo.ReyonRoutes SET SourceZoneId=@z, ProductId=@p, CategoryId=@c, ReyonId=@r, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.AddWithValue("@Id", r.Id);
        }
        command.Parameters.AddWithValue("@z", (object?)r.SourceZoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("@p", (object?)r.ProductId ?? DBNull.Value);
        command.Parameters.AddWithValue("@c", (object?)r.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@r", r.ReyonId);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        if (r.Id == 0) r.Id = (int)command.ExecuteScalar()!;
        else command.ExecuteNonQuery();
        return r;
    }

    public void Delete(int id, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.ReyonRoutes SET IsDeleted=1, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Bir ürünün, verilen lokasyonda hangi reyona düşeceğini çözer (en özel kural). Yoksa null.</summary>
    public int? Resolve(int zoneId, int productId)
    {
        using var connection = Database.OpenCompany();

        var categoryIds = new List<int>();
        using (var cc = connection.CreateCommand())
        {
            cc.CommandText = "SELECT CategoryId FROM dbo.ProductCategoryLinks WHERE ProductId=@p;";
            cc.Parameters.AddWithValue("@p", productId);
            using var rr = cc.ExecuteReader();
            while (rr.Read()) categoryIds.Add(rr.GetInt32(0));
        }

        var routes = new List<(int? Zone, int? Prod, int? Cat, int Reyon)>();
        using (var rc = connection.CreateCommand())
        {
            rc.CommandText = "SELECT SourceZoneId, ProductId, CategoryId, ReyonId FROM dbo.ReyonRoutes WHERE IsDeleted=0;";
            using var rr = rc.ExecuteReader();
            while (rr.Read())
                routes.Add((rr.IsDBNull(0) ? null : rr.GetInt32(0), rr.IsDBNull(1) ? null : rr.GetInt32(1),
                            rr.IsDBNull(2) ? null : rr.GetInt32(2), rr.GetInt32(3)));
        }

        // En özelden genele
        int? Pick(Func<(int? Zone, int? Prod, int? Cat, int Reyon), bool> pred) =>
            routes.Where(pred).Select(x => (int?)x.Reyon).FirstOrDefault();

        return Pick(r => r.Prod == productId && r.Zone == zoneId)
            ?? Pick(r => r.Prod == productId && r.Zone == null)
            ?? Pick(r => r.Cat != null && categoryIds.Contains(r.Cat.Value) && r.Zone == zoneId)
            ?? Pick(r => r.Cat != null && categoryIds.Contains(r.Cat.Value) && r.Zone == null)
            ?? Pick(r => r.Prod == null && r.Cat == null && r.Zone == zoneId)
            ?? Pick(r => r.Prod == null && r.Cat == null && r.Zone == null);
    }
}
