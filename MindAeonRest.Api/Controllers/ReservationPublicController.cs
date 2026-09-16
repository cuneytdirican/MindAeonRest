using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Api.Auth;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>FAZ 5c — ONLINE REZERVASYON + BEKLEME LİSTESİ API'si.
/// PUBLIC uçlar ([AllowAnonymous], rate-limited "public"): müşteri public sayfadan müsait slot sorgular, rezervasyon
/// oluşturur; uygun masa yoksa bekleme listesine alınır. Token'daki ŞUBE bağlamı ELLE doğrulanır (IDOR yok; müşteri
/// başka şubeye erişemez). Rezervasyon oluşturma mevcut <see cref="ReservationService"/> (çakışma motoru + Save) ile
/// yürür — WPF/back-office ile aynı kurallar. Onay SMS'i NotificationService (reservation-confirm) ile best-effort.
/// YÖNETİM ucu ([Authorize]): back-office public rezervasyon LİNKİ üretir (şube gömülü token).</summary>
[ApiController]
[Route("api")]
public class ReservationPublicController(PublicTokenService tokens) : ControllerBase
{
    private readonly ReservationService _reservations = new();
    private readonly WaitlistService _waitlist = new();
    private readonly BranchService _branches = new();

    private const int DefaultDurationMinutes = 120;

    /// <summary>Token'daki şirket/şube bağlamını bu istek için (izole) Session kapsamına yazar (FeedbackController deseni).</summary>
    private static void ApplyContext(ReserveTokenContext ctx)
    {
        Session.CurrentCompany = new Company { Code = ctx.CompanyCode, Name = ctx.CompanyCode, DatabaseName = ctx.DbName };
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(ctx.DbName);
        Session.CurrentBranch = new CompanyBranch { Id = ctx.BranchId };
        Session.CurrentKioskNo = 1;
    }

