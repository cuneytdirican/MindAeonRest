using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Users;

public class EditModel : PageModel
{
    private readonly UserService _userService = new();
    private readonly RoleService _roleService = new();
    private readonly UserRoleService _userRoleService = new();
    private readonly UserAccessService _accessService = new();

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string? Password { get; set; }          // yeni kayıtta zorunlu; düzenlemede boşsa değişmez. GET'te ASLA doldurulmaz.
    [BindProperty] public List<int> SelectedRoleIds { get; set; } = new();
    [BindProperty] public bool AllBranches { get; set; } = true;
    [BindProperty] public List<int> SelectedBranchIds { get; set; } = new();
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<RoleOption> RoleOptions { get; private set; } = new();
    public List<BranchOption> BranchOptions { get; private set; } = new();

    // Kullanıcı yönetimi SİSTEM DB'dir. SysAdmin: tam (eski davranış). Kiracı yöneticisi (Patron/Yönetici): yalnız
    // KENDİ şirketinin kullanıcıları (IsManageableByTenantAdmin) + SysAdmin rolü atanamaz/görünmez.
    private static bool CanAccess => Session.IsTenantAdmin;

    /// <summary>KİRACI İZOLASYONU: kiracı yöneticisi bu kullanıcıyı yönetebilir mi? Hedef (silinmiş olsa da)
    /// aktif şirkete erişimli olmalı, SysAdmin OLMAMALI ve BAŞKA hiçbir şirkete erişimi OLMAMALI
    /// (birden çok kiracıya bağlı ortak kullanıcıları yalnız SysAdmin yönetir). Index (sil/geri al) de kullanır.</summary>
    internal static bool IsManageableByTenantAdmin(int userId, int companyId)
    {
        if (userId <= 0 || companyId <= 0) return false;
        var companies = new UserAccessService().GetCompanyIdsForUser(userId);
        if (companies.Count == 0 || companies.Any(c => c != companyId)) return false;
        return !new UserService().UserHasSysAdminRole(userId);
    }

    private static bool IsSysAdminRoleName(string name) => string.Equals(name, "SysAdmin", StringComparison.OrdinalIgnoreCase);

    // Kiracı yöneticisi kendi yönetici rolünü/aktifliğini kaldırıp şirketi yöneticisiz bırakamaz.
    private static bool IsTenantAdminRoleName(string name) =>
        new[] { "Patron", "Yönetici", "Yonetici" }.Any(x => string.Equals(name, x, StringComparison.OrdinalIgnoreCase));

    // WPF ile aynı: birincil/gösterim rolü en yetkili roldür.
    private static int Priority(string name) => name switch
    { "SysAdmin" => 0, "Kasiyer" => 1, "Garson" => 2, "Mutfak" => 3, _ => 9 };

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        int companyId = ResolveCompanyId();
        LoadLookups(companyId);

        if (id is int i && i > 0)
        {
            if (!Session.IsSysAdmin && !IsManageableByTenantAdmin(i, companyId)) return Forbid();
            var u = _userService.GetById(i);
            if (u is null) return RedirectToPage("Index");
            Id = u.Id;
            Username = u.Username;
            FullName = u.FullName;
            IsActive = u.IsActive;
            SelectedRoleIds = _userRoleService.GetRoleIds(u.Id);

            if (companyId > 0)
            {
                var (all, ids) = _accessService.GetAccess(u.Id, companyId);
                AllBranches = all;
                SelectedBranchIds = ids;
            }
            // Password: düzenlemede boş bırakılır → değişmez. GET'te asla set edilmez.
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        int companyId = ResolveCompanyId();
        bool tenantScoped = !Session.IsSysAdmin;
        if (tenantScoped)
        {
            // Şirket bağlamı yoksa kiracı yöneticisi erişimsiz (şirketsiz) kullanıcı üretemez.
            if (companyId <= 0) return Forbid();
            if (!IsNew && !IsManageableByTenantAdmin(Id, companyId)) return Forbid();
            // SysAdmin rol id'si POST'ta gelirse (form dışı) sunucu tarafında reddet.
            var saIds = _roleService.GetAll().Where(r => IsSysAdminRoleName(r.Name)).Select(r => r.Id).ToHashSet();
            if (SelectedRoleIds.Any(saIds.Contains)) return Forbid();
        }
        LoadLookups(companyId);

        Username = (Username ?? "").Trim();
        FullName = (FullName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Username)) { Error = "Kullanıcı adını giriniz."; return Page(); }
        if (string.IsNullOrWhiteSpace(FullName)) { Error = "Ad soyad giriniz."; return Page(); }

