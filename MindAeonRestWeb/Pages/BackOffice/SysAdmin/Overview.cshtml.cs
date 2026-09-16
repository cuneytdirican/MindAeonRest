using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Süper Admin › Pano (S5) — SALT-OKUMA sistem geneli özet + sağlık paneli. YALNIZ SysAdmin.
///
/// Dört blok: (A) sayım kartları (şirket/kullanıcı/SysAdmin/şube), (B) her kiracının lisans özeti
/// (LicenseService.GetStatusByCompany), (C) sistem sağlığı (DB bağlantı testi + ortam + sürüm + CentralHost),
/// (D) son denetim kayıtları (AuditService.Query) veya Denetim İzi ekranına link.
///
/// GÜVENLİK: OnGet başında SysAdmin kapısı (Forbid). Nav öğesi de yalnız SysAdmin görür (section gate).
/// SALT-OKUMA: hiçbir mutasyon yok (POST handler yok). EF YOK — Core servisleri ADO.NET üzerinden çalışır.
/// Sağlık kontrolleri try/catch ile sarılıdır ki tek bir hata panoyu çökertmesin.
/// </summary>
public class OverviewModel(IWebHostEnvironment env, IConfiguration config) : PageModel
{
    private readonly CompanyService _companies = new();
    private readonly UserService _users = new();
    private readonly BranchService _branches = new();
    private readonly LicenseService _license = new();

    // Yalnız sistem yöneticisi (çapraz-tenant özet gücü).
    private static bool CanAccess => Session.IsSysAdmin;

    // ---- (A) Sayımlar ----
    public int CompaniesActive { get; private set; }
    public int CompaniesInactive { get; private set; }
    public int CompaniesTotal => CompaniesActive + CompaniesInactive;
    public int UsersActive { get; private set; }
    public int UsersInactive { get; private set; }
    public int UsersTotal => UsersActive + UsersInactive;
    public int SysAdminCount { get; private set; }
    public int BranchesTotal { get; private set; }

    // ---- (B) Lisans özeti ----
    public sealed record LicenseRow(
        string Code, string Name, bool CompanyActive,
        string Package, string StateText, DateTime? ExpiresUtc,
        int MaxUsers, int MaxBranches, int MaxKiosks,
        bool IsUsable, bool IsExpired, bool IsSoon);

    public List<LicenseRow> Licenses { get; private set; } = new();
    public string? PortalUrl { get; private set; }

    // ---- (C) Sağlık ----
    public bool DbOk { get; private set; }
    public string? DbError { get; private set; }
    /// <summary>Sayım sorgusu (LoadCounts) hatası — sağlık ping'inden BAĞIMSIZ gösterilir; aksi halde 0'lar sağlıklı sanılırdı.</summary>
    public string? CountsError { get; private set; }
    public string Environment { get; private set; } = "";
    public string AppVersion { get; private set; } = "";
    public bool CentralHost { get; private set; }

    // ---- (D) Son denetim ----
    public List<AuditLogEntry>? RecentAudit { get; private set; }
    public string? AuditError { get; private set; }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        LoadCounts();
        LoadLicenses();
        LoadHealth();
        LoadRecentAudit();

        return Page();
    }

    private void LoadCounts()
    {
        try
        {
            var companies = _companies.GetAll();
            CompaniesActive = companies.Count(c => c.IsActive);
            CompaniesInactive = companies.Count(c => !c.IsActive);

            // Tüm şirketlerin şubeleri (sistem DB'sindeki CompanyBranches). Az sayıda tenant için kabul.
            BranchesTotal = companies.Sum(c => _branches.GetByCompany(c.Id).Count);

            var users = _users.GetAll();
            UsersActive = users.Count(u => u.IsActive);
            UsersInactive = users.Count(u => !u.IsActive);

            SysAdminCount = _users.CountActiveSysAdmins();
        }
        catch (Exception ex)
        {
            // Sayım sorgusu düşse bile pano ayakta kalsın; hatayı sağlık ping'inden BAĞIMSIZ göster
            // (aksi halde ping başarılıysa 0'lar yeşil "Bağlantı OK" ile sağlıklı sanılırdı).
            CountsError = ex.Message;
        }
    }

    private void LoadLicenses()
    {
        // Config'te açıkça Portal URL varsa link olarak göster; yoksa yalnız metin (lisans API'sini portal sanmayalım).
        var portal = config["Licence:PortalUrl"] ?? config["Licensing:PortalUrl"];
        PortalUrl = string.IsNullOrWhiteSpace(portal) ? null : portal;

        List<MindAeonRest.Models.Company> companies;
        try { companies = _companies.GetAll(); }
        catch { return; }

        var now = DateTime.UtcNow;
        // PERFORMANS NOTU: N şirket için N lisans sorgusu (GetStatusByCompany). Az sayıda kiracı için kabul edilir;
        // kiracı sayısı büyürse toplu tek sorguya (CompanyCode IN ...) taşınmalıdır.
        foreach (var c in companies.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase))
        {
            LicenseStatus st;
            try { st = _license.GetStatusByCompany(c.Code); }
            catch { st = new LicenseStatus { Message = "okunamadı" }; }

            bool expired = st.State is LicenseState.Expired or LicenseState.TrialExpired
                           || (st.ExpiresUtc is { } ex && ex < now);
            bool soon = !expired && st.ExpiresUtc is { } e && (e - now).TotalDays <= 15;

            Licenses.Add(new LicenseRow(
                c.Code, c.Name, c.IsActive,
                st.PackageName, st.StateText, st.ExpiresUtc,
                st.MaxUsers, st.MaxBranches, st.MaxKiosks,
                st.IsUsable, expired, soon));
        }
    }

    private void LoadHealth()
    {
        Environment = env.EnvironmentName;

        // CentralHost bayrağı (SaaS/merkezi lisans modu).
        CentralHost = config.GetValue("Licensing:CentralHost", false);

        // Uygulama sürümü: önce InformationalVersion, yoksa assembly sürümü.
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        AppVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                     ?? asm.GetName().Version?.ToString()
                     ?? "—";

        // Sistem DB bağlantısı — basit aç/kapa testi (salt-okuma). Try/catch ile panoyu korur.
        try
        {
            using var c = Database.OpenConnection();
            DbOk = c.State == System.Data.ConnectionState.Open;
        }
        catch (Exception ex)
        {
            DbOk = false;
            DbError = ex.Message;
        }
    }

    private void LoadRecentAudit()
    {
        // AuditService.Query mevcut → aktif şirketin son 8 denetim kaydını göster (salt-okuma).
        // Company DB'sine bağlanır; hata olursa görünümde "Denetim İzi" ekranına link kalır.
        try
        {
            RecentAudit = AuditService.Query(null, null, null, null, take: 8);
        }
        catch (Exception ex)
        {
            AuditError = ex.Message;
        }
    }
}
