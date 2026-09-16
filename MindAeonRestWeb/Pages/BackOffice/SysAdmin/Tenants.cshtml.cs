using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Sistem Yönetimi › Şirketler (S1) — çok-kiracılı PROVISIONING konsolu. SALT SysAdmin.
///
/// Tüm kiracı şirketleri (ProvisionService.ListTenants) + her birinin aktiflik bilgisini
/// (CompanyService.GetAll → Company.IsActive) birleştirip listeler. Satırda Pasifleştir/Aktifleştir
/// (CompanyService.SetActive) ve Detay (aynı sayfada ?detail=KOD ile kullanıcı + şube dökümü) sunar.
///
/// GÜVENLİK: her OnGet/OnPost başında SysAdmin kapısı (Forbid). Native confirm YOK → doğrudan form POST.
/// EF YOK — ProvisionService/CompanyService ADO.NET üzerinden çalışır.
/// </summary>
public class TenantsModel : PageModel
{
    private readonly ProvisionService _prov = new();
    private readonly CompanyService _companies = new();
    private readonly BranchService _branches = new();
    private readonly LicenseService _license = new();

    public sealed record TenantRow(
        int? CompanyId, string Code, string Name, string DbName,
        int BranchCount, int UserCount, bool IsActive, DateTime? CreatedAt);

    public List<TenantRow> Rows { get; private set; } = new();

    // Detay paneli (aynı sayfada) — ?detail=KOD ise dolar.
    public string? DetailCode { get; private set; }
    public string? DetailName { get; private set; }
    public List<TenantUserInfo>? DetailUsers { get; private set; }
    public List<TenantBranchInfo>? DetailBranches { get; private set; }

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    // Yalnız sistem yöneticisi (çok-kiracılı provisioning gücü).
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet(string? detail)
    {
        if (!CanAccess) return Forbid();

        Message = TempData["SysMsg"] as string;
        Error = TempData["SysErr"] as string;

        LoadRows();

        if (!string.IsNullOrWhiteSpace(detail))
        {
            DetailCode = detail.Trim();
            DetailUsers = _prov.ListTenantUsers(DetailCode);
            DetailBranches = _prov.ListTenantBranches(DetailCode);
            DetailName = Rows.FirstOrDefault(r => string.Equals(r.Code, DetailCode, StringComparison.OrdinalIgnoreCase))?.Name;
            // Bilinmeyen kod → ListTenantUsers null döner; görünümde "bulunamadı" gösterilir.
            if (DetailUsers is null) DetailCode = null;
        }

        return Page();
    }

    /// <summary>Bir kiracının aktifliğini değiştirir (CompanyService.SetActive). Doğrudan form POST — native onay yok.</summary>
    public IActionResult OnPostSetActive(int companyId, bool active)
    {
        if (!CanAccess) return Forbid();

        try
        {
            _companies.SetActive(companyId, active, Session.CurrentUserId);
            TempData["SysMsg"] = active
                ? "Şirket aktifleştirildi."
                : "Şirket pasifleştirildi. (Pasif şirkete giriş engellenir.)";
        }
        catch (Exception ex)
        {
            TempData["SysErr"] = "Aktiflik değiştirilemedi: " + ex.Message;
        }

        return RedirectToPage();
    }