        // Rol kümesi doğrulama (WPF ile aynı kural: Mutfak/SysAdmin tek başına).
        var selectedNames = RoleOptions.Where(r => SelectedRoleIds.Contains(r.Id)).Select(r => r.Name).ToList();
        var roleError = UserRoleService.ValidateRoleSet(selectedNames);
        if (roleError != null) { Error = roleError; return Page(); }

        // Birincil rol en yetkili; UserService.Create/Update roleIds[0]'ı birincil kabul eder.
        var orderedRoleIds = RoleOptions
            .Where(r => SelectedRoleIds.Contains(r.Id))
            .OrderBy(r => Priority(r.Name))
            .Select(r => r.Id)
            .ToList();

        // Yalnız aktif şirketin şubeleri kabul edilir (başka şirketin şube id'si erişim kaydına yazılmaz).
        if (companyId > 0)
            SelectedBranchIds = SelectedBranchIds.Where(b => BranchOptions.Any(o => o.Id == b)).Distinct().ToList();

        if (tenantScoped && !IsNew && Id == Session.CurrentUserId
            && (!IsActive || !selectedNames.Any(IsTenantAdminRoleName)))
        {
            Error = "Kendi yönetici rolünüzü veya aktifliğinizi kaldıramazsınız.";
            return Page();
        }

        if (!AllBranches && SelectedBranchIds.Count == 0)
        {
            Error = "En az bir şube seçin veya 'Tüm şubeler'i işaretleyin.";
            return Page();
        }

        try
        {
            int userId;
            if (IsNew)
            {
                if (string.IsNullOrEmpty(Password)) { Error = "Yeni kullanıcı için parola/PIN giriniz."; return Page(); }
                var created = _userService.Create(Username, FullName, orderedRoleIds, IsActive, Password, Session.CurrentUserId);
                userId = created.Id;
            }
            else
            {
                userId = Id;
                // Pasif kullanıcıyı yeniden aktifleştirmek lisans kullanıcı limitine tabidir (kiracı yöneticisi için baypas engeli).
                if (tenantScoped && IsActive && !_userService.IsActive(userId))
                    UserService.EnforceUserLimit(companyId);
                _userService.Update(userId, FullName, orderedRoleIds, IsActive, Session.CurrentUserId);
                if (!string.IsNullOrEmpty(Password))
                    _userService.SetPassword(userId, Password, Session.CurrentUserId);
            }

            if (companyId > 0)
                _accessService.SetBranchAccess(userId, companyId, AllBranches, SelectedBranchIds, Session.CurrentUserId);

            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadLookups(int companyId)
    {
        RoleOptions = _roleService.GetAll()
            // SysAdmin (çapraz-tenant) rolünü yalnız SysAdmin atayabilir.
            .Where(r => Session.IsSysAdmin || !IsSysAdminRoleName(r.Name))
            .Select(r => new RoleOption { Id = r.Id, Name = r.Name })
            .ToList();

        BranchOptions = companyId > 0
            ? new BranchService().GetByCompany(companyId)
                .Select(b => new BranchOption { Id = b.Id, Name = b.Display })
                .ToList()
            : new();
    }

    private static int ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrWhiteSpace(code)) return 0;
        return new CompanyService().GetByCode(code)?.Id ?? 0;
    }

    public class RoleOption { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class BranchOption { public int Id { get; set; } public string Name { get; set; } = ""; }
}
