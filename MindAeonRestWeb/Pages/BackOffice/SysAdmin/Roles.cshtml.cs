using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Sistem Yönetimi › Roller (S4) — SALT SysAdmin rol konsolu. Roller SİSTEM DB'de tutulur ve
/// yalnızca SysAdmin yönetir (WPF: "Yalnızca SysAdmin çağırmalı").
///
/// Listeler: ad, açıklama, AI Hub yetkisi, (sistem/built-in mi). Aksiyonlar:
///   • Yeni Rol  → OnPostCreate  → RoleService.Create(name, description, byUserId)
///   • Düzenle   → OnPostUpdate  → RoleService.Update(id, name, description, byUserId)
///   • AI izni   → OnPostSetAi   → RoleService.SetAiPermission(roleId, canUseAi, userId)
///
/// SİSTEM (built-in) ROLLER: adı DEĞİŞTİRİLEMEZ. UI'da input readonly + sunucuda ad posta edilse bile
/// mevcut ad korunur (RoleService de zaten Update'te exception atar — çift kemer). Silme YOK.
///
/// GÜVENLİK: sayfa + HER handler başında SysAdmin kapısı (Forbid). Native alert/confirm YOK → doğrudan
/// form POST. EF YOK — RoleService ADO.NET üzerinden çalışır. TempData ile mesaj taşınır (Tenants deseni).
/// </summary>
public class RolesModel : PageModel
{
    private readonly RoleService _svc = new();

    public List<Role> Items { get; private set; } = new();

    // Düzenleme paneli (aynı sayfada) — ?edit=Id ise dolar.
    public Role? EditRole { get; private set; }

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    // Yalnız sistem yöneticisi rolleri yönetir (sistem DB gücü).
    private static bool CanAccess => Session.IsSysAdmin;

    // Sistem/built-in roller: adı kilitli. ExclusiveRoles + varsayılan tohum rolleri (+ Patron).
    // Diakritik varyasyonlar (Yonetici/İade) da güvenlik için dahildir.
    private static readonly HashSet<string> BuiltInRoles = BuildBuiltIns();

    private static HashSet<string> BuildBuiltIns()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SysAdmin", "Yönetici", "Yonetici", "Kasiyer", "Patron",
            "Garson", "Mutfak", "İade", "Iade", "Z_Rapor"
        };
        foreach (var r in UserRoleService.ExclusiveRoles) set.Add(r);
        return set;
    }

    /// <summary>Adı kilitli sistem rolü mü? (UI + sunucu ortak kullanır.)</summary>
    public static bool IsBuiltIn(string? name)
        => !string.IsNullOrWhiteSpace(name) && BuiltInRoles.Contains(name.Trim());

    public IActionResult OnGet(int? edit)
    {
        if (!CanAccess) return Forbid();

        Message = TempData["SysMsg"] as string;
        Error = TempData["SysErr"] as string;

        Items = _svc.GetAll(includeDeleted: false);

        if (edit is { } eid)
            EditRole = Items.FirstOrDefault(r => r.Id == eid) ?? _svc.GetById(eid);

        return Page();
    }

    /// <summary>Yeni rol oluşturur (sistem DB). Ad zorunlu; benzersizlik/kural denetimi RoleService'te.</summary>
    public IActionResult OnPostCreate(string? name, string? description)
    {
        if (!CanAccess) return Forbid();

        var nm = (name ?? "").Trim();
        if (nm.Length == 0)
        {
            TempData["SysErr"] = "Rol adı zorunludur.";
            return RedirectToPage();
        }

        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        try
        {
            _svc.Create(nm, desc, Session.CurrentUserId);
            try { AuditService.Write("Role", 0, "Insert", null, new { Name = nm, Description = desc }); }
            catch { /* denetim best-effort */ }
            TempData["SysMsg"] = $"'{nm}' rolü oluşturuldu.";
        }
        catch (Exception ex)
        {
            TempData["SysErr"] = "Rol oluşturulamadı: " + ex.Message;
        }
        return RedirectToPage();
    }

    /// <summary>Rol ad/açıklamasını günceller. Sistem rolünde ad DEĞİŞTİRİLMEZ (mevcut ad korunur).</summary>
    public IActionResult OnPostUpdate(int id, string? name, string? description)
    {
        if (!CanAccess) return Forbid();

        var role = _svc.GetById(id);
        if (role is null)
        {
            TempData["SysErr"] = "Rol bulunamadı.";
            return RedirectToPage();
        }

        // Sistem rolünde ad kilitli → posta edilen ad NE olursa olsun mevcut adı koru (UI readonly + sunucu kalkanı).
        var newName = IsBuiltIn(role.Name) ? role.Name : (name ?? "").Trim();
        if (newName.Length == 0)
        {
            TempData["SysErr"] = "Rol adı boş olamaz.";
            return RedirectToPage(new { edit = id });
        }

        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        try
        {
            _svc.Update(id, newName, desc, Session.CurrentUserId);
            try
            {
                AuditService.Write("Role", id, "Update",
                    new { role.Name, role.Description },
                    new { Name = newName, Description = desc });
            }
            catch { /* denetim best-effort */ }
            TempData["SysMsg"] = $"'{newName}' rolü güncellendi.";
        }
        catch (Exception ex)
        {
            TempData["SysErr"] = "Rol güncellenemedi: " + ex.Message;
        }
        return RedirectToPage();
    }

    /// <summary>AI Hub yetkisini aç/kapat — güncel değeri DB'den okuyup tersine çevirir (SetAiPermission).</summary>
    public IActionResult OnPostSetAi(int id)
    {
        if (!CanAccess) return Forbid();

        var role = _svc.GetById(id);
        if (role is null)
        {
            TempData["SysErr"] = "Rol bulunamadı.";
            return RedirectToPage();
        }

        var next = !role.CanUseAi;
        try
        {
            _svc.SetAiPermission(id, next, Session.CurrentUserId);
            try { AuditService.Write("Role", id, "SetAiPermission", new { role.CanUseAi }, new { CanUseAi = next }); }
            catch { /* denetim best-effort */ }
            TempData["SysMsg"] = next
                ? $"'{role.Name}' rolüne AI Hub yetkisi verildi."
                : $"'{role.Name}' rolünün AI Hub yetkisi kaldırıldı.";
        }
        catch (Exception ex)
        {
            TempData["SysErr"] = "AI yetkisi değiştirilemedi: " + ex.Message;
        }
        return RedirectToPage();
    }
}
