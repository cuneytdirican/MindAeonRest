using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.SysAdmin;

/// <summary>
/// Sistem Yönetimi › Yeni Şirket Kur (S2) — çok-kiracılı PROVISIONING. SALT SysAdmin.
///
/// Form (Kod, Unvan, Yönetici Email, Yönetici Ad Soyad) → ProvisionService.CreateTenant.
/// Başarıda dönen GEÇİCİ admin parolası SONUÇ panelinde TEK SEFER gösterilir (asla saklanmaz;
/// TempData/redirect KULLANILMAZ → return Page() ile doğrudan gösterilir, kalıcı bir yerde tutulmaz).
///
/// GÜVENLİK: her OnGet/OnPost başında SysAdmin kapısı (Forbid). TenantAlreadyExistsException → "kod kullanımda".
/// EF YOK — ProvisionService ADO.NET üzerinden çalışır.
/// </summary>
public class NewTenantModel : PageModel
{
    private readonly ProvisionService _prov = new();

    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? AdminEmail { get; set; }
    [BindProperty] public string? AdminFullName { get; set; }

    /// <summary>Başarılı kurulum sonucu — TEK SEFER (geçici parola dahil). Görünümde varsa sonuç paneli render edilir.</summary>
    public TenantCreatedResult? Result { get; private set; }
    public string? Error { get; private set; }

    // Yalnız sistem yöneticisi.
    private static bool CanAccess => Session.IsSysAdmin;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();

        var code = (Code ?? "").Trim();
        var name = (Name ?? "").Trim();
        var email = (AdminEmail ?? "").Trim();
        var fullName = (AdminFullName ?? "").Trim();

        if (code.Length == 0 || name.Length == 0 || fullName.Length == 0)
        {
            Error = "Kod, Unvan ve Yönetici Ad Soyad zorunludur.";
            return Page();
        }

        try
        {
            // İmza: CreateTenant(code, name, adminEmail, adminFullName) — createdBy parametresi YOK (provisioning oturumsuz).
            Result = _prov.CreateTenant(code, name, email, fullName);
            // Başarıda formu temizle (parola tekrar POST'la sızmasın).
            Code = Name = AdminEmail = AdminFullName = null;
        }
        catch (TenantAlreadyExistsException)
        {
            Error = "Bu kod zaten kullanılıyor. Farklı bir şirket kodu seçin.";
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = "Şirket kurulamadı: " + ex.Message;
        }

        return Page();
    }
}
