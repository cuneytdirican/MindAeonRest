using MindAeonRest.Data;

namespace MindAeonRest.Services;

/// <summary>Kullanıcıların şirket/şube erişim yetkilerini yönetir (SysAdmin hariç).</summary>
public class UserAccessService
{
    /// <summary>Kullanıcının belirtilen şirket/şubeye erişimi var mı? (BranchId NULL = tüm şubeler)</summary>
    public bool HasAccess(int userId, int companyId, int branchId)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COUNT(1) FROM dbo.UserCompanyAccess
WHERE IsDeleted = 0 AND UserId = @UserId AND CompanyId = @CompanyId
      AND (BranchId IS NULL OR BranchId = @BranchId);";
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@CompanyId", companyId);
        command.Parameters.AddWithValue("@BranchId", branchId);
        return (int)command.ExecuteScalar() > 0;
    }

    /// <summary>Kullanıcının bir şirketteki erişimi: tüm şubeler mi, yoksa hangi şube id'leri.</summary>
    public (bool AllBranches, List<int> BranchIds) GetAccess(int userId, int companyId)
    {
        bool all = false;
        var ids = new List<int>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT BranchId FROM dbo.UserCompanyAccess
WHERE IsDeleted = 0 AND UserId = @UserId AND CompanyId = @CompanyId;";
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@CompanyId", companyId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) all = true;
            else ids.Add(reader.GetInt32(0));
        }
        return (all, ids);
    }

    /// <summary>
    /// Kullanıcının bir şirketteki şube erişimini topluca ayarlar.
    /// Mevcut erişimler soft-delete edilir (silme yok), yeni seçim eklenir.
    /// allBranches=true ise tek bir "tüm şubeler" (BranchId NULL) kaydı oluşturulur.
    /// </summary>
    public void SetBranchAccess(int userId, int companyId, bool allBranches, IEnumerable<int> branchIds, int? updatedByUserId)
    {
        using var connection = Database.OpenConnection();

        using (var del = connection.CreateCommand())
        {
            del.CommandText = @"
UPDATE dbo.UserCompanyAccess SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE IsDeleted=0 AND UserId=@UserId AND CompanyId=@CompanyId;";
            del.Parameters.AddWithValue("@By", (object?)updatedByUserId ?? DBNull.Value);
            del.Parameters.AddWithValue("@UserId", userId);
            del.Parameters.AddWithValue("@CompanyId", companyId);
            del.ExecuteNonQuery();
        }

        void Insert(int? branchId)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.UserCompanyAccess (UserId, CompanyId, BranchId, CreatedBy)
VALUES (@UserId, @CompanyId, @BranchId, @By);";
            ins.Parameters.AddWithValue("@UserId", userId);
            ins.Parameters.AddWithValue("@CompanyId", companyId);
            ins.Parameters.AddWithValue("@BranchId", (object?)branchId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@By", (object?)updatedByUserId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }

        if (allBranches)
        {
            Insert(null);
        }
        else
        {
            foreach (int id in branchIds.Distinct())
                Insert(id);
        }
    }

    /// <summary>Kullanıcının erişimi olan (silinmemiş) şirket Id'leri (cross-tenant yönetim için).</summary>
    public List<int> GetCompanyIdsForUser(int userId)
    {
        var ids = new List<int>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT DISTINCT CompanyId FROM dbo.UserCompanyAccess
WHERE IsDeleted = 0 AND UserId = @UserId
ORDER BY CompanyId;";
        command.Parameters.AddWithValue("@UserId", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    /// <summary>
    /// Kullanıcıya bir şirkete erişim verir (idempotent UPSERT). BranchId NULL kayıt = "tüm şubeler".
    ///  - allBranches=true → tüm-şubeler (BranchId NULL) kaydı garanti edilir; gereksiz tekil şube
    ///    kayıtları soft-delete edilir (tümü zaten kapsandığı için).
    ///  - allBranches=false → tüm-şubeler kaydı varsa kaldırılır (kısıtlı erişime düşürme). Tekil şube
    ///    seçimleri <see cref="SetBranchAccess"/> ile yönetilir; mevcut tekil kayıtlar korunur.
    /// Aynı çağrı defalarca yapılabilir (yinelenen kayıt üretmez).
    /// </summary>
    public void GrantCompany(int userId, int companyId, bool allBranches, int? byUserId)
    {
        using var connection = Database.OpenConnection();

        // Bu kullanıcı+şirket için mevcut (silinmemiş) tüm-şubeler (NULL) kaydı var mı?
        bool hasActiveAllRow;
        using (var chk = connection.CreateCommand())
        {
            chk.CommandText = @"
SELECT COUNT(1) FROM dbo.UserCompanyAccess
WHERE IsDeleted = 0 AND UserId = @UserId AND CompanyId = @CompanyId AND BranchId IS NULL;";
            chk.Parameters.AddWithValue("@UserId", userId);
            chk.Parameters.AddWithValue("@CompanyId", companyId);
            hasActiveAllRow = (int)chk.ExecuteScalar() > 0;
        }

        if (allBranches)
        {
            // Tekil şube kayıtları artık gereksiz → soft-delete (SetBranchAccess deseni).
            using (var del = connection.CreateCommand())
            {
                del.CommandText = @"
UPDATE dbo.UserCompanyAccess SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE IsDeleted=0 AND UserId=@UserId AND CompanyId=@CompanyId AND BranchId IS NOT NULL;";
                del.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
                del.Parameters.AddWithValue("@UserId", userId);
                del.Parameters.AddWithValue("@CompanyId", companyId);
                del.ExecuteNonQuery();
            }
            // Tüm-şubeler kaydı yoksa ekle (idempotent).
            if (!hasActiveAllRow)
            {
                using var ins = connection.CreateCommand();
                ins.CommandText = @"
INSERT INTO dbo.UserCompanyAccess (UserId, CompanyId, BranchId, CreatedBy)
VALUES (@UserId, @CompanyId, NULL, @By);";
                ins.Parameters.AddWithValue("@UserId", userId);
                ins.Parameters.AddWithValue("@CompanyId", companyId);
                ins.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
        }
        else
        {
            // Kısıtlı erişime düşür: varsa tüm-şubeler kaydını kaldır (tekil kayıtlara dokunma).
            if (hasActiveAllRow)
            {
                using var del = connection.CreateCommand();
                del.CommandText = @"
UPDATE dbo.UserCompanyAccess SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE IsDeleted=0 AND UserId=@UserId AND CompanyId=@CompanyId AND BranchId IS NULL;";
                del.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
                del.Parameters.AddWithValue("@UserId", userId);
                del.Parameters.AddWithValue("@CompanyId", companyId);
                del.ExecuteNonQuery();
            }
        }

        // AUDIT (best-effort): şirket erişimi verme/ayarlama.
        AuditService.Write("UserCompanyAccess", userId, "GrantCompany", null,
            new { UserId = userId, CompanyId = companyId, AllBranches = allBranches }, byUserId);
    }

    /// <summary>
    /// Kullanıcının bir şirkete erişimini tümüyle kaldırır: o şirkete ait TÜM erişim kayıtları
    /// (tüm-şubeler NULL kaydı + tekil şube kayıtları) soft-delete edilir. İdempotenttir.
    /// </summary>
    public void RevokeCompany(int userId, int companyId, int? byUserId)
    {
        using var connection = Database.OpenConnection();
        using (var del = connection.CreateCommand())
        {
            del.CommandText = @"
UPDATE dbo.UserCompanyAccess SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE IsDeleted=0 AND UserId=@UserId AND CompanyId=@CompanyId;";
            del.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
            del.Parameters.AddWithValue("@UserId", userId);
            del.Parameters.AddWithValue("@CompanyId", companyId);
            del.ExecuteNonQuery();
        }
        // AUDIT (best-effort): şirket erişimini kaldırma.
        AuditService.Write("UserCompanyAccess", userId, "RevokeCompany", null,
            new { UserId = userId, CompanyId = companyId }, byUserId);
    }
}
