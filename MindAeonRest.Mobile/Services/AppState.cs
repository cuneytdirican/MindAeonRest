using MindAeonRest.Mobile.Models;

namespace MindAeonRest.Mobile.Services;

/// <summary>Cihazda saklanan API adresi + oturum (token) + giriş bilgisi.
/// API adresi Preferences'ta, token SecureStorage'da tutulur; "kayıtlı API üzerinden haberleşme".</summary>
public class AppState
{
    private const string KeyApiUrl = "api_base_url";
    private const string KeyToken = "auth_token";

    /// <summary>Bulut merkez API adresi. Preferences'ta elle LAN override yoksa bu kullanılır.</summary>
    public const string DefaultApiUrl = "https://mindaeonrestapi.mobilteg.com";

    private string? _apiBaseUrl;
    /// <summary>Kayıtlı (LAN override) adres yoksa bulut merkez FQDN varsayılanı döner.</summary>
    public string? ApiBaseUrl => string.IsNullOrWhiteSpace(_apiBaseUrl) ? DefaultApiUrl : _apiBaseUrl;

    /// <summary>Kullanıcının elle kaydettiği override (yoksa null). ConfigPage'te göstermek için.</summary>
    public string? ApiBaseUrlOverride => _apiBaseUrl;

    public string? Token { get; private set; }
    public LoginResponse? Login { get; set; }

    /// <summary>Şirket V2 garson terminalinde mi? Girişten sonra UiSettings ile doldurulur (varsayılan false).</summary>
    public bool AdisyonV2 { get; set; }

    // Bulut merkez varsayılanı daima var → uygulama ilk açılışta doğrudan LoginPage'e gidebilir.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBaseUrl);
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Token);

    public async Task LoadAsync()
    {
        _apiBaseUrl = Preferences.Get(KeyApiUrl, null);
        try { Token = await SecureStorage.GetAsync(KeyToken); } catch { Token = null; }
    }

    /// <summary>Elle LAN override kaydeder. Boş verilirse override kaldırılır (bulut merkeze döner).</summary>
    public void SaveApiUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _apiBaseUrl = null;
            Preferences.Remove(KeyApiUrl);
            return;
        }
        _apiBaseUrl = NormalizeUrl(url);
        Preferences.Set(KeyApiUrl, _apiBaseUrl);
    }

    public async Task SaveTokenAsync(string? token)
    {
        Token = token;
        try
        {
            if (string.IsNullOrEmpty(token)) SecureStorage.Remove(KeyToken);
            else await SecureStorage.SetAsync(KeyToken, token);
        }
        catch { /* bazı platformlarda SecureStorage yoksa bellekte kalır */ }
    }

    public async Task LogoutAsync()
    {
        Login = null;
        await SaveTokenAsync(null);
    }

    /// <summary>"192.168.1.50:5000" gibi girilse bile http:// ve sonda / olmadan normalize eder.</summary>
    public static string NormalizeUrl(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return raw;
        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = "http://" + raw;
        return raw.TrimEnd('/');
    }
}
