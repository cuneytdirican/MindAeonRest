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

    // Kullanıcı yönetimi SİSTEM DB'dir; yalnız SysAdmin.
    private static bool CanAccess => Session.IsSysAdmin;

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
