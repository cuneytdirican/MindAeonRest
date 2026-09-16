using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;

namespace MindAeonRestWeb.Pages.BackOffice.SystemManagement;

/// <summary>
/// Sistem Yönetimi (back-office) — SALT-OKUNUR bilgi paneli. WPF SystemManagementWindow'un
/// web karşılığıdır ama SELF-UPDATE YOKTUR: web sürümü sunucuya (IIS) dağıtım ile güncellenir,
/// kendini indirmez. Bu yüzden AppUpdater ÇAĞRILMAZ; ekran yalnız çalışan uygulamanın sürüm,
/// ortam ve oturum/kiracı bilgisini gösterir.
///
/// Kapı: yalnız SysAdmin. POST/handler YOK.
/// </summary>
public class IndexModel : PageModel
{
    // ===== Sürüm =====
    public string AppVersion { get; private set; } = "";
    public string RuntimeVersion { get; private set; } = "";

    // ===== Ortam =====
    public string MachineName { get; private set; } = "";
    public string OsVersion { get; private set; } = "";
    public bool Is64BitProcess { get; private set; }
    public bool Is64BitOs { get; private set; }
    public string FrameworkDescription { get; private set; } = "";
    public string StartTimeText { get; private set; } = "";
    public string UptimeText { get; private set; } = "";

    // ===== Kiracı / Oturum =====
    public string CompanyText { get; private set; } = "—";
    public string BranchText { get; private set; } = "—";
    public string KioskNoText { get; private set; } = "—";
    public string LicensePackageText { get; private set; } = "—";
    public string UserText { get; private set; } = "—";

    // Yalnız sistem yöneticisi.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        // --- Sürüm bilgisi ---
        var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = asmVer is null ? "—" : $"v{asmVer}";
        RuntimeVersion = Environment.Version.ToString();
        FrameworkDescription = RuntimeInformation.FrameworkDescription;

        // --- Ortam bilgisi ---
        MachineName = Environment.MachineName;
        OsVersion = Environment.OSVersion.ToString();
        Is64BitProcess = Environment.Is64BitProcess;
        Is64BitOs = Environment.Is64BitOperatingSystem;

        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var start = proc.StartTime;
            StartTimeText = start.ToString("dd.MM.yyyy HH:mm:ss");
            var up = DateTime.Now - start;
            UptimeText = $"{(int)up.TotalDays} gün {up.Hours} sa {up.Minutes} dk";
        }
        catch
        {
            StartTimeText = "—";
            UptimeText = "—";
        }

        // --- Kiracı / oturum bilgisi (Session'dan salt-okunur) ---
        var company = Session.CurrentCompany;
        if (company is not null)
            CompanyText = string.IsNullOrWhiteSpace(company.Code)
                ? company.Name
                : $"{company.Name} ({company.Code})";

        var branch = Session.CurrentBranch;
        if (branch is not null)
            BranchText = branch.Name;

        KioskNoText = Session.CurrentKioskNo.ToString();

        var lic = Session.CurrentLicense;
        if (lic is not null)
            LicensePackageText = lic.PackageName;

        var user = Session.CurrentUser;
        if (user is not null)
            UserText = string.Join(", ", Session.CurrentRoles.DefaultIfEmpty(user.RoleName));

        return Page();
    }
}