    /// <summary>date (yyyy-MM-dd) + time (HH:mm) → yerel DateTime. Geçersizse false.</summary>
    private static bool TryBuildStart(string? date, string? time, out DateTime start)
    {
        start = default;
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var t) &&
            !TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out t))
            return false;
        start = d.Date.Add(t);
        return true;
    }

    /// <summary>PUBLIC — sayfa başlığı için şube adı (salt-okuma). Geçersiz token → 401.</summary>
    [HttpGet("public/reservation/info")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult Info([FromQuery] string t)
    {
        var ctx = tokens.ValidateReserve(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var branch = _branches.GetById(ctx.BranchId);
            return Ok(new { branch = branch?.Name ?? ctx.CompanyCode, waiting = _waitlist.CountWaiting(ctx.BranchId) });
        }
    }

    /// <summary>PUBLIC — müsaitlik sorgusu: verilen gün/saat/kişi için şubede uygun masa var mı, kaç tane?
    /// Fiyat/kimlik YOK; yalnız token'daki şube kapsamı. Rezervasyon oluşturmaz (salt-okuma).</summary>
    [HttpGet("public/reservation/availability")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult Availability([FromQuery] string t, [FromQuery] string? date, [FromQuery] string? time,
        [FromQuery] int party = 1, [FromQuery] int durationMin = DefaultDurationMinutes)
    {
        var ctx = tokens.ValidateReserve(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (!TryBuildStart(date, time, out var start))
            return BadRequest(new { message = "Geçerli bir tarih ve saat seçiniz." });
        if (party < 1) party = 1;
        if (durationMin < 30) durationMin = DefaultDurationMinutes;
        var end = start.AddMinutes(durationMin);

        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var tables = _reservations.GetAvailableTables(ctx.BranchId, start, end, party);
            int fit = tables.Count(x => x.Seats >= party);
            return Ok(new { available = fit > 0, count = fit });
        }
    }

    /// <summary>PUBLIC — rezervasyon oluştur. Uygun masa varsa rezervasyon açar (çakışma motoru + Save) ve onay SMS'i
    /// gönderir (best-effort); UYGUN MASA YOKSA otomatik BEKLEME LİSTESİNE alır. Dönüş: mode = reservation | waitlist.</summary>
    [HttpPost("public/reservation")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult Create([FromQuery] string t, [FromBody] PublicReservationRequest req)
    {
        var ctx = tokens.ValidateReserve(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Ad Soyad giriniz." });
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { message = "Telefon giriniz." });
        if (!TryBuildStart(req.Date, req.Time, out var start))
            return BadRequest(new { message = "Geçerli bir tarih ve saat seçiniz." });

        int party = req.Party < 1 ? 1 : req.Party;
        int durationMin = req.DurationMin < 30 ? DefaultDurationMinutes : req.DurationMin;
        var end = start.AddMinutes(durationMin);

        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            try
            {
                var res = _reservations.CreatePublicReservation(ctx.BranchId, req.Name!, req.Phone!, party, start, end, req.Note);
                if (res is not null)
                {
                    TrySendConfirm(ctx.BranchId, req.Name!, req.Phone!, start, res.Code);
                    return Ok(new { mode = "reservation", code = res.Code,
                        message = $"Rezervasyonunuz alındı. Kod: {res.Code}. Onay mesajı gönderildi." });
                }

                // Uygun masa yok → bekleme listesine al.
                var entry = _waitlist.Add(new WaitlistEntry
                {
                    BranchId = ctx.BranchId, Name = req.Name!, Phone = req.Phone,
                    PartySize = party, RequestedAt = DateTime.UtcNow, Note = req.Note,
                    Status = WaitlistStatus.Bekliyor
                }, null);
                return Ok(new { mode = "waitlist", id = entry.Id,
                    message = "Seçtiğiniz saat için uygun masa kalmadı. Bekleme listesine alındınız; yer açılınca sizi arayacağız." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }

    /// <summary>PUBLIC — doğrudan bekleme listesine ekle (müşteri masa aramadan sıraya girmek isterse).</summary>
    [HttpPost("public/reservation/waitlist")]
    [AllowAnonymous]
    [EnableRateLimiting("public")]
    public IActionResult AddWaitlist([FromQuery] string t, [FromBody] PublicWaitlistRequest req)
    {
        var ctx = tokens.ValidateReserve(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Ad Soyad giriniz." });
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { message = "Telefon giriniz." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            try
            {
                var entry = _waitlist.Add(new WaitlistEntry
                {
                    BranchId = ctx.BranchId, Name = req.Name!, Phone = req.Phone,
                    PartySize = req.Party < 1 ? 1 : req.Party, RequestedAt = DateTime.UtcNow,
                    Note = req.Note, Status = WaitlistStatus.Bekliyor
                }, null);
                return Ok(new { id = entry.Id, message = "Bekleme listesine alındınız; yer açılınca sizi arayacağız." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }

    /// <summary>YÖNETİM — back-office public rezervasyon LİNKİ üretir (şube gömülü token). Yalnız [Authorize].
    /// Restoran bu linki web sitesine/QR'a koyar; müşteri /Reservation?t=... ile rezervasyon yapar.</summary>
    [HttpPost("reservation/public-link")]
    [Authorize]
    public IActionResult PublicLink([FromBody] ReservationLinkRequest req)
    {
        var company = Session.CurrentCompany;
        int branchId = (req?.BranchId ?? 0) > 0 ? req!.BranchId : Session.CurrentBranch?.Id ?? 0;
        if (company is null || branchId <= 0)
            return BadRequest(new { message = "Şube bağlamı çözülemedi." });

        var token = tokens.CreateReserveToken(company.Code, company.DatabaseName, branchId);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/Reservation?t={Uri.EscapeDataString(token)}";
        return Ok(new { token, url });
    }

    /// <summary>Onay SMS'i (reservation-confirm şablonu) — best-effort; fail-closed sağlayıcı akışı bozmaz.</summary>
    private static void TrySendConfirm(int branchId, string name, string phone, DateTime start, string code)
    {
        try
        {
            var model = new Dictionary<string, string?>
            {
                ["ad"] = name,
                ["tarih"] = start.ToString("dd.MM.yyyy HH:mm"),
                ["kod"] = code
            };
            new NotificationService().Send(NotificationChannel.Sms, phone, "reservation-confirm", null, model, branchId);
        }
        catch { /* bildirim gönderilemedi → rezervasyon akışını bozma */ }
    }
}

/// <summary>Public rezervasyon gövdesi (fiyat/kimlik YOK — ad/telefon/gün/saat/kişi + not).</summary>
public sealed class PublicReservationRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Date { get; set; }        // yyyy-MM-dd
    public string? Time { get; set; }        // HH:mm
    public int Party { get; set; } = 1;
    public int DurationMin { get; set; }
    public string? Note { get; set; }
}

/// <summary>Public bekleme-listesi gövdesi.</summary>
public sealed class PublicWaitlistRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public int Party { get; set; } = 1;
    public string? Note { get; set; }
}

/// <summary>Yönetim — public rezervasyon linki isteği.</summary>
public sealed class ReservationLinkRequest
{
    public int BranchId { get; set; }
}
