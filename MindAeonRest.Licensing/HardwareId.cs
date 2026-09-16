using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace MindAeonRest.Licensing;

/// <summary>Bu makinenin donanım kimliği: CPU ProcessorId + SİSTEM diski seri no → SHA-256 → kısa kimlik.
/// Lisansı makineye bağlar (başka makineye kopyalanamaz).
///
/// <para><b>Kararlılık (#3):</b> Disk seri no artık <c>Win32_DiskDrive</c>'ın rastgele ilk kaydından değil,
/// SİSTEM diskinden (Index=0, yoksa ilk USB-olmayan sabit disk) alınır → USB takılınca kimlik DEĞİŞMEZ.
/// Sıradan makinelerde (disk 0'ın seri no'su dolu) üretilen değer eski kodla AYNIdır → mevcut lisanslar kırılmaz.</para>
///
/// <para><b>Fail-OPEN önlemi (#17):</b> CPU ve disk'in İKİSİ de okunamıyorsa eskiden ham dize "CPU:NA|DISK:NA"
/// olur, TÜM böyle makineler AYNI kimliği alırdı (paylaşılan sabit id). Artık bu uç durumda anakart seri no +
/// MAC + makine adı gibi makineye-özel bileşenlere düşülür; hiçbiri güvenilir değilse makineye özel KALICI bir
/// GUID dosyasına düşülür. Böylece kimlik hiçbir zaman tüm makinelerde ortak/sabit olmaz.</para>
/// </summary>
public static class HardwareId
{
    private static string? _cached;

    public static string Current
    {
        get
        {
            if (_cached != null) return _cached;

            string cpu = GetCpuId();
            string disk = GetSystemDiskSerial();

            string raw;
            if (cpu.Length > 0 || disk.Length > 0)
            {
                // NORMAL: eski biçimi birebir koru (eksik bileşen "NA") → mevcut lisanslarla uyumlu.
                raw = $"CPU:{(cpu.Length > 0 ? cpu : "NA")}|DISK:{(disk.Length > 0 ? disk : "NA")}";
            }
            else
            {
                // İKİSİ de yok → paylaşılan "CPU:NA|DISK:NA" ÜRETME (fail-open). Makineye özel bileşenlere düş.
                string board = GetBoardSerial();
                string mac = GetPrimaryMac();
                if (board.Length > 0 || mac.Length > 0)
                    raw = $"MB:{(board.Length > 0 ? board : "NA")}|MAC:{(mac.Length > 0 ? mac : "NA")}|HOST:{Environment.MachineName}";
                else
                    raw = $"GUID:{GetOrCreatePersistentMachineGuid()}";
            }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            // okunur/kısa: ilk 10 baytın HEX'i, 5'er gruplu
            var hex = System.Convert.ToHexString(hash, 0, 10);
            _cached = $"{hex[..5]}-{hex[5..10]}-{hex[10..15]}-{hex[15..20]}";
            return _cached;
        }
    }

    private static string GetCpuId() => SafeWmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");

    /// <summary>Sistem (boot) diskinin seri no'su: önce Index=0, yoksa ilk USB-olmayan sabit disk.
    /// Böylece USB bellek takıp çıkarmak kimliği değiştirmez.</summary>
    private static string GetSystemDiskSerial()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber, Index, InterfaceType FROM Win32_DiskDrive");
            string? index0 = null;
            string? firstFixed = null;
            foreach (ManagementObject mo in searcher.Get())
            {
                var serial = mo["SerialNumber"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(serial)) continue;
                var iface = mo["InterfaceType"]?.ToString() ?? "";

                uint idx = uint.MaxValue;
                try { idx = Convert.ToUInt32(mo["Index"] ?? uint.MaxValue); } catch { }

                if (idx == 0 && index0 is null) index0 = serial;                 // sistem/boot diski
                if (!iface.Equals("USB", StringComparison.OrdinalIgnoreCase) && firstFixed is null)
                    firstFixed = serial;                                          // ilk sabit (USB-olmayan) disk
            }
            return index0 ?? firstFixed ?? "";
        }
        catch { return ""; }
    }

    private static string GetBoardSerial()
    {
        var v = SafeWmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
        // BIOS bazen "To be filled by O.E.M." gibi anlamsız/paylaşılan değerler döner → güvenilmez say.
        if (v.Length == 0 || v.Contains("O.E.M", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("filled", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("Default", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("None", StringComparison.OrdinalIgnoreCase))
            return "";
        return v;
    }

    private static string GetPrimaryMac()
        => SafeWmi("SELECT MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE AND MACAddress IS NOT NULL", "MACAddress");

    private static string SafeWmi(string query, string prop)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject mo in searcher.Get())
            {
                var v = mo[prop]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
        }
        catch { /* WMI erişilemezse boş */ }
        return "";
    }

    /// <summary>Son çare: makineye özel KALICI GUID (ProgramData). Paylaşılan sabit id yerine en azından
    /// her makinede FARKLI bir değer verir. Dosya silinirse yeniden üretilir (fail-closed: o makinede lisans
    /// yeniden aktivasyon ister — tüm makinelerde ortak açık kapı bırakmaktan iyidir).</summary>
    private static string GetOrCreatePersistentMachineGuid()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MindAeon", "keys");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "hwid.guid");
            if (File.Exists(file))
            {
                var existing = File.ReadAllText(file).Trim();
                if (existing.Length > 0) return existing;
            }
            var guid = Guid.NewGuid().ToString("N");
            File.WriteAllText(file, guid);
            return guid;
        }
        catch
        {
            // Dosya yazılamıyorsa bile makine adına düş (yine de tüm makinelerde ortak DEĞİL).
            return $"HOST:{Environment.MachineName}";
        }
    }
}
