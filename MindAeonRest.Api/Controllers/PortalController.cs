using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindAeonRest.Api.Auth;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>FAZ 5a MÜŞTERİ SADAKAT PORTALI — ANONİM ([AllowAnonymous]) müşteri self-servis katmanı: telefon+OTP giriş,
/// puan bakiyesi, sipariş geçmişi, geçmiş adisyonu YENİDEN-SİPARİŞ. Kimlik yerine <see cref="PortalTokenService"/>
/// kısıtlı-scope token ELLE doğrulanır (ayrı audience → normal /api [Authorize] JwtBearer bunu reddeder → yetki
/// yükselmesi yok). İki aşama: (1) restoran GİRİŞ token'ı (şirket+şube gömülü) ile OTP iste/doğrula; (2) OTP sonrası
/// MÜŞTERİ-SCOPE oturum token'ı (customerId gömülü) ile puan/geçmiş/yeniden-sipariş → müşteri yalnız KENDİ kartını
/// görür (IDOR imkânsız). Rate-limit "public" (IP başına). Anti-enumerasyon: OTP-iste DAİMA genel yanıt döner
/// (numara kayıtlı olsun/olmasın). SMS gönderimi fail-closed (NotificationService; kanal kapalıysa gerçek gönderim yok).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/portal")]
[EnableRateLimiting("public")]
public class PortalController(PortalTokenService tokens, PosNotifier notifier, IConfiguration config) : ControllerBase
{
    private readonly CustomerPortalService _portal = new();
    private readonly LoyaltyService _loyalty = new();
    private readonly NotificationService _notify = new();
    private readonly PosCatalogService _catalog = new();
    private readonly OrderService _orders = new();

    /// <summary>Token bağlamını (şirket/şube) bu istek için izole Session kapsamına yazar. Anonim istekte TenantMiddleware
    /// çalışmaz → bağlamı burada kurar; şema (yeni tablolar dahil) bir kez garanti edilir. KioskNo public için sanal '1'.</summary>
    private static void ApplyContext(string companyCode, string dbName, int branchId)
    {
        Session.CurrentCompany = new Company { Code = companyCode, Name = companyCode, DatabaseName = dbName };
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(dbName);
        Session.CurrentBranch = new CompanyBranch { Id = branchId };
        Session.CurrentKioskNo = 1;
    }

    private bool EchoOtp => string.Equals(config["Portal:EchoOtp"], "true", StringComparison.OrdinalIgnoreCase);

