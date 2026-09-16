using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Masa servis istekleri (çağrı / masa temizliği / hesap) — şirket DB.</summary>
public class ServiceRequestService
{
    private const string SelectBase = @"
SELECT s.Id, s.BranchId, s.TableId, t.Code AS TableCode, s.ZoneId, z.Name AS ZoneName,
       s.OrderId, s.Type, s.Status, s.Note, s.KioskNo, s.CompletedAt, s.CreatedAt
FROM dbo.ServiceRequests s
LEFT JOIN dbo.Tables t ON t.Id = s.TableId
LEFT JOIN dbo.Zones z ON z.Id = s.ZoneId";

    /// <summary>Şubedeki bekleyen (tamamlanmamış) servis istekleri.</summary>
    public List<ServiceRequest> GetActive(int branchId, ServiceRequestType? type = null)
    {
        var list = new List<ServiceRequest>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase +
            " WHERE s.IsDeleted=0 AND s.BranchId=@b AND s.Status=0 AND (@t IS NULL OR s.Type=@t) ORDER BY s.CreatedAt;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@t", (object?)(type.HasValue ? (int)type.Value : null) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    /// <summary>İstek oluşturur. Aynı masa+tip için zaten bekleyen varsa onu döndürür (mükerrer önleme).</summary>
    public ServiceRequest Create(ServiceRequest req, int? userId)
    {
        using var c = Database.OpenCompany();
        using (var dup = c.CreateCommand())
        {
            dup.CommandText = "SELECT Id FROM dbo.ServiceRequests WHERE IsDeleted=0 AND Status=0 AND BranchId=@b AND Type=@t AND ISNULL(TableId,0)=ISNULL(@tab,0);";
            dup.Parameters.AddWithValue("@b", req.BranchId);
            dup.Parameters.AddWithValue("@t", (int)req.Type);
            dup.Parameters.AddWithValue("@tab", (object?)req.TableId ?? DBNull.Value);
            if (dup.ExecuteScalar() is int existingId) { req.Id = existingId; return req; }
        }
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.ServiceRequests (BranchId, TableId, ZoneId, OrderId, Type, Status, Note, KioskNo, CreatedBy)
OUTPUT INSERTED.Id VALUES (@b,@tab,@z,@o,@t,0,@n,@k,@by);";
        cmd.Parameters.AddWithValue("@b", req.BranchId);
        cmd.Parameters.AddWithValue("@tab", (object?)req.TableId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@z", (object?)req.ZoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", (object?)req.OrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", (int)req.Type);
        cmd.Parameters.AddWithValue("@n", (object?)req.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@k", req.KioskNo <= 0 ? 1 : req.KioskNo);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        req.Id = (int)cmd.ExecuteScalar()!;
        return req;
    }

    /// <summary>İsteğin şubesi (IDOR kontrolü). Yoksa null.</summary>
    public int? GetBranch(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BranchId FROM dbo.ServiceRequests WHERE Id=@id AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@id", id);
        var o = cmd.ExecuteScalar();
        return o is null || o == DBNull.Value ? null : Convert.ToInt32(o);
    }

    /// <summary>İsteği tamamlandı işaretler.</summary>
    public void Complete(int id, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.ServiceRequests SET Status=1, CompletedAt=SYSUTCDATETIME(), UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static ServiceRequest Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        BranchId = r.GetInt32(1),
        TableId = r.IsDBNull(2) ? null : r.GetInt32(2),
        TableCode = r.IsDBNull(3) ? null : r.GetString(3),
        ZoneId = r.IsDBNull(4) ? null : r.GetInt32(4),
        ZoneName = r.IsDBNull(5) ? null : r.GetString(5),
        OrderId = r.IsDBNull(6) ? null : r.GetInt32(6),
        Type = (ServiceRequestType)r.GetInt32(7),
        Status = (ServiceRequestStatus)r.GetInt32(8),
        Note = r.IsDBNull(9) ? null : r.GetString(9),
        KioskNo = r.GetInt32(10),
        CompletedAt = r.IsDBNull(11) ? null : r.GetDateTime(11),
        CreatedAt = r.GetDateTime(12)
    };
}
