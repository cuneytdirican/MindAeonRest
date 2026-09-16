using System.Security.Cryptography;

namespace MindAeonRest.Api.Auth;

/// <summary>JWT imza anahtarını GÜVENLİ çözer: yapılandırmadaki anahtar güçlüyse onu kullanır;
/// boş / kısa / "değiştir" placeholder'ı ise startup'ı bozmadan MAKİNEYE ÖZEL kalıcı güçlü bir anahtar
/// üretir (ProgramData) — böylece repodaki placeholder ile token sahteciliği yapılamaz.</summary>
public static class JwtKeyProvider
{
    private const string PlaceholderMarker = "degistir-bu-anahtari";
    private static string? _cached;
    private static readonly object _lock = new();

    public static string Resolve(IConfiguration config)
    {
        if (_cached is not null) return _cached;
        lock (_lock)
        {
            if (_cached is not null) return _cached;
            var configured = config["Jwt:Key"];
            if (!string.IsNullOrWhiteSpace(configured) && configured.Length >= 32 &&
                !configured.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
                return _cached = configured;   // güçlü ve özelleştirilmiş → kullan

            // Placeholder/zayıf → makineye özel kalıcı anahtar üret/oku (kök repoda saklanmaz)
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MindAeon", "keys");
            try { Directory.CreateDirectory(dir); } catch { }
            var file = Path.Combine(dir, "api-jwt.key");
            try
            {
                if (File.Exists(file))
                {
                    var existing = File.ReadAllText(file).Trim();
                    if (existing.Length >= 32) return _cached = existing;
                }
            }
            catch { }
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));   // 384-bit
            try { File.WriteAllText(file, key); } catch { }
            return _cached = key;
        }
    }
}
