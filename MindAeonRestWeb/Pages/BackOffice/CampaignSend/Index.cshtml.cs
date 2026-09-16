using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.CampaignSend;

/// <summary>KAMPANYA GÖNDERİMİ (Faz 5b): hedef segment (tümü/grup/doğum günü/geri kazanım) + kanal (SMS/e-posta/WhatsApp)
/// + şablon/gövde + (opsiyonel) kampanya seç → ÖNİZLE (alıcıları listele) → GÖNDER (NotificationService toplu) → log.
/// FAIL-CLOSED: gerçek gönderim kanal ayarına/TEST moduna bağlıdır (kapalıysa simülasyon/başarısız). Gate: SysAdmin/Patron + Campaign lisansı.</summary>
public class IndexModel : PageModel
{
    private readonly CampaignSendService _svc = new();
    private readonly BranchService _branchSvc = new();
    private readonly CustomerGroupService _groupSvc = new();
    private readonly CampaignService _campSvc = new();

    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public int Segment { get; set; }
    [BindProperty] public int? GroupId { get; set; }
    [BindProperty] public string? RecoveryDaysText { get; set; } = "30";
    [BindProperty] public int Channel { get; set; }
    [BindProperty] public int? CampaignId { get; set; }
    [BindProperty] public string? TemplateKey { get; set; }
    [BindProperty] public string? Body { get; set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> GroupOptions { get; private set; } = new();
    public List<SelectListItem> CampaignOptions { get; private set; } = new();
    public List<SelectListItem> TemplateOptions { get; private set; } = new();
    public List<SelectListItem> SegmentOptions { get; private set; } = new();
    public List<SelectListItem> ChannelOptions { get; private set; } = new();

    public List<CampaignSendRecipient> Preview { get; private set; } = new();
    public CampaignSendResult? Result { get; private set; }
    public List<CampaignSendLogRow> RecentLog { get; private set; } = new();
    public string? Error { get; private set; }
    public string? Info { get; private set; }

    // Kampanya gönderimi yönetimsel + kampanya lisansı gerektirir.
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.Campaign);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (BranchId <= 0) BranchId = Session.CurrentBranch?.Id ?? FirstBranchId();
        RecentLog = _svc.GetRecentLog();
        return Page();
    }

    /// <summary>Önizleme: segmenti çözer, alıcıları listeler (GÖNDERİM YAPMAZ).</summary>
    public IActionResult OnPostPreview()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        try
        {
            Preview = _svc.GetRecipients((CampaignSegment)Segment, (NotificationChannel)Channel, GroupId, RecoveryDays(), DateTime.Now);
            Info = $"{Preview.Count} alıcı bulundu. Gönderim için 'Gönder' butonunu kullanın.";
        }
        catch (Exception ex) { Error = ex.Message; }
        RecentLog = _svc.GetRecentLog();
        return Page();
    }

    /// <summary>Gönder: alıcıları YENİDEN çözer (istemciye güvenilmez) ve toplu gönderir.</summary>
    public IActionResult OnPostSend()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (BranchId <= 0) { Error = "Lütfen bir şube seçiniz."; RecentLog = _svc.GetRecentLog(); return Page(); }
        if (string.IsNullOrWhiteSpace(Body) && string.IsNullOrWhiteSpace(TemplateKey))
        {
            Error = "Lütfen bir şablon seçin ya da mesaj metni girin.";
            RecentLog = _svc.GetRecentLog();
            return Page();
        }
        try
        {
            var recipients = _svc.GetRecipients((CampaignSegment)Segment, (NotificationChannel)Channel, GroupId, RecoveryDays(), DateTime.Now);
            if (recipients.Count == 0)
            {
                Info = "Seçili segment + kanal için uygun alıcı (iletişim adresi olan) bulunamadı.";
            }
            else
            {
                Result = _svc.SendBulk(recipients, (NotificationChannel)Channel, (CampaignSegment)Segment,
                    CampaignId, string.IsNullOrWhiteSpace(TemplateKey) ? null : TemplateKey,
                    string.IsNullOrWhiteSpace(Body) ? null : Body.Trim(), BranchId, Session.CurrentUserId);
                Info = $"Toplam {Result.Total} — Gönderildi: {Result.Sent}, Simülasyon: {Result.Simulated}, Başarısız: {Result.Failed}.";
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        RecentLog = _svc.GetRecentLog();
        return Page();
    }

    private int RecoveryDays() => Num.TryParseDecimal(RecoveryDaysText, out var d) && d > 0 ? (int)d : 30;

    private void LoadLookups()
    {
        BranchOptions = new();
        GroupOptions = new();
        CampaignOptions = new();

        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(_branchSvc.GetByCompany(co.Id).Select(b => new SelectListItem(b.Name, b.Id.ToString())));

        GroupOptions.Add(new SelectListItem("— Grup seçin —", ""));
        GroupOptions.AddRange(_groupSvc.GetAll().Where(g => g.IsActive).Select(g => new SelectListItem(g.Display, g.Id.ToString())));

        CampaignOptions.Add(new SelectListItem("— (Kampanya bağlama yok) —", ""));
        CampaignOptions.AddRange(_campSvc.GetAll().Where(c => c.IsActive).Select(c => new SelectListItem($"{c.Name} ({c.Code})", c.Id.ToString())));

        TemplateOptions = new() { new SelectListItem("— (Serbest metin) —", "") };
        TemplateOptions.AddRange(NotificationTemplates.Builtins.Keys.Select(k => new SelectListItem(k, k)));

        SegmentOptions = Enum.GetValues<CampaignSegment>()
            .Select(s => new SelectListItem(s.ToTr(), ((int)s).ToString())).ToList();
        ChannelOptions = new[] { NotificationChannel.Sms, NotificationChannel.Email, NotificationChannel.WhatsApp }
            .Select(ch => new SelectListItem(ch.ToTr(), ((int)ch).ToString())).ToList();
    }

    private int FirstBranchId() => int.TryParse(BranchOptions.FirstOrDefault()?.Value, out var id) ? id : 0;
}
