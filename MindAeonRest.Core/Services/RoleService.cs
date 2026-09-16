using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class RoleService
{
    /// <summary>
    /// Yeniden adlandırılamaz / silinemez sistem (built-in) rolleri. Bu adlar korumalıdır:
    /// yeni rol bu adla oluşturulamaz ve mevcut bir rol bu ada/bu addan taşınamaz.
    /// Karşılaştırma büyük/küçük harf duyarsızdır; hem aksanlı ("Yönetici") hem aksansız ("Yonetici") yazım kapsanır.
    /// </summary>
    private static readonly HashSet<string> BuiltInRoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SysAdmin", "Yönetici", "Yonetici", "Kasiyer", "Mutfak", "Patron", "Garson"
    };

    /// <summary>Verilen ad korumalı bir sistem rolü mü? (trim + büyük/küçük harf duyarsız)</summary>
    public static bool IsBuiltInRoleName(string? name)
        => !string.IsNullOrWhiteSpace(name) && BuiltInRoleNames.Contains(name.Trim());

    public List<Role> GetAll(bool includeDeleted = false)
    {
        var roles = new List<Role>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, Name, Description, IsActive, CreatedAt, IsDeleted, CanUseAi
FROM dbo.Roles
WHERE (@inc = 1 OR IsDeleted = 0)
ORDER BY Name;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            roles.Add(new Role
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4),
                IsDeleted = reader.GetBoolean(5),
                CanUseAi = reader.GetBoolean(6)
            });
        }
        return roles;
    }

    /// <summary>Tek bir rolü Id ile getirir (yoksa null). Additive — web toggle'ında güncel değeri okumak için.</summary>
    public Role? GetById(int id, bool includeDeleted = false)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, Name, Description, IsActive, CreatedAt, IsDeleted, CanUseAi
FROM dbo.Roles
WHERE Id = @id AND (@inc = 1 OR IsDeleted = 0);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Role
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsActive = reader.GetBoolean(3),
            CreatedAt = reader.GetDateTime(4),
            IsDeleted = reader.GetBoolean(5),
            CanUseAi = reader.GetBoolean(6)
        };
    }

    /// <summary>Aynı ada sahip başka bir (silinmemiş) rol var mı? (trim + büyük/küçük harf duyarsız)</summary>
    public bool NameExists(string name, int excludeId = 0)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.Roles WHERE Name=@n AND IsDeleted=0 AND Id<>@ex;";
        command.Parameters.AddWithValue("@n", name.Trim());
        command.Parameters.AddWithValue("@ex", excludeId);
        return (int)command.ExecuteScalar() > 0;
    }

    /// <summary>
    /// Yeni rol oluşturur (sistem DB). Ad benzersiz olmalı (trim + büyük/küçük harf duyarsız),
    /// korumalı sistem rolü adı kullanılamaz. IsActive varsayılan açık, CanUseAi varsayılan kapalı.
    /// </summary>
    public Role Create(string name, string? description, int? byUserId)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Rol adı boş olamaz.");
        if (IsBuiltInRoleName(name))
            throw new InvalidOperationException($"'{name}' bir sistem rolüdür ve yeniden oluşturulamaz.");
        if (NameExists(name))
            throw new InvalidOperationException($"'{name}' adında bir rol zaten var.");

        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO dbo.Roles (Name, Description, IsActive, CanUseAi, CreatedBy)
OUTPUT INSERTED.Id, INSERTED.CreatedAt
VALUES (@Name, @Description, 1, 0, @By);";
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        reader.Read();
        int id = reader.GetInt32(0);
        DateTime createdAt = reader.GetDateTime(1);
        reader.Close();

        AuditService.Write("Role", id, "Insert", null,
            new { Name = name, Description = description }, byUserId);
        return new Role { Id = id, Name = name, Description = description, IsActive = true, CanUseAi = false, CreatedAt = createdAt };
    }

    /// <summary>
    /// Rolün adını/açıklamasını günceller (sistem DB).
    /// KORUMA: Korumalı sistem rolleri (SysAdmin, Yönetici, Kasiyer, Mutfak, Patron, Garson) YENİDEN ADLANDIRILAMAZ
    /// (yalnızca açıklamaları düzenlenebilir); ayrıca herhangi bir rol korumalı bir ada TAŞINAMAZ.
    /// Ad benzersiz olmalı (trim + büyük/küçük harf duyarsız).
    /// </summary>
    public void Update(int id, string name, string? description, int? byUserId)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Rol adı boş olamaz.");

        var current = GetById(id);
        if (current is null) throw new InvalidOperationException("Rol bulunamadı.");

        bool nameChanged = !string.Equals(current.Name.Trim(), name, StringComparison.OrdinalIgnoreCase);
        if (IsBuiltInRoleName(current.Name))
        {
            // Korumalı sistem rolü: adı değiştirilemez (sadece açıklama düzenlenebilir).
            if (nameChanged)
                throw new InvalidOperationException($"'{current.Name}' bir sistem rolüdür ve yeniden adlandırılamaz.");
        }
        else if (IsBuiltInRoleName(name))
        {
            // Kullanıcı rolü korumalı bir sistem adına taşınamaz.
            throw new InvalidOperationException($"'{name}' bir sistem rolü adıdır ve kullanılamaz.");
        }

        if (NameExists(name, id))
            throw new InvalidOperationException($"'{name}' adında bir rol zaten var.");

        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.Roles SET Name=@Name, Description=@Description, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
        command.Parameters.AddWithValue("@By", (object?)byUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Id", id);
        command.ExecuteNonQuery();

        AuditService.Write("Role", id, "Update",
            new { current.Name, current.Description },
            new { Name = name, Description = description }, byUserId);
    }

    /// <summary>Rolün AI Hub yetkisini açar/kapatır (sistem DB).</summary>
    public void SetAiPermission(int roleId, bool canUseAi, int? userId)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Roles SET CanUseAi=@a, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@a", canUseAi);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", roleId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rol adından AI yetkisini döndürür.</summary>
    public bool CanUseAi(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return false;
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(MAX(CASE WHEN CanUseAi=1 THEN 1 ELSE 0 END),0) FROM dbo.Roles WHERE Name=@n AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@n", roleName);
        return (int)cmd.ExecuteScalar()! == 1;
    }
}
