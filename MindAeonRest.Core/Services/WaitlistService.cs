using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Bekleme Listesi (şirket DB, <c>dbo.Waitlist</c>). Online rezervasyonda uygun masa kalmadığında
/// müşteri buraya alınır. Akış: ekle → çağır → geldi / iptal. Şube kapsamlıdır. EF YOK, ADO.NET.
/// Rezervasyon tablosundan BAĞIMSIZDIR (kendi tablosu); mevcut akışları etkilemez.</summary>
public class WaitlistService
{
    private const string SelectBase = @"
SELECT w.Id, w.BranchId, w.Name, w.Phone, w.PartySize, w.RequestedAt, w.Status, w.Note, w.IsDeleted
FROM dbo.Waitlist w";

    /// <summary>Back-office liste: şube geneli, opsiyonel tarih (talep günü) + durum + silinenler.</summary>
    public List<WaitlistEntry> GetForList(int branchId, DateTime? date = null, WaitlistStatus? status = null, bool includeDeleted = false)
    {
        var list = new List<WaitlistEntry>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase +
            @" WHERE w.BranchId=@b AND (@inc=1 OR w.IsDeleted=0)
               AND (@d IS NULL OR CAST(w.RequestedAt AS DATE)=@d)
               AND (@st IS NULL OR w.Status=@st)
               ORDER BY w.RequestedAt DESC;";
        command.Parameters.AddWithValue("@b", branchId);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        command.Parameters.AddWithValue("@d", (object?)date?.Date ?? DBNull.Value);
        command.Parameters.AddWithValue("@st", status is null ? DBNull.Value : (int)status.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public WaitlistEntry? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE w.Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Bekleyen sayısı (şube, Status=Bekliyor) — public/back-office bilgilendirme için.</summary>
    public int CountWaiting(int branchId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Waitlist WHERE BranchId=@b AND IsDeleted=0 AND Status=0;";
        command.Parameters.AddWithValue("@b", branchId);
        return (int)command.ExecuteScalar()!;
    }

    /// <summary>Bekleme listesine ekler (Status=Bekliyor). RequestedAt verilmezse şimdi (UTC). Geriye sıradaki kaydı döner.</summary>
    public WaitlistEntry Add(WaitlistEntry e, int? userId)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new ArgumentException("Ad Soyad boş olamaz.");
        if (e.PartySize <= 0) throw new ArgumentException("Kişi sayısı geçersiz.");
        if (e.BranchId <= 0) throw new ArgumentException("Şube bulunamadı.");
        if (e.RequestedAt == default) e.RequestedAt = DateTime.UtcNow;

        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO dbo.Waitlist (BranchId, Name, Phone, PartySize, RequestedAt, Status, Note, CreatedBy)
OUTPUT INSERTED.Id VALUES (@b,@n,@p,@party,@req,@st,@note,@by);";
        command.Parameters.AddWithValue("@b", e.BranchId);
        command.Parameters.AddWithValue("@n", e.Name.Trim());
        command.Parameters.AddWithValue("@p", (object?)e.Phone ?? DBNull.Value);
        command.Parameters.AddWithValue("@party", e.PartySize);
        command.Parameters.AddWithValue("@req", e.RequestedAt);
        command.Parameters.AddWithValue("@st", (int)e.Status);
        command.Parameters.AddWithValue("@note", (object?)e.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        e.Id = (int)command.ExecuteScalar()!;
        return e;
    }

    /// <summary>Durum değiştirir (çağır=Cagrildi / geldi=Geldi / iptal=Iptal).</summary>
    public void SetStatus(int id, WaitlistStatus status, int? userId)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Waitlist SET Status=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        command.Parameters.AddWithValue("@s", (int)status);
        command.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private static WaitlistEntry Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        BranchId = r.GetInt32(1),
        Name = r.GetString(2),
        Phone = r.IsDBNull(3) ? null : r.GetString(3),
        PartySize = r.GetInt32(4),
        RequestedAt = r.GetDateTime(5),
        Status = (WaitlistStatus)r.GetInt32(6),
        Note = r.IsDBNull(7) ? null : r.GetString(7),
        IsDeleted = r.GetBoolean(8)
    };
}
