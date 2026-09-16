using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

/// <summary>
/// POS tenant'ları için gözetim (oversight) ekranı. POS provisioning API'sini SUNUCU-TARAFI çağırır;
/// X-Provision-Key tarayıcıya gitmez. Salt-okuma: listeler + satır detayı (kullanıcılar/şubeler).
/// </summary>
[Authorize(Roles = "Admin")]
public class TenantsModel(PosProvisionClient pos) : PageModel
{
    public IReadOnlyList<TenantSummary> Tenants { get; private set; } = [];
    public string? LoadError { get; private set; }

    // Detay partial için
    public string? DetailCode { get; private set; }
    public string? DetailName { get; private set; }
    public IReadOnlyList<TenantUser> Users { get; private set; } = [];
    public IReadOnlyList<TenantBranch> Branches { get; private set; } = [];
    public string? DetailError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var (value, error) = await pos.ListTenantsAsync(ct);
        Tenants = value ?? [];
        LoadError = error;
    }

    /// <summary>Satırdan açılan detay: seçilen tenant'ın kullanıcı + şube listeleri (htmx partial).</summary>
    public async Task<IActionResult> OnGetDetailAsync(string code, string? name, CancellationToken ct)
    {
        DetailCode = code;
        DetailName = name;

        var (users, uErr) = await pos.ListTenantUsersAsync(code, ct);
        var (branches, bErr) = await pos.ListTenantBranchesAsync(code, ct);
        Users = users ?? [];
        Branches = branches ?? [];
        DetailError = uErr ?? bErr;

        return Partial("_TenantDetail", this);
    }
}