    // ===== 1) Giriş: OTP iste =====
    /// <summary>Telefona OTP gönderir. GİRİŞ token'ı (t) şirket/şube bağlamını taşır. Anti-enumerasyon: numara kayıtlı
    /// olsun ya da olmasın DAİMA aynı genel yanıt döner. Kayıtlıysa kod üretilip SMS ile gönderilir (fail-closed).</summary>
    [HttpPost("otp-request")]
    public IActionResult RequestOtp([FromQuery] string t, [FromBody] PortalOtpRequest req)
    {
        var ctx = tokens.ValidateEntry(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (string.IsNullOrWhiteSpace(req?.Phone))
            return BadRequest(new { message = "Telefon numarası giriniz." });

        string? devCode = null;
        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);
            var issue = _portal.IssueOtp(req.Phone);
            if (issue.Found && issue.Code is not null)
            {
                // SMS gönder (fail-closed: kanal kapalı/yapılandırılmamışsa gerçek gönderim yok, akış yine genel yanıt döner).
                var body = $"Sadakat portalı giriş kodunuz: {issue.Code}";
                try { _notify.Send(NotificationChannel.Sms, req.Phone, null, body, null, ctx.BranchId); } catch { /* fail-closed: sızdırma */ }
                if (EchoOtp) devCode = issue.Code;   // yalnız Portal:EchoOtp=true (geliştirme) — üretimde kapalı
            }
        }
        // Genel yanıt (numara kayıtlı olup olmadığını AÇIĞA ÇIKARMAZ).
        return Ok(new { ok = true, message = "Numara kayıtlıysa doğrulama kodu gönderildi.", devCode });
    }

    // ===== 2) Giriş: OTP doğrula =====
    /// <summary>OTP doğrular; başarılıysa müşteri-scope oturum token'ı döner. GİRİŞ token'ı (t) bağlamı taşır.</summary>
    [HttpPost("otp-verify")]
    public IActionResult VerifyOtp([FromQuery] string t, [FromBody] PortalVerifyRequest req)
    {
        var ctx = tokens.ValidateEntry(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (string.IsNullOrWhiteSpace(req?.Phone) || string.IsNullOrWhiteSpace(req?.Code))
            return BadRequest(new { message = "Telefon ve kod giriniz." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);
            var customerId = _portal.VerifyOtp(req.Phone, req.Code);
            if (customerId is null)
                return Unauthorized(new { message = "Kod hatalı veya süresi dolmuş." });

            var customer = new CustomerService().GetById(customerId.Value);
            var session = tokens.CreateSessionToken(ctx.CompanyCode, ctx.DbName, ctx.BranchId, customerId.Value);
            return Ok(new { token = session, name = customer?.Name ?? "" });
        }
    }

    // ===== 3) Puan bakiyesi =====
    /// <summary>Müşteri puan bakiyesi + ₺ karşılığı (oturum token'ıyla; müşteri yalnız kendi bakiyesini görür).</summary>
    [HttpGet("points")]
    public IActionResult Points([FromQuery] string t)
    {
        var ctx = tokens.ValidateSession(t);
        if (ctx is null) return Unauthorized(new { message = "Oturum süresi doldu, tekrar giriş yapın." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);
            var settings = _loyalty.GetSettings();
            decimal points = _loyalty.GetBalance(ctx.CustomerId);
            decimal rate = settings.RedeemRate <= 0 ? 1 : settings.RedeemRate;
            return Ok(new
            {
                active = settings.IsActive,
                points,
                balanceTl = Math.Round(points * rate, 2)
            });
        }
    }

    // ===== 4) Sipariş geçmişi =====
    /// <summary>Müşterinin sipariş geçmişi (kendi cari fişleri), en yeni önce.</summary>
    [HttpGet("history")]
    public IActionResult History([FromQuery] string t)
    {
        var ctx = tokens.ValidateSession(t);
        if (ctx is null) return Unauthorized(new { message = "Oturum süresi doldu, tekrar giriş yapın." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);
            var items = _portal.GetHistory(ctx.CustomerId).Select(h => new
            {
                id = h.ClFicheId,
                code = h.Code,
                total = h.Total,
                paid = h.Paid,
                date = h.FicheDate
            });
            return Ok(new { items });
        }
    }

    /// <summary>Bir geçmiş fişin ürün kalemleri (yeniden-sipariş önizlemesi). Sahiplik doğrulanır → yabancı fiş boş döner.</summary>
    [HttpGet("history-lines/{id:int}")]
    public IActionResult HistoryLines(int id, [FromQuery] string t)
    {
        var ctx = tokens.ValidateSession(t);
        if (ctx is null) return Unauthorized(new { message = "Oturum süresi doldu, tekrar giriş yapın." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);
            var lines = _portal.GetFicheLines(id, ctx.CustomerId)
                .Select(l => new { productId = l.ProductId, name = l.ProductName, qty = l.Qty });
            return Ok(new { lines });
        }
    }

    // ===== 5) Yeniden-sipariş =====
    /// <summary>Geçmiş adisyonu ŞABLON alarak yeni bir PAKET adisyonu açar, güncel menü fiyatlarıyla kalemleri ekler ve
    /// mutfağa gönderir. Fiyat/KDV SUNUCUDAN (menü) çözülür — istemci fiyat gönderemez. Sahiplik doğrulanır (kendi fişi).</summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromQuery] string t, [FromBody] PortalReorderRequest req)
    {
        var ctx = tokens.ValidateSession(t);
        if (ctx is null) return Unauthorized(new { message = "Oturum süresi doldu, tekrar giriş yapın." });
        if (req is null || req.ClFicheId <= 0) return BadRequest(new { message = "Geçersiz sipariş." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx.CompanyCode, ctx.DbName, ctx.BranchId);

            var target = _portal.GetReorderTarget(req.ClFicheId, ctx.CustomerId);
            if (target is null) return NotFound(new { message = "Sipariş bulunamadı." });

            var template = _portal.GetFicheLines(req.ClFicheId, ctx.CustomerId);
            if (template.Count == 0) return BadRequest(new { message = "Yeniden sipariş için uygun ürün yok." });

            // Güncel menüyü bir kez çöz → her ürünü fiyat/KDV/satış-durumu için buradan doğrula (istemci fiyat gönderemez).
            var menu = _catalog.GetProducts(target.Value.BranchId).ToDictionary(p => p.Id);

            var order = _orders.CreateOrder(target.Value.BranchId, target.Value.ZoneId, ctx.CustomerId, Array.Empty<int>(), null);
            _portal.MarkOrderAsPackage(order.Id);

            int added = 0;
            foreach (var line in template)
            {
                if (line.Qty <= 0) continue;
                if (!menu.TryGetValue(line.ProductId, out var prod)) continue;   // menüde olmayan ürün atlanır
                if (prod.IsSalesStopped) continue;                                // satışa kapalı ürün eklenmez
                _orders.AddLine(order.Id, order.ZoneId, prod.Id, line.Qty, prod.Price, prod.TaxRate, "Sadakat portalı yeniden sipariş", 0, null);
                added++;
            }
            if (added == 0) return BadRequest(new { message = "Yeniden sipariş için uygun ürün yok (satışa kapalı olabilir)." });

            var tickets = _orders.SendToKitchen(order.Id, null);
            await notifier.NewKitchenTicket(ctx.CompanyCode, target.Value.BranchId, tickets);
            // HEDEFLİ alarm: mutfak-geneli + reyon-özel KDS'ler (sadakat portalı yeniden sipariş). Mevcut NewTicket tazele KORUNUR.
            await notifier.KitchenNew(ctx.CompanyCode, target.Value.BranchId, tickets.Select(t => t.ReyonId).Distinct(),
                new { tableCode = tickets.FirstOrDefault()?.TableCodes, reyonIds = tickets.Select(t => t.ReyonId).Distinct() });
            await notifier.TableChanged(ctx.CompanyCode, target.Value.BranchId);

            return Ok(new { orderId = order.Id, order.Code, added });
        }
    }
}

// ===== FAZ 5a Portal DTO'ları (Dtos.cs'e dokunulmadı — eşzamanlı faz çakışmasını önlemek için burada) =====
/// <summary>OTP isteği: yalnız telefon (bağlam GİRİŞ token'ından).</summary>
public record PortalOtpRequest(string Phone);
/// <summary>OTP doğrulama: telefon + kod.</summary>
public record PortalVerifyRequest(string Phone, string Code);
/// <summary>Yeniden-sipariş: şablon alınacak geçmiş cari fiş Id'si.</summary>
public record PortalReorderRequest(int ClFicheId);
