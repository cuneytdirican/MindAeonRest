using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

public class AuthService
{
    public enum AuthResult
    {
        Success,
        InvalidCredentials,
        UserInactive
    }

    /// <summary>Yalnızca kullanıcı adı + parola doğrular (şirket/şube kontrolü LoginService'te).</summary>
    public AuthResult Authenticate(string username, string password, out User? user)
    {
        user = null;

        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT u.Id, u.Username, u.PasswordHash, u.FullName, u.RoleId, r.Name AS RoleName,
       u.IsActive, u.CreatedAt, u.LastLoginAt,
       ISNULL(u.MustChangePassword, 0) AS MustChangePassword
FROM dbo.Users u
INNER JOIN dbo.Roles r ON r.Id = u.RoleId
WHERE u.Username = @Username AND u.IsDeleted = 0;";
        command.Parameters.AddWithValue("@Username", username);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return AuthResult.InvalidCredentials;

        var found = new User
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            FullName = reader.GetString(3),
            RoleId = reader.GetInt32(4),
            RoleName = reader.GetString(5),
            IsActive = reader.GetBoolean(6),
            CreatedAt = reader.GetDateTime(7),
            LastLoginAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            MustChangePassword = !reader.IsDBNull(9) && reader.GetBoolean(9)
        };

        if (!PasswordHasher.Verify(password, found.PasswordHash))
            return AuthResult.InvalidCredentials;

        if (!found.IsActive)
            return AuthResult.UserInactive;

        user = found;
        return AuthResult.Success;
    }

    public void UpdateLastLogin(int userId)
    {
        using var connection = Database.OpenConnection();
        UpdateLastLogin(connection, userId);
    }

    private static void UpdateLastLogin(SqlConnection connection, int userId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Users SET LastLoginAt = SYSUTCDATETIME() WHERE Id = @Id;";
        command.Parameters.AddWithValue("@Id", userId);
        command.ExecuteNonQuery();
    }
}
