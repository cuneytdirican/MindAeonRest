using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Süper Admin › Kullanıcılar (S4) — çapraz-kiracılı KULLANICI konsolu. SALT SysAdmin.
///
/// UserService.GetAll() (sistem DB, tüm kullanıcılar) + her kullanıcı için roller (UserRoleService.GetRoleNames)
/// ve erişimli şirketler (UserAccessService.GetCompanyIdsForUser → CompanyService adları) birleştirilir.
/// Şirket/arama filtresi querystring ile taşınır. Satır aksiyonları: Düzenle, Pasifleştir/Aktifleştir
/// (OnPostSetActive → UserService.SetActive), Parola Sıfırla (OnPostResetPassword → UserService.ResetPassword).
///
/// GÜVENLİK: her OnGet/OnPost başında SysAdmin kapısı (Forbid). Native onay YOK → doğrudan form POST.
/// Geçici parola TEK SEFER gösterilir: redirect/TempData YOK → return Page() ile doğrudan panelde; hiçbir yerde saklanmaz, loglanmaz.
/// EF YOK — servisler ADO.NET üzerinden çalışır.
/// </summary>
public class UsersModel : PageModel
{
    private readonly UserService _users = new();
    private readonly UserRoleService _userRoles = new();
    private readonly UserAccessService _access = new();
    private readonly CompanyService _companies = new();

    public sealed record UserRow(
        int Id, string Username, string FullName,
        IReadOnlyList<string> Roles, bool IsSysAdmin,
        IReadOnlyList<string> Companies, bool IsActive, DateTime? LastLoginAt);

    public sealed record CompanyOption(int Id, string Code, string Name);

    public List<UserRow> Rows { get; private set; } = new();
    public List<CompanyOption> CompanyFilterOptions { get; private set; } = new();

    // Filtreler (querystring). Company POST'larda korunur.
    [BindProperty(SupportsGet = true, Name = "company")] public int? CompanyFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Parola sıfırlama sonucu — TEK SEFER (geçici parola dahil). Doluysa görünümde panel render edilir; asla saklanmaz.</summary>
    public ResetResult? Reset { get; private set; }
    public sealed record ResetResult(string Username, string FullName, string TempPassword);

    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Message = TempData["SysMsg"] as string;
        Error = TempData["SysErr"] as string;
        LoadRows();
        return Page();
    }

    /// <summary>Kullanıcının aktifliğini değiştirir (UserService.SetActive). Doğrudan form POST — native onay yok.</summary>
    public IActionResult OnPostSetActive(int userId, bool active)
    {
        if (!CanAccess) return Forbid();
        try
        {
            _users.SetActive(userId, active, Session.CurrentUserId);
            TempData["SysMsg"] = active
                ? "Kullanıcı aktifleştirildi."
                : "Kullanıcı pasifleştirildi. (Pasif kullanıcı giriş yapamaz.)";
        }
        catch (Exception ex)
        {
            TempData["SysErr"] = "Aktiflik değiştirilemedi: " + ex.Message;
        }
        // Filtreyi koru.
        return RedirectToPage(new { company = CompanyFilter, q = Query });
    }

    /// <summary>Kullanıcının parolasını sıfırlar (UserService.ResetPassword → geçici parola).
    /// Dönen geçici parola TEK SEFER panelde gösterilir → redirect/TempData KULLANILMAZ, return Page() ile.
    /// Parola hiçbir yerde saklanmaz ve loglanmaz.</summary>
    public IActionResult OnPostResetPassword(int userId)
    {
        if (!CanAccess) return Forbid();
        try
        {
            var target = _users.GetById(userId);
            if (target is null)
            {
                Error = "Kullanıcı bulunamadı.";
            }
            else
            {
                string temp = _users.ResetPassword(userId, Session.CurrentUserId);
                Reset = new ResetResult(target.Username, target.FullName, temp);
                Message = "Geçici parola oluşturuldu. Aşağıdaki paneli kapatınca bir daha görüntülenemez.";
            }
        }
        catch (Exception ex)
        {
            Error = "Parola sıfırlanamadı: " + ex.Message;
        }
        LoadRows();
        return Page();   // redirect YOK → geçici parola yalnız bu yanıtta yaşar.
    }

    private void LoadRows()
    {
        var companies = _companies.GetAll();
        var byId = companies.ToDictionary(c => c.Id, c => c);
        CompanyFilterOptions = companies
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CompanyOption(c.Id, c.Code, c.Name))
            .ToList();

        var q = (Query ?? "").Trim();

        var rows = new List<UserRow>();
        foreach (var u in _users.GetAll())
        {
            var roleNames = _userRoles.GetRoleNames(u.Id);
            bool isSys = roleNames.Any(r => r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase));

            // Erişimli şirket adları (SysAdmin çapraz-tenant → "Tüm şirketler").
            var companyIds = _access.GetCompanyIdsForUser(u.Id);
            var companyNames = companyIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id].Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Şirket filtresi: seçili şirkete erişimi olanlar (SysAdmin her şirkette görünür).
            if (CompanyFilter is int cf && !isSys && !companyIds.Contains(cf))
                continue;

            // Arama filtresi: kullanıcı adı / ad soyad.
            if (q.Length > 0 &&
                u.Username.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                u.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            rows.Add(new UserRow(u.Id, u.Username, u.FullName, roleNames, isSys,
                companyNames, u.IsActive, u.LastLoginAt));
        }

        Rows = rows
            .OrderByDescending(r => r.IsSysAdmin)
            .ThenBy(r => r.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
