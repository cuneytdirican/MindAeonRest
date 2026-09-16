using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Api.Auth;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>FAZ 5d — Geri-bildirim / yorum hunisi API'si.
/// PUBLIC uçlar ([AllowAnonymous], rate-limited): müşteri anket sayfasından yıldız+yorum gönderir; token'daki şube
/// bağlamı ELLE doğrulanır (IDOR yok). HUNİ: puan >= eşik ve şube ayarı açıksa Google yorum URL'i döndürülür
/// (istemci oraya yönlendirir); düşük puan yalnız içeride kaydedilir. YÖNETİM ucu ([Authorize]): POS/kasa
/// adisyon kapanınca/ödeme sonrası OPSİYONEL SMS daveti tetikler (best-effort, akışı bozmaz).</summary>
[ApiController]
[Route("api")]
public class FeedbackController(PublicTokenService tokens) : ControllerBase
{
    private readonly FeedbackService _feedback = new();
    private readonly BranchService _branches = new();

    /// <summary>Token'daki şirket/şube bağlamını bu istek için (izole) Session kapsamına yazar (PublicController deseni).</summary>
    private static void ApplyContext(FeedbackTokenContext ctx)
    {
        Session.CurrentCompany = new Company { Code = ctx.CompanyCode, Name = ctx.CompanyCode, DatabaseName = ctx.DbName };
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(ctx.DbName);
        Session.CurrentBranch = new CompanyBranch { Id = ctx.BranchId };
        Session.CurrentKioskNo = 1;
    }

    /// <summary>PUBLIC — anket başlığı için şube adı (salt-okuma). Geçersiz token → 401.</summary>
    [HttpGet("public/feedback/info")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult Info([FromQuery] string t)
    {
        var ctx = tokens.ValidateFeedback(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var branch = _branches.GetById(ctx.BranchId);
            return Ok(new { branch = branch?.Name ?? ctx.CompanyCode });
        }
    }

    /// <summary>PUBLIC — anket gönder: yıldız (1-5) + yorum. Kaydeder; huni kararını (Google URL veya null) döner.
    /// Fiyat/kimlik YOK; yalnız token'daki şube kapsamı. Yüksek puan + ayar açık → redirect döndürülür.</summary>
    [HttpPost("public/feedback")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult Submit([FromQuery] string t, [FromBody] FeedbackSubmitRequest req)
    {
        var ctx = tokens.ValidateFeedback(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (req is null || req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { message = "Lütfen 1-5 arası bir puan verin." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx);

            // Huni kararı: şube ayarına göre yüksek puan Google'a yönlendirilir (fail-closed: ayar yoksa null).
            var settings = _feedback.GetSettings(ctx.BranchId);
            var redirect = FeedbackService.ResolveRedirect(req.Rating, settings);

            _feedback.Save(new Feedback
            {
                BranchId = ctx.BranchId,
                OrderId = ctx.OrderId,
                CustomerPhone = req.Phone,
                Rating = req.Rating,
                Comment = req.Comment,
                GoogleRedirected = redirect is not null
            });

            return Ok(new { ok = true, redirect });
        }
    }

    /// <summary>YÖNETİM — adisyon kapanınca/ödeme sonrası OPSİYONEL anket SMS daveti (POS/kasa tetikler).
    /// Anket token'ı + public sayfa URL'i sunucuda üretilir; SMS best-effort (NotificationService fail-closed).
    /// Mevcut satış/kapanış akışına bağlı DEĞİLDİR (çağıran ister, göndermezse akış etkilenmez).</summary>
    [HttpPost("feedback/invite")]
    [Authorize]
    public IActionResult Invite([FromBody] FeedbackInviteRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { message = "Telefon gerekli." });

        var company = Session.CurrentCompany;
        int branchId = req.BranchId > 0 ? req.BranchId : Session.CurrentBranch?.Id ?? 0;
        if (company is null || branchId <= 0)
            return BadRequest(new { message = "Şube bağlamı çözülemedi." });

        var token = tokens.CreateFeedbackToken(company.Code, company.DatabaseName, branchId, req.OrderId ?? 0);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var surveyUrl = $"{baseUrl}/feedback?t={Uri.EscapeDataString(token)}";

        var result = _feedback.TrySendInvite(branchId, req.Phone!, surveyUrl, Session.CurrentUserId);
        return Ok(new { sent = result.Success, status = result.Status.ToString(), message = result.Error, surveyUrl });
    }
}

/// <summary>Public anket gönderim gövdesi (fiyat/kimlik YOK — yalnız puan+yorum+opsiyonel telefon).</summary>
public sealed class FeedbackSubmitRequest
{
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? Phone { get; set; }
}

/// <summary>Yönetim daveti gövdesi (POS/kasa → SMS anket linki).</summary>
public sealed class FeedbackInviteRequest
{
    public int BranchId { get; set; }
    public int? OrderId { get; set; }
    public string? Phone { get; set; }
}
