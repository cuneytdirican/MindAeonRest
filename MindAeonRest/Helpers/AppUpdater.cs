using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace MindAeonRest.Helpers;

/// <summary>WPF uygulamasının kendini güncellemesi: sunucudaki version.json'a bakar, yeni sürüm varsa
/// WPF.rar'ı indirip geçici klasöre açar, küçük bir güncelleyici script ile (uygulama kapandıktan sonra)
/// dosyaları kurulum klasörüne kopyalar ve uygulamayı yeniden başlatır. Çalışan .exe kendini değiştiremez,
/// bu yüzden takas kapanıştan SONRA harici script ile yapılır.</summary>
public static class AppUpdater
{
    public const string VersionUrl = "https://www.mobilteg.com.tr/downloads/MindAeonRest/version.json";
    public const string DownloadUrl = "https://www.mobilteg.com.tr/downloads/MindAeonRest/WPF.rar";
    private const string ExeName = "MindAeonRest.exe";

    /// <summary>Çalışan uygulamanın sürümü.</summary>
    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    public static string CurrentText => Current.ToString(3);

    public record UpdateInfo(bool HasUpdate, string Latest, string? Notes, string Url);

    /// <summary>Sunucudaki version.json'ı okuyup mevcut sürümle karşılaştırır. Ağ hatası → HasUpdate=false.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string json = await http.GetStringAsync(VersionUrl);
            var node = JsonNode.Parse(json);
            string? verStr = node?["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(verStr) || !Version.TryParse(verStr, out var latest)) return null;
            string? notes = node?["notes"]?.ToString();
            string url = node?["url"]?.ToString() is { Length: > 0 } u ? u : DownloadUrl;
            return new UpdateInfo(latest > Current, latest.ToString(3), notes, url);
        }
        catch { return null; }
    }

    /// <summary>WPF.rar'ı indirir, geçici klasöre açar, güncelleyici script'i yazıp başlatır.
    /// Başarılıysa çağıran uygulamayı KAPATMALIDIR (script kapanmayı bekleyip dosyaları takas eder).</summary>
    public static async Task<(bool ok, string message)> DownloadAndApplyAsync(string? url, Action<string>? progress = null)
    {
        try
        {
            string appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            string tmpRar = Path.Combine(Path.GetTempPath(), "MindAeonRest.WPF.rar");
            string staging = Path.Combine(Path.GetTempPath(), "MindAeonRest.WPF.update");

            progress?.Invoke("Güncelleme indiriliyor…");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var resp = await http.GetAsync(url ?? DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmpRar);
                await resp.Content.CopyToAsync(fs);
            }

            progress?.Invoke("Arşiv açılıyor…");
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            Directory.CreateDirectory(staging);
            using (var archive = ArchiveFactory.Open(tmpRar))
            {
                var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    entry.WriteToDirectory(staging, opts);
            }
            try { File.Delete(tmpRar); } catch { }

            // exe arşiv kökünde değilse (alt klasörde paketlenmişse) kökü bul
            string root = File.Exists(Path.Combine(staging, ExeName))
                ? staging
                : Directory.GetFiles(staging, ExeName, SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault() ?? staging;
            if (!File.Exists(Path.Combine(root, ExeName)))
                return (false, "Güncelleme paketi geçersiz: MindAeonRest.exe bulunamadı.");

            progress?.Invoke("Kurulum hazırlanıyor…");
            int pid = Environment.ProcessId;
            string exePath = Path.Combine(appDir, ExeName);
            string bat = Path.Combine(Path.GetTempPath(), "mar_update.cmd");
            // Uygulama (pid) kapanmasını bekle → dosyaları kopyala (robocopy) → yeniden başlat → temizle
            string script =
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                ":waitloop\r\n" +
                $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto waitloop)\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                $"robocopy \"{root}\" \"{appDir}\" /E /R:4 /W:2 /NFL /NDL /NJH /NJS >nul\r\n" +
                $"start \"\" \"{exePath}\"\r\n" +
                $"rmdir /s /q \"{staging}\" >nul 2>&1\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n";
            File.WriteAllText(bat, script, new System.Text.UTF8Encoding(false));

            progress?.Invoke("Yeniden başlatılıyor…");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            });
            return (true, "Güncelleme indirildi. Uygulama kapanıp yeni sürümle yeniden başlatılacak.");
        }
        catch (Exception ex) { return (false, "Güncelleme hatası: " + ex.Message); }
    }
}
