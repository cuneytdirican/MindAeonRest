using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace MindAeonRest.Helpers;

/// <summary>Mobil API sunucusunu (MindAeonRest.Api.exe) yönetir: durum/başlat/durdur + LAN IP.
/// Karar gereği API ayrı süreç; WPF açılışta yedek olarak başlatabilir, panel elle kontrol eder.</summary>
public static class MobileServer
{
    public const int Port = 5000;
    private const string ExeName = "MindAeonRest.Api.exe";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private static string PathFile => Path.Combine(AppContext.BaseDirectory, "mobileapi.path");

    /// <summary>Kullanıcının "Gözat" ile seçtiği API exe yolu (kalıcı). Yoksa null.</summary>
    public static string? CustomExePath
    {
        get { try { return File.Exists(PathFile) ? File.ReadAllText(PathFile).Trim() : null; } catch { return null; } }
        set
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) { if (File.Exists(PathFile)) File.Delete(PathFile); }
                else File.WriteAllText(PathFile, value);
            }
            catch { }
        }
    }

    /// <summary>API exe'sini bulur: önce kullanıcı seçimi, sonra exe yanı (dağıtım), sonra geliştirme yolu.</summary>
    public static string? FindExe()
    {
        var custom = CustomExePath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        string near = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(near)) return near;
        // Geliştirme: ...\MindAeonRest\bin\Debug\net9.0-windows\ -> ...\MindAeonRest.Api\bin\Debug\net9.0-windows\
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? cfgTfm = dir.Name;                    // net9.0-windows
            string? cfg = dir.Parent?.Name;               // Debug
            string? root = dir.Parent?.Parent?.Parent?.Parent?.FullName; // çözüm kökü
            if (root is not null && cfg is not null)
            {
                string dev = Path.Combine(root, "MindAeonRest.Api", "bin", cfg, cfgTfm, ExeName);
                if (File.Exists(dev)) return dev;
            }
        }
        catch { }
        return null;
    }

    public static async Task<bool> IsRunningAsync()
    {
        try { var r = await _http.GetAsync($"http://localhost:{Port}/api/ping"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public static bool ProcessExists() =>
        Process.GetProcessesByName("MindAeonRest.Api").Length > 0;

    public static (bool ok, string message) Start()
    {
        if (ProcessExists()) return (true, "Zaten çalışıyor.");
        var exe = FindExe();
        if (exe is null) return (false, $"{ExeName} bulunamadı. API projesini derleyin veya exe yanına koyun.");
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return (true, "Başlatıldı.");
        }
        catch (Exception ex) { return (false, "Başlatılamadı: " + ex.Message); }
    }

    public static void Stop()
    {
        foreach (var p in Process.GetProcessesByName("MindAeonRest.Api"))
            try { p.Kill(true); } catch { }
    }

    // ===== Otomatik kurulum: API'yi indir + PC ayarlarına göre yapılandır =====
    public const string DownloadUrl = "https://www.mobilteg.com.tr/downloads/MindAeonRest/API.rar";
    /// <summary>Garson PWA (statik tablet/tarayıcı arayüzü) paketi.</summary>
    public const string PwaDownloadUrl = "https://www.mobilteg.com.tr/downloads/MindAeonRest/PWA.rar";

    /// <summary>API'nin kurulu olacağı klasör (WPF exe'sinin yanında).</summary>
    public static string InstallDir => Path.Combine(AppContext.BaseDirectory, "MobilSunucu");

    /// <summary>API kurulu mu (bulunabiliyor mu).</summary>
    public static bool IsInstalled() => FindExe() is not null;

    /// <summary>Kurulu API'nin sürümü (exe dosya sürümü). Yoksa null.</summary>
    public static string? InstalledVersion()
    {
        try { var exe = FindExe(); return exe is null ? null : FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0]; }
        catch { return null; }
    }

    /// <summary>API.rar'ı indirir, açar, appsettings.json'u BU PC'nin SQL/Kiosk ayarlarına göre yazar, yolu kaydeder.
    /// preserveSettings=true ise (GÜNCELLEME) mevcut appsettings.json'daki ayarlar korunur (yeni sürüm üzerine yedirilir).</summary>
    public static async Task<(bool ok, string message)> DownloadAndInstallAsync(Action<string>? progress = null, bool preserveSettings = false)
    {
        try
        {
            // GÜNCELLEME: mevcut appsettings.json'u (kullanıcı ayarları) extract'tan ÖNCE yedekle
            JsonObject? preserved = null;
            if (preserveSettings)
            {
                try
                {
                    var oldExe = FindExe();
                    var oldCfg = oldExe is null ? null : Path.Combine(Path.GetDirectoryName(oldExe)!, "appsettings.json");
                    if (oldCfg is not null && File.Exists(oldCfg))
                        preserved = JsonNode.Parse(File.ReadAllText(oldCfg)) as JsonObject;
                }
                catch { }
            }

            progress?.Invoke("API indiriliyor…");
            string tmp = Path.Combine(Path.GetTempPath(), "MindAeonRest.Api.rar");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            using (var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs);
            }

            progress?.Invoke("Arşiv açılıyor…");
            Directory.CreateDirectory(InstallDir);
            using (var archive = ArchiveFactory.Open(tmp))
            {
                var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    entry.WriteToDirectory(InstallDir, opts);
            }
            try { File.Delete(tmp); } catch { }

            var exe = Directory.GetFiles(InstallDir, ExeName, SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null) return (false, "Arşiv açıldı ama MindAeonRest.Api.exe bulunamadı.");

            progress?.Invoke("Ayarlar yapılandırılıyor…");
            ConfigureAppSettings(Path.Combine(Path.GetDirectoryName(exe)!, "appsettings.json"), preserved);

            CustomExePath = exe;   // panel/servis bu yolu kullanır
            return (true, preserveSettings
                ? "API yeni sürüme güncellendi; mevcut ayarlar korundu."
                : "API indirildi, bu PC'ye göre yapılandırıldı ve kuruldu.");
        }
        catch (Exception ex) { return (false, "Kurulum hatası: " + ex.Message); }
    }

    /// <summary>API'yi yeni sürüme GÜNCELLER: çalışan sunucuyu durdurur, indirip açar (mevcut ayarlar korunur), yeniden başlatır.</summary>
    public static async Task<(bool ok, string message)> UpdateApiAsync(Action<string>? progress = null)
    {
        if (!IsInstalled()) return (false, "Güncellenecek bir API kurulumu bulunamadı. Önce 'İndir & Kur' yapın.");
        bool svc = IsServiceInstalled();
        progress?.Invoke("Sunucu durduruluyor…");
        if (svc) RunElevated($"/c sc stop {ServiceName}", "");   // hizmeti durdur (dosya kilidi)
        Stop();                                                   // süreci de kapat
        await Task.Delay(1500);                                   // dosya kilitleri serbest kalsın

        var (ok, msg) = await DownloadAndInstallAsync(progress, preserveSettings: true);
        if (!ok) { if (svc) RunElevated($"/c sc start {ServiceName}", ""); else Start(); return (ok, msg); }

        progress?.Invoke("Sunucu yeniden başlatılıyor…");
        if (svc) { var (sok, smsg) = RunElevated($"/c sc start {ServiceName}", "Hizmet yeniden başlatıldı."); return (true, msg + (sok ? " Hizmet yeniden başlatıldı." : " (Hizmeti elle başlatın: " + smsg + ")")); }
        Start();
        return (true, msg + " Sunucu yeniden başlatıldı.");
    }

    /// <summary>İndirilen API'nin appsettings.json'unu BU PC'nin Database + Kiosk ayarlarıyla yazar.
    /// preserve verilirse (güncelleme) mevcut ayarlar yeni varsayılanların ÜZERİNE yedirilir (kullanıcı ayarları korunur).</summary>
    private static void ConfigureAppSettings(string path, JsonObject? preserve = null)
    {
        JsonObject root = ((File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null) as JsonObject) ?? new JsonObject();
        if (preserve is not null) DeepMerge(root, preserve);   // mevcut (eski) ayarlar yeni sürüm varsayılanlarının üzerine
        var db = AppConfig.Settings;
        root["Database"] = new JsonObject
        {
            ["Server"] = db.Server,
            ["UserId"] = db.UserId,
            ["Password"] = db.Password,
            ["DatabaseName"] = db.DatabaseName,
            ["Encrypt"] = db.Encrypt,
            ["TrustServerCertificate"] = db.TrustServerCertificate,
            ["ConnectTimeout"] = db.ConnectTimeout
        };
        root["Kiosk"] = new JsonObject { ["No"] = AppConfig.KioskNo };
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>overlay değerlerini target üzerine derin (recursive) yedirir; nesneler birleşir, diğer değerler overlay kazanır.</summary>
    private static void DeepMerge(JsonObject target, JsonObject overlay)
    {
        foreach (var kv in overlay)
        {
            if (kv.Value is JsonObject ov && target[kv.Key] is JsonObject tv) DeepMerge(tv, ov);
            else target[kv.Key] = kv.Value?.DeepClone();
        }
    }

    // ===== Garson PWA (statik) kurulumu =====
    /// <summary>PWA'nın açılacağı klasör: kurulu API exe'sinin yanında MindAeonRest.Pwa\wwwroot (API bunu /app'te sunar).</summary>
    public static string? PwaTargetDir
    {
        get
        {
            var exe = FindExe();
            var baseDir = exe is not null ? Path.GetDirectoryName(exe)! : InstallDir;
            return Path.Combine(baseDir, "MindAeonRest.Pwa", "wwwroot");
        }
    }

    /// <summary>PWA kurulu mu (index.html var mı).</summary>
    public static bool IsPwaInstalled()
    {
        var dir = PwaTargetDir;
        return dir is not null && File.Exists(Path.Combine(dir, "index.html"));
    }

    /// <summary>Tabletlerin açacağı PWA adresi.</summary>
    public static string PwaUrl => $"{Url}/app";

    /// <summary>PWA.rar'ı indirir ve API'nin sunduğu klasöre açar (eski içerik temizlenir). API'nin kurulu olması gerekir.</summary>
    public static async Task<(bool ok, string message)> DownloadAndInstallPwaAsync(Action<string>? progress = null)
    {
        try
        {
            var target = PwaTargetDir;
            if (target is null) return (false, "Önce Mobil API'yi kurun (PWA, API'nin yanına kurulur).");

            progress?.Invoke("PWA indiriliyor…");
            string tmpRar = Path.Combine(Path.GetTempPath(), "MindAeonRest.Pwa.rar");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            using (var resp = await http.GetAsync(PwaDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmpRar);
                await resp.Content.CopyToAsync(fs);
            }

            progress?.Invoke("PWA açılıyor…");
            string tmpDir = Path.Combine(Path.GetTempPath(), "MindAeonRest.Pwa.extract");
            try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            Directory.CreateDirectory(tmpDir);
            using (var archive = ArchiveFactory.Open(tmpRar))
            {
                var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    entry.WriteToDirectory(tmpDir, opts);
            }
            try { File.Delete(tmpRar); } catch { }

            // PWA kökünü bul (index.html'in bulunduğu klasör — arşiv kökünde ya da alt klasörde olabilir)
            string? root = File.Exists(Path.Combine(tmpDir, "index.html"))
                ? tmpDir
                : Directory.GetFiles(tmpDir, "index.html", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault();
            if (root is null) return (false, "Arşiv açıldı ama index.html bulunamadı (geçersiz PWA paketi).");

            progress?.Invoke("PWA yerleştiriliyor…");
            try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { }   // eski içerik temizlenir
            CopyDirectory(root, target);
            try { Directory.Delete(tmpDir, true); } catch { }

            return (true, "PWA indirildi ve kuruldu. Tabletlerden " + PwaUrl + " adresinden açılır.");
        }
        catch (Exception ex) { return (false, "PWA kurulum hatası: " + ex.Message); }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    // ===== Windows Hizmeti (otomatik başlama, POS'tan bağımsız) =====
    public const string ServiceName = "MindAeonRestApi";

    /// <summary>Hizmet kayıtlı mı (sc query exit kodu 0).</summary>
    public static bool IsServiceInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query {ServiceName}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(4000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Hizmeti kurar (otomatik başlama) + başlatır. Yönetici onayı (UAC) ister.</summary>
    public static (bool ok, string message) InstallService()
    {
        var exe = FindExe();
        if (exe is null) return (false, $"{ExeName} bulunamadı. Önce API'yi derleyin/dağıtın.");
        string cmd = $"/c {FirewallCmd} & " +
                     $"sc create {ServiceName} binPath= \"{exe}\" start= auto DisplayName= \"MindAeonRest Mobil API\" & " +
                     $"sc description {ServiceName} \"MindAeonRest mobil uygulama (garson/mutfak/patron) API sunucusu\" & " +
                     $"sc start {ServiceName}";
        return RunElevated(cmd, "Hizmet kuruldu, başlatıldı ve güvenlik duvarı açıldı.");
    }

    /// <summary>Hizmeti durdurur ve kaldırır. Yönetici onayı ister.</summary>
    public static (bool ok, string message) UninstallService()
        => RunElevated($"/c sc stop {ServiceName} & sc delete {ServiceName}", "Hizmet kaldırıldı.");

    /// <summary>Güvenlik duvarında 5000 portunu TÜM profillerde (Public dahil) açar; çakışan otomatik
    /// uygulama kurallarını siler. Yönetici onayı ister.</summary>
    public static (bool ok, string message) OpenFirewall()
        => RunElevated("/c " + FirewallCmd, $"Güvenlik duvarında {Port} portu (tüm ağlar) açıldı.");

    private const string FirewallCmd =
        "netsh advfirewall firewall delete rule name=\"MindAeonRest.Api\" >nul 2>&1 & " +
        "netsh advfirewall firewall delete rule name=\"MindAeonRest Mobil API\" >nul 2>&1 & " +
        "netsh advfirewall firewall add rule name=\"MindAeonRest Mobil API\" dir=in action=allow protocol=TCP localport=5000 profile=any";

    private static (bool, string) RunElevated(string cmdArgs, string okMsg)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", cmdArgs) { UseShellExecute = true, Verb = "runas", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0 ? (true, okMsg) : (false, $"İşlem tamamlanamadı (kod {p.ExitCode}). Yönetici onayı verildiğinden emin olun.");
        }
        catch (Exception ex) { return (false, "İşlem iptal edildi ya da başarısız: " + ex.Message); }
    }

    /// <summary>WPF açılışında yedek başlatma — YALNIZ API exe'si WPF ile aynı klasördeyse (dağıtım/sunucu makinesi).
    /// Böylece her terminal değil, yalnız API'nin paketlendiği sunucu makinesi otomatik başlatır.</summary>
    public static async Task EnsureStartedAsync()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(bundled)) return;                       // dağıtım değil → otomatik başlatma yok
        if (ProcessExists() || await IsRunningAsync()) return;
        try { Process.Start(new ProcessStartInfo(bundled) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory }); }
        catch { }
    }

    /// <summary>Bu makinenin LAN IPv4 adresi (tabletlerin bağlanacağı). Sanal adaptörleri eler,
    /// gerçek WiFi/Ethernet'i (192.168 > 10 > 172) tercih eder.</summary>
    public static string LocalIPv4()
    {
        try
        {
            string[] virtualHints = { "virtual", "vmware", "hyper-v", "wsl", "docker", "loopback", "vethernet", "tap" };
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !virtualHints.Any(h => n.Description.ToLowerInvariant().Contains(h) || n.Name.ToLowerInvariant().Contains(h)))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .ToList();

            int Rank(string ip) => ip.StartsWith("192.168.") ? 0 : ip.StartsWith("10.") ? 1 : ip.StartsWith("172.") ? 2 : 3;
            var best = ips.OrderBy(Rank).FirstOrDefault();
            return best ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    public static string Url => $"http://{LocalIPv4()}:{Port}";
}