    /// <summary>S3 — SysAdmin oturum-içi kiracı geçişi: cookie principal HEDEF şirket + o şirketin bir şubesi +
    /// hedef lisansla YENİDEN imzalanır. SysAdmin kimliği + rolleri KORUNUR (geri dönebilir, tam yetkilidir).
    /// AKTİF/pasif fark etmez (SysAdmin pasife de girebilir). Zaten geçişteysek mevcut ev kodunu KORUR.</summary>
    public async Task<IActionResult> OnPostSwitchToAsync(string code)
    {
        if (!Session.IsSysAdmin) return Forbid();

        var company = _companies.GetByCode(code);
        if (company is null)
        {
            TempData["SysErr"] = "Şirket bulunamadı.";
            return RedirectToPage("./Tenants");
        }

        // Aktif şube yoksa (hepsi pasif) yine de bir şubeyle geçelim → kurulum yapılabilsin. Hiç şube yoksa hata.
        var branch = _branches.GetActiveByCompany(company.Id).FirstOrDefault()
                     ?? _branches.GetByCompany(company.Id).FirstOrDefault();
        if (branch is null)
        {
            TempData["SysErr"] = "Bu şirketin şubesi yok; geçiş yapılamaz.";
            return RedirectToPage("./Tenants");
        }

        // Zaten geçiş modundaysak mevcut ev'i KORU (aksi halde bulunduğumuz şirket ev olur).
        var home = Session.SysAdminHomeCode ?? Session.CompanyCode;
        var lic = _license.GetStatusByCompany(company.Code);

        var fullName = HttpContext.User.Identity?.Name ?? "";
        var user = new User { Id = Session.CurrentUserId ?? 0, FullName = fullName, Username = fullName };
        var roles = Session.CurrentRoles.ToList();   // SysAdmin dahil — geri dönüş ve tam yetki için

        var principal = WebLoginService.BuildPrincipal(user, company, branch.Id, roles, lic,
            sysAdminHomeCode: home, impersonating: true);

        try { AuditService.Write("SysAdminImpersonate", company.Id, "SwitchTo", null, new { Target = company.Code, Home = home }); }
        catch { /* denetim best-effort */ }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        return RedirectToPage("/BackOffice/Index");
    }

    /// <summary>S3 — "Süper Admin'e dön": ev şirketine geri imzalar, geçiş modunu TEMİZLER (sa_home/sa_imp düşer).</summary>
    public async Task<IActionResult> OnPostReturnHomeAsync()
    {
        if (!Session.IsSysAdmin) return Forbid();

        var home = Session.SysAdminHomeCode;
        if (string.IsNullOrWhiteSpace(home)) return RedirectToPage("./Tenants");   // zaten evdeyiz

        var company = _companies.GetByCode(home);
        if (company is null)
        {
            TempData["SysErr"] = "Ev şirketi bulunamadı.";
            return RedirectToPage("./Tenants");
        }

        var branch = _branches.GetActiveByCompany(company.Id).FirstOrDefault()
                     ?? _branches.GetByCompany(company.Id).FirstOrDefault();
        if (branch is null)
        {
            TempData["SysErr"] = "Ev şirketinin şubesi yok.";
            return RedirectToPage("./Tenants");
        }

        var lic = _license.GetStatusByCompany(company.Code);

        var fullName = HttpContext.User.Identity?.Name ?? "";
        var user = new User { Id = Session.CurrentUserId ?? 0, FullName = fullName, Username = fullName };
        var roles = Session.CurrentRoles.ToList();

        var principal = WebLoginService.BuildPrincipal(user, company, branch.Id, roles, lic,
            sysAdminHomeCode: null, impersonating: false);   // geçiş modunu TEMİZLE

        try { AuditService.Write("SysAdminImpersonate", company.Id, "ReturnHome", null, new { Home = company.Code }); }
        catch { /* denetim best-effort */ }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });

        return RedirectToPage("/BackOffice/SysAdmin/Tenants");
    }

    private void LoadRows()
    {
        // Aktiflik + CompanyId + oluşturma tarihi ListTenants özetinde yok → GetAll ile koda göre eşle.
        var byCode = _companies.GetAll()
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Rows = _prov.ListTenants().Select(t =>
        {
            byCode.TryGetValue(t.Code, out var co);
            return new TenantRow(
                co?.Id,
                t.Code,
                t.Name,
                t.DbName,
                t.BranchCount,
                t.UserCount,
                co?.IsActive ?? true,
                co?.CreatedAt);
        }).ToList();
    }
}
