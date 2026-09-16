using System.Text.Json;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;

namespace MindAeonRest.Services;

/// <summary>
/// Merkezî denetim (audit) yazıcısı — POS/şirket tarafının EF'siz (ham ADO.NET) karşılığı.
/// Lisans tarafındaki <c>AuditLog</c> + <c>AuditSaveChangesInterceptor</c> desenini uyarlar:
/// before/after JSON anlık görüntüsü, kim/ne-zaman/hangi terminal (KIOSK)/şube bağlamı.
///
/// TASARIM:
///  - Bağlam <see cref="Session"/>'dan okunur (CurrentUser/KioskNo/Branch) — çağıran taraf ekstra bilgi vermek zorunda değil.
///  - FAIL-SAFE: audit yazımı asıl işlemi ASLA bozmaz. Tüm gövde try/catch ile sarılıdır; hata yutulur + Debug'a loglanır.
///  - Yalnızca INSERT + okuma; kayıtlar hiç güncellenmez/silinmez (dbo.AuditLogs'ta soft-delete kolonu yoktur).
///  - Yüksek-değerli (para/tanım) işlemlere bağlanması amaçlanır; her CRUD'a değil.
/// </summary>
public static class AuditService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        // Türkçe karakter/₺ gibi işaretler \uXXXX kaçışı olmadan okunur kalsın (log görüntüleme için).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Enum'lar sayı yerine ad olarak yazılsın (audit okunabilirliği).
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Denetim kaydı yazar (best-effort). Bağlam Session'dan alınır; before/after nesneleri System.Text.Json ile serileşir.
    /// </summary>
    /// <param name="entityType">İş varlığı adı (ör. "Product", "Campaign", "Shift", "OrderLineRefund", "User").</param>
    /// <param name="entityId">Varlık Id'si (0 → NULL yazılır).</param>
    /// <param name="action">"Insert" / "Update" / "Delete" (ya da özel: "Refund", "CloseShift"...).</param>
    /// <param name="before">Değişiklik öncesi durum (yoksa null — ör. Insert).</param>
    /// <param name="after">Değişiklik sonrası durum (yoksa null — ör. Delete).</param>
    /// <param name="userId">Açıkça verilen kullanıcı (null → Session.CurrentUserId).</param>
    /// <param name="ip">İstemci IP'si (web tarafında verilebilir; WPF'te null).</param>
    public static void Write(string entityType, int entityId, string action,
        object? before, object? after, int? userId = null, string? ip = null)
    {
        try
        {
            var uid = userId ?? Session.CurrentUserId;
            var userName = Session.CurrentUser?.FullName ?? Session.CurrentUser?.Username;
            int? branchId = Session.CurrentBranch?.Id;
            int kioskNo = Session.CurrentKioskNo;

            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.AuditLogs (UserId, UserName, Action, EntityType, EntityId, BeforeJson, AfterJson, KioskNo, BranchId, Ip, CreatedAt)
VALUES (@uid, @uname, @act, @etype, @eid, @before, @after, @kno, @bid, @ip, SYSUTCDATETIME());";
            cmd.Parameters.AddWithValue("@uid", (object?)uid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@uname", (object?)userName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@act", action ?? "");
            cmd.Parameters.AddWithValue("@etype", entityType ?? "");
            cmd.Parameters.AddWithValue("@eid", entityId == 0 ? (object)DBNull.Value : entityId);
            cmd.Parameters.AddWithValue("@before", (object?)Serialize(before) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@after", (object?)Serialize(after) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kno", kioskNo);
            cmd.Parameters.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // Audit ASLA asıl işlemi bozmaz. Hatayı yut + izini bırak.
            System.Diagnostics.Debug.WriteLine($"[AuditService] Denetim kaydı yazılamadı ({entityType}/{entityId}/{action}): {ex.Message}");
        }
    }

    private static string? Serialize(object? o)
    {
        if (o is null) return null;
        try { return JsonSerializer.Serialize(o, JsonOpts); }
        catch { return null; }   // serileşemeyen nesne audit'i bozmasın
    }

    // ==================== Okuma (BackOffice görüntüleme) ====================

    /// <summary>Denetim izini filtreli listeler (tarih + kullanıcı + entityType). En yeni önce. Yalnız SysAdmin çağırmalı.</summary>
    public static List<AuditLogEntry> Query(DateTime? fromLocal, DateTime? toLocal, int? userId, string? entityType, int take = 500)
    {
        var list = new List<AuditLogEntry>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@take) Id, UserId, UserName, Action, EntityType, EntityId, BeforeJson, AfterJson, KioskNo, BranchId, Ip, CreatedAt
FROM dbo.AuditLogs
WHERE (@from  IS NULL OR CreatedAt >= @from)
  AND (@to    IS NULL OR CreatedAt <  @to)
  AND (@uid   IS NULL OR UserId = @uid)
  AND (@etype IS NULL OR EntityType = @etype)
ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@take", take <= 0 ? 500 : take);
        // CreatedAt UTC saklanır (SYSUTCDATETIME). Yerel gün sınırlarını UTC'ye çevir ki yerel günün ilk saatleri
        // (UTC ofseti kadar) yanlış güne düşüp filtreden kaybolmasın — grid ToLocalTime gösterimiyle tutarlı.
        cmd.Parameters.AddWithValue("@from", (object?)fromLocal?.Date.ToUniversalTime() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)toLocal?.Date.AddDays(1).ToUniversalTime() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uid", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@etype", string.IsNullOrWhiteSpace(entityType) ? DBNull.Value : entityType);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditLogEntry
            {
                Id = r.GetInt64(0),
                UserId = r.IsDBNull(1) ? null : r.GetInt32(1),
                UserName = r.IsDBNull(2) ? null : r.GetString(2),
                Action = r.GetString(3),
                EntityType = r.GetString(4),
                EntityId = r.IsDBNull(5) ? null : r.GetInt32(5),
                BeforeJson = r.IsDBNull(6) ? null : r.GetString(6),
                AfterJson = r.IsDBNull(7) ? null : r.GetString(7),
                KioskNo = r.GetInt32(8),
                BranchId = r.IsDBNull(9) ? null : r.GetInt32(9),
                Ip = r.IsDBNull(10) ? null : r.GetString(10),
                CreatedAt = r.GetDateTime(11)
            });
        return list;
    }

    /// <summary>Filtre açılır listesi için mevcut entityType değerleri (alfabetik).</summary>
    public static List<string> DistinctEntityTypes()
    {
        var list = new List<string>();
        try
        {
            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT EntityType FROM dbo.AuditLogs ORDER BY EntityType;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AuditService] DistinctEntityTypes: {ex.Message}"); }
        return list;
    }
}

/// <summary>Denetim kaydı okuma modeli (BackOffice görüntüleme).</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public int? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public int KioskNo { get; set; }
    public int? BranchId { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; }
}
