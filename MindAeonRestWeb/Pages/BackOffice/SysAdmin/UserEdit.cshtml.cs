using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Süper Admin › Kullanıcı Ekle/Düzenle (S4) — çapraz-kiracılı. SALT SysAdmin.
///
/// Form: Kullanıcı Adı (yeni), Ad Soyad, Roller (RoleService.GetAll checkbox), Aktif ve ŞİRKET ERİŞİMİ
/// (CompanyService.GetAll listesinden işaretle → UserAccessService.GrantCompany allBranches:true / kaldır → RevokeCompany).
/// Yeni kullanıcı: UserService.Create; parola verilmezse geçici parola üretilir ve TEK SEFER panelde gösterilir.
/// Düzenleme: UserService.Update + erişim değişiklikleri Grant/Revoke.
///
/// GÜVENLİK: her OnGet/OnPost başında SysAdmin kapısı (Forbid). SysAdmin rolü atamak TEHLİKELİ → görünür uyarı
/// (yine izinli). Geçici parola redirect/TempData KULLANILMADAN doğrudan panelde; saklanmaz, loglanmaz.
/// EF YOK — servisler ADO.NET üzerinden çalışır.
/// </summary>
public class UserEditModel : PageModel
{
    private readonly UserService _users = new();
    private readonly RoleService _roles = new();
    private readonly UserRoleService _userRoles = new();
    private readonly UserAccessService _access = new();
    private readonly CompanyService _companies = new();

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string? Password { get; set; }   // yeni: boşsa geçici üretilir. Düzenleme: boşsa değişmez. GET'te ASLA doldurulmaz.
    [BindProperty] public List<int> SelectedRoleIds { get; set; } = new();
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public List<int> SelectedCompanyIds { get; set; } = new();

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<RoleOption> RoleOptions { get; private set; } = new();
    public List<CompanyOption> CompanyOptions { get; private set; } = new();
    public int SysAdminRoleId { get; private set; }

    /// <summary>Yeni kullanıcı için üretilen geçici parola — TEK SEFER. Doluysa görünümde panel render edilir; asla saklanmaz.</summary>
    public ResultInfo? Result { get; private set; }
    public sealed record ResultInfo(string Username, string FullName, string? TempPassword);

    public sealed record RoleOption(int Id, string Name);
    public sealed record CompanyOption(int Id, string Code, string Name);

    private static bool CanAccess => Session.IsSysAdmin;

    // Birincil/gösterim rolü en yetkili roldür (WPF paritesi).
    private static int Priority(string name) => name switch
    { "SysAdmin" => 0, "Kasiyer" => 1, "Garson" => 2, "Mutfak" => 3, _ => 9 };

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (id is int i && i > 0)
        {
            var u = _users.GetById(i);
            if (u is null) return RedirectToPage("Users");
            Id = u.Id;
            Username = u.Username;
            FullName = u.FullName;
            IsActive = u.IsActive;
            SelectedRoleIds = _userRoles.GetRoleIds(u.Id);
            SelectedCompanyIds = _access.GetCompanyIdsForUser(u.Id).ToList();
            // Password GET'te ASLA doldurulmaz.
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        Username = (Username ?? "").Trim();
        FullName = (FullName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Username)) { Error = "Kullanıcı adını giriniz."; return Page(); }
        if (string.IsNullOrWhiteSpace(FullName)) { Error = "Ad soyad giriniz."; return Page(); }

        // Rol kümesi doğrulama (WPF ile aynı kural: Mutfak/SysAdmin tek başına).
        var selectedNames = RoleOptions.Where(r => SelectedRoleIds.Contains(r.Id)).Select(r => r.Name).ToList();
        var roleError = UserRoleService.ValidateRoleSet(selectedNames);
        if (roleError != null) { Error = roleError; return Page(); }

        // Birincil rol en yetkili; Create/Update roleIds[0]'ı birincil kabul eder.
        var orderedRoleIds = RoleOptions
            .Where(r => SelectedRoleIds.Contains(r.Id))
            .OrderBy(r => Priority(r.Name))
            .Select(r => r.Id)
            .ToList();

        try
        {
            int userId;
            string? tempToShow = null;

            if (IsNew)
            {
                if (!string.IsNullOrEmpty(Password))
                {
                    var created = _users.Create(Username, FullName, orderedRoleIds, IsActive, Password, Session.CurrentUserId);
                    userId = created.Id;
                }
                else
                {
                    // Parola verilmedi → geçici bir başlangıç parolasıyla oluştur, ardından ResetPassword ile
                    // gerçek geçici parolayı üret ve TEK SEFER göster.
                    string seed = Guid.NewGuid().ToString("N");
                    var created = _users.Create(Username, FullName, orderedRoleIds, IsActive, seed, Session.CurrentUserId);
                    userId = created.Id;
                    tempToShow = _users.ResetPassword(userId, Session.CurrentUserId);
                }
            }
            else
            {
                userId = Id;
                _users.Update(userId, FullName, orderedRoleIds, IsActive, Session.CurrentUserId);
                if (!string.IsNullOrEmpty(Password))
                    _users.SetPassword(userId, Password, Session.CurrentUserId);
            }

            // Şirket erişimi: işaretli → GrantCompany (allBranches:true), işaretsiz → RevokeCompany.
            foreach (var c in CompanyOptions)
            {
                if (SelectedCompanyIds.Contains(c.Id))
                    _access.GrantCompany(userId, c.Id, allBranches: true, Session.CurrentUserId);
                else
                    _access.RevokeCompany(userId, c.Id, Session.CurrentUserId);
            }

            // Geçici parola üretildiyse listede değil BURADA (redirect'siz) göster → hiçbir yerde saklanmaz.
            if (tempToShow != null)
            {
                Id = userId;
                Result = new ResultInfo(Username, FullName, tempToShow);
                Password = null;
                return Page();
            }

            return RedirectToPage("Users");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadLookups()
    {
        RoleOptions = _roles.GetAll()
            .Select(r => new RoleOption(r.Id, r.Name))
            .ToList();
        SysAdminRoleId = RoleOptions
            .FirstOrDefault(r => r.Name.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase))?.Id ?? 0;

        CompanyOptions = _companies.GetAll()
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CompanyOption(c.Id, c.Code, c.Name))
            .ToList();
    }
}
