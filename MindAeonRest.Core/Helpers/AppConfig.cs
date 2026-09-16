using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MindAeonRest.Helpers;

/// <summary>
/// Veritabanı bağlantı bilgilerini exe ile aynı dizindeki <c>appsettings.json</c>
/// dosyasından okur. Dosya yoksa varsayılan değerlerle oluşturur.
/// </summary>
public static class AppConfig
{
    private const string FileName = "appsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static AppSettings? _settings;

    /// <summary>Config dosyasının tam yolu (exe çalışma dizini).</summary>
    public static string ConfigFilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Yüklü veritabanı ayarları (ilk erişimde dosyadan okunur).</summary>
    public static DatabaseSettings Settings => (_settings ??= Load()).Database;

    public static string DatabaseName => Settings.DatabaseName;

    /// <summary>Bu terminalin (kurulumun) KIOSK numarası — appsettings.json "Kiosk:No". Değiştirilemez kimlik.</summary>
    public static int KioskNo => (_settings ??= Load()).Kiosk?.No ?? 1;

    /// <summary>Hedef veritabanına (MindAeonRest) bağlantı dizesi.</summary>
    public static string ConnectionString => BuildConnectionString(Settings.DatabaseName);

    /// <summary>Veritabanı oluşturma işlemleri için master veritabanına bağlantı dizesi.</summary>
    public static string MasterConnectionString => BuildConnectionString("master");

    /// <summary>Belirtilen veritabanı için bağlantı dizesi (ör. şirket DB'leri MindAeonRest_{Kod}).</summary>
    public static string GetConnectionString(string databaseName) => BuildConnectionString(databaseName);

    /// <summary>Lisans portalı API'sinin GÖMÜLÜ (varsayılan) adresi — müşteri girmez/bilmez.</summary>
    public const string DefaultLicenceApiUrl = "https://mindaeonrestlicenceapi.mobilteg.com";

    /// <summary>Lisans portalı API adresi. appsettings'te override yoksa gömülü varsayılan kullanılır → online her zaman çalışır.</summary>
    public static string LicenceApiUrl
    {
        get
        {
            var u = ((_settings ??= Load()).Licence?.ApiUrl ?? "").Trim().TrimEnd('/');
            return string.IsNullOrWhiteSpace(u) ? DefaultLicenceApiUrl : u;
        }
    }

    private static AppSettings Load()
    {
        string path = ConfigFilePath;

        // Dosya yoksa varsayılanlarla oluştur
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch
        {
            // Dosya bozuksa varsayılanlara düş (üzerine yazma)
            return new AppSettings();
        }
    }

    private static void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // Yazılamazsa sessizce geç; varsayılan değerlerle çalışmaya devam edilir
        }
    }

    private static string BuildConnectionString(string database)
    {
        var s = Settings;
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = s.Server,
            UserID = s.UserId,
            Password = s.Password,
            InitialCatalog = database,
            Encrypt = s.Encrypt,
            TrustServerCertificate = s.TrustServerCertificate,
            ConnectTimeout = s.ConnectTimeout,
            ApplicationName = "MindAeonRest"
        };
        return builder.ConnectionString;
    }
}
