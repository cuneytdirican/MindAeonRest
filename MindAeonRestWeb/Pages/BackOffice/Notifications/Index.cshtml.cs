using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Notifications;

/// <summary>Bildirim Ayarları (ŞUBE BAZLI, kanal başına: SMS / e-posta / WhatsApp).
/// Sağlayıcı + anahtar/başlık + SMTP sunucu/port + aktiflik + TEST modu. Anahtarlar şifreli saklanır.
/// FAIL-CLOSED: TEST modunda gerçek gönderim yapılmaz. Gate: yalnız sistem yöneticisi / Patron.</summary>
public class IndexModel : PageModel
{
    private readonly NotificationService _svc = new();
    private readonly BranchService _branchSvc = new();

    /// <summary>Formda düzenlenen tek kanal satırı.</summary>
    public class ChannelRow
    {
        public int Channel { get; set; }
        public string ChannelText { get; set; } = "";
        public int Provider { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? Sender { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public bool UseSsl { get; set; } = true;
        public bool IsEnabled { get; set; }
        public bool TestMode { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }
    [BindProperty] public List<ChannelRow> Rows { get; set; } = new();

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    // Bildirim ayarları yönetimsel: yalnız SysAdmin/Patron (Core özellik — lisans kapısı yok).
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    /// <summary>Kanala uygun sağlayıcı seçenekleri (Yok + o kanalın sağlayıcıları).</summary>
    public static List<SelectListItem> ProvidersFor(NotificationChannel ch)
    {
        var providers = ch switch
        {
            NotificationChannel.Sms => new[] { NotificationProvider.Yok, NotificationProvider.Netgsm, NotificationProvider.IletiMerkezi },
            NotificationChannel.Email => new[] { NotificationProvider.Yok, NotificationProvider.Smtp },
            NotificationChannel.WhatsApp => new[] { NotificationProvider.Yok, NotificationProvider.WhatsAppBusiness },
            _ => new[] { NotificationProvider.Yok }
        };
        return providers.Select(p => new SelectListItem(p.ToTr(), ((int)p).ToString())).ToList();
    }

    public IActionResult OnGet(bool saved = false)
    {
        if (!CanAccess) return Forbid();
        LoadBranches();
        if (BranchId <= 0)
            BranchId = Session.CurrentBranch?.Id ?? FirstBranchId();
        LoadRows();
        Saved = saved ? "Şube bildirim ayarları kaydedildi." : null;
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadBranches();

        if (BranchId <= 0)
        {
            Error = "Lütfen bir şube seçiniz.";
            LoadRows();
            return Page();
        }

        try
        {
            foreach (var row in Rows)
            {
                _svc.SaveSettings(new NotificationSetting
                {
                    BranchId = BranchId,
                    Channel = (NotificationChannel)row.Channel,
                    Provider = (NotificationProvider)row.Provider,
                    ApiKey = string.IsNullOrWhiteSpace(row.ApiKey) ? null : row.ApiKey.Trim(),
                    ApiSecret = string.IsNullOrWhiteSpace(row.ApiSecret) ? null : row.ApiSecret.Trim(),
                    Sender = string.IsNullOrWhiteSpace(row.Sender) ? null : row.Sender.Trim(),
                    Host = string.IsNullOrWhiteSpace(row.Host) ? null : row.Host.Trim(),
                    Port = row.Port,
                    UseSsl = row.UseSsl,
                    IsEnabled = row.IsEnabled,
                    TestMode = row.TestMode
                }, Session.CurrentUserId);
            }
            return RedirectToPage(new { BranchId, saved = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LoadRows();
            return Page();
        }
    }

    private void LoadBranches()
    {
        BranchOptions = new();
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(_branchSvc.GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }

    private int FirstBranchId()
        => int.TryParse(BranchOptions.FirstOrDefault()?.Value, out var id) ? id : 0;

    private void LoadRows()
    {
        Rows = new();
        if (BranchId <= 0) return;
        foreach (var s in _svc.GetByBranch(BranchId))
            Rows.Add(new ChannelRow
            {
                Channel = (int)s.Channel,
                ChannelText = s.ChannelText,
                Provider = (int)s.Provider,
                ApiKey = s.ApiKey,
                ApiSecret = s.ApiSecret,
                Sender = s.Sender,
                Host = s.Host,
                Port = s.Port,
                UseSsl = s.UseSsl,
                IsEnabled = s.IsEnabled,
                TestMode = s.TestMode
            });
    }
}
