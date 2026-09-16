using Microsoft.Data.SqlClient;
using MindAeonRest.Data;

namespace MindAeonRest.Services;

/// <summary>Kullanıcı-rol (çoklu) ilişkisi (sistem DB).</summary>
public class UserRoleService
{
    /// <summary>Tek başına olması gereken (başka rolle birleşmeyen) roller.</summary>
    public static readonly string[] ExclusiveRoles = { "SysAdmin", "Mutfak" };

    public List<int> GetRoleIds(int userId)
    {
        var ids = new List<int>();
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT RoleId FROM dbo.UserRoles WHERE UserId=@u;";
        cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt32(0));
        return ids;
    }

    public List<string> GetRoleNames(int userId)
    {
        var names = new List<string>();
        using var c = Database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT r.Name FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId WHERE ur.UserId=@u AND r.IsDeleted=0 ORDER BY r.Name;";
        cmd.Parameters.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    /// <summary>Kullanıcının rollerini tümüyle değiştirir (tek transaction). Aynı bağlantı/tx verilebilir.</summary>
    public void SetRoles(int userId, IEnumerable<int> roleIds, SqlConnection c, SqlTransaction? tx)
    {
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM dbo.UserRoles WHERE UserId=@u;";
            del.Parameters.AddWithValue("@u", userId);
            del.ExecuteNonQuery();
        }
        foreach (int rid in roleIds.Distinct())
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@u,@r);";
            ins.Parameters.AddWithValue("@u", userId);
            ins.Parameters.AddWithValue("@r", rid);
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>Seçilen rol adlarını mantık kurallarına göre doğrular; hata mesajı döner (geçerliyse null).</summary>
    public static string? ValidateRoleSet(IReadOnlyCollection<string> roleNames)
    {
        if (roleNames.Count == 0) return "En az bir rol seçiniz.";
        foreach (var ex in ExclusiveRoles)
            if (roleNames.Contains(ex) && roleNames.Count > 1)
                return $"'{ex}' rolü tek başına atanır; başka rolle birleştirilemez. (Örn. Mutfak hem Garson olamaz, Admin başka rol alamaz.)";
        return null;
    }
}
