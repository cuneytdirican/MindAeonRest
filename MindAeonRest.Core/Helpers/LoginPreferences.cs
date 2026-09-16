using System.IO;
using System.Text.Json;

namespace MindAeonRest.Helpers;

/// <summary>Son giriş bilgilerini (şirket/şube/kullanıcı) exe yanındaki login.json'da saklar.</summary>
public class LoginPreferences
{
    public string? LastCompanyCode { get; set; }
    public int? LastBranchId { get; set; }
    public string? LastUsername { get; set; }
    public string? Theme { get; set; }

    private const string FileName = "login.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static LoginPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LoginPreferences>(File.ReadAllText(FilePath)) ?? new LoginPreferences();
        }
        catch { /* bozuksa varsayılan */ }
        return new LoginPreferences();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options)); }
        catch { /* yazılamazsa sessiz geç */ }
    }
}
