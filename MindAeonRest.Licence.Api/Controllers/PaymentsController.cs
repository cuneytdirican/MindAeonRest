using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Api.Payments;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Ödeme başlatma + havale onayı + listeleme.
///
/// Şu an yalnızca Banka Havalesi/EFT (BankTransfer) etkin tahsilat yöntemidir: müşteri ödeme
/// başlatır (Pending), admin dekontu eşleştirip onaylar (Paid). Online PSP yöntemleri
/// (İyzico/PayTR/Garanti VPOS) etkin değildir ve başlatma denenirse net hata döner.
///
/// GÜVENLİK İLKELERİ:
///  - Tutar HER ZAMAN sunucuda PackagePrice (paket+süre) + hedef bayinin MarginPercent'inden
///    hesaplanır; istemcinin gönderdiği tutar KABUL EDİLMEZ (DTO'da tutar alanı yoktur).
///  - Payment kaydı önce Pending olarak açılır (Amount/MarginAmount/NetAmount sabitlenir).
///  - Havale onayı idempotenttir: zaten Paid ise tekrar işlenmez.
///  - Yetki: Admin tümünü; Bayi/Müşteri sadece kendi tenant verisini görür (query filter + kontrol).
///
/// DI NOTU: PaymentConfigProtector + PaymentProviderFactory burada elle kurulur
/// (Program.cs'e dokunulmaması kuralı). IDataProtectionProvider çerçeveden gelir.
/// </summary>
[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly LicenceDbContext _db;
    private readonly PaymentProviderFactory _factory;

    public PaymentsController(LicenceDbContext db, IDataProtectionProvider dp)
    {
        _db = db;
        var protector = new PaymentConfigProtector(dp);
        _factory = new PaymentProviderFactory(db, protector);
    }

    private bool IsAdmin => User.IsInRole("Admin");
    private int? CurrentDealerId => int.TryParse(User.FindFirst(JwtTokenService.ClaimDealer)?.Value, out var d) ? d : null;
    private int? CurrentBusinessId => int.TryParse(User.FindFirst(JwtTokenService.ClaimBusiness)?.Value, out var b) ? b : null;
    private string? CurrentUserId => User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

    // ------------------------------------------------------------------ ÖDEME BAŞLAT

    /// <summary>
    /// Ödeme başlatır. Tutar sunucuda hesaplanır (istemci tutarı yok).
    /// Pending Payment kaydı açılır; sağlayıcıya göre yönlendirme/HTML döner.
    /// </summary>
    [HttpPost("start")]
    [Authorize(Roles = "Admin,Dealer,Customer")]
    public async Task<ActionResult<StartPaymentResp>> Start([FromBody] StartPaymentReq req, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(PaymentProviderKind), req.Provider))
            return BadRequest(new { message = "Geçersiz ödeme sağlayıcısı." });
        // Yalnızca Banka Havalesi/EFT etkindir; online PSP yöntemleri kapalıdır (fail-closed).
        if (!_factory.IsAvailable(req.Provider))
            return BadRequest(new { message = "Bu ödeme yöntemi şu an etkin değil. Lütfen Banka Havalesi/EFT kullanın." });
        if (!Enum.IsDefined(typeof(LicensePackage), req.Package))
            return BadRequest(new { message = "Geçersiz paket." });
        if (!Enum.IsDefined(typeof(DurationKind), req.Duration))
            return BadRequest(new { message = "Geçersiz süre." });

        // --- Hedef işletme/bayi çöz (tenant kısıtı) ---
        int? businessId = req.BusinessId;
        int? dealerId;

        if (IsAdmin)
        {
            // Admin BusinessId verirse onun bayisini; vermezse req üzerinden bayi yok say.
            if (businessId is { } abid)
            {
                var biz = await _db.Businesses.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == abid && !x.IsDeleted, ct);
                if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });
                dealerId = biz.DealerId;
            }
            else dealerId = null;
        }
        else if (User.IsInRole("Dealer"))
        {
            dealerId = CurrentDealerId;
            if (dealerId is null) return Forbid();
            // Bayi yalnız kendi işletmesi için ödeme başlatabilir.
            if (businessId is { } dbid)
            {
                bool owns = await _db.Businesses.AsNoTracking() // query filter zaten bayiye kısıtlar
                    .AnyAsync(x => x.Id == dbid, ct);
                if (!owns) return Forbid();
            }
        }
        else // Customer (en dar): yalnız kendi işletmesi
        {
            businessId = CurrentBusinessId;
            if (businessId is null) return Forbid();
            dealerId = await _db.Businesses.AsNoTracking()
                .Where(x => x.Id == businessId)
                .Select(x => x.DealerId)
                .FirstOrDefaultAsync(ct);
        }

        // --- Taban fiyat (paket + süre) ---
        var price = await _db.PackagePrices.AsNoTracking()
            .Where(p => p.Package == req.Package && p.Duration == req.Duration && p.IsActive && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);
        if (price is null)
            return BadRequest(new { message = "Bu paket ve süre için aktif fiyat tanımı yok." });

        // --- Marj (bayiden) → brüt/marj/net (SUNUCUDA) ---
        decimal marginPercent = 0m;
        BillingMode billingMode = BillingMode.CustomerPaysUs;
        if (dealerId is { } did)
        {
            var dealer = await _db.Dealers.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == did && !x.IsDeleted, ct);
            if (dealer is not null)
            {
                marginPercent = dealer.MarginPercent;
                billingMode = dealer.BillingMode;
            }
        }

        decimal baseAmount = price.BasePrice;
        decimal gross = decimal.Round(baseAmount * (1 + marginPercent / 100m), 2, MidpointRounding.AwayFromZero);
        decimal margin = decimal.Round(gross - baseAmount, 2, MidpointRounding.AwayFromZero);
        decimal net = baseAmount; // bizim net = taban; marj bayinin payı

        // --- Pending Payment kaydı ---
        var payment = new Payment
        {
            DealerId = dealerId,
            BusinessId = businessId,
            LicenseId = req.LicenseId,
            Provider = req.Provider,
            BillingMode = billingMode,
            Amount = gross,
            MarginAmount = margin,
            NetAmount = net,
            CurrencyCode = string.IsNullOrWhiteSpace(price.CurrencyCode) ? "TRY" : price.CurrencyCode,
            Status = PaymentStatus.Pending,
            CreatedByUserId = CurrentUserId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        // --- Sağlayıcı çağrısı ---
        var provider = await _factory.CreateAsync(req.Provider, dealerId, ct);
        var meta = new PaymentMeta(payment.Id, dealerId, businessId, req.LicenseId, req.Package, req.Duration, User.FindFirst("name")?.Value);
        var result = await provider.CreateAsync(gross, payment.CurrencyCode, req.ReturnUrl, meta);

        // Sağlayıcı anında hata verdiyse kaydı Failed yap.
        if (result.Status == PaymentStatus.Failed)
        {
            payment.Status = PaymentStatus.Failed;
            payment.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrWhiteSpace(result.ProviderRef))
        {
            payment.ProviderRef = result.ProviderRef;
            payment.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new StartPaymentResp(
            payment.Id, payment.Status, payment.Provider,
            payment.Amount, payment.MarginAmount, payment.NetAmount, payment.CurrencyCode,
            payment.ProviderRef, result.RedirectUrl, result.Html, result.Message));
    }

    // ------------------------------------------------------------------ 3DS CALLBACK (ETKİN DEĞİL)

    /// <summary>
    /// Online PSP 3DS dönüşü için ayrılmış uç. Etkin online sağlayıcı bulunmadığından bu uca
    /// hiçbir banka/PSP yönlendirme yapmaz ve uç kapalıdır: her istek 410 Gone döner.
    /// (Yalnızca Banka Havalesi/EFT etkindir; havale onayı admin tarafından yapılır.)
    /// </summary>
    [HttpPost("callback/{provider}")]
    [AllowAnonymous]
    public IActionResult Callback(PaymentProviderKind provider)
        => StatusCode(StatusCodes.Status410Gone, new { message = "Bu ödeme yöntemi şu an etkin değil." });

    // ------------------------------------------------------------------ HAVALE ONAY (ADMIN)

    /// <summary>
    /// ADMIN: manuel banka havalesini onaylar (dekont eşleşince Pending → Paid).
    /// İdempotent: zaten Paid ise tekrar işlenmez.
    /// </summary>
    [HttpPost("{id:int}/approve-bank-transfer")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApproveBankTransfer(int id, [FromBody] ApproveBankTransferReq req, CancellationToken ct)
    {
        var payment = await _db.Payments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (payment is null) return NotFound(new { message = "Ödeme bulunamadı." });
        if (payment.Provider != PaymentProviderKind.BankTransfer)
            return BadRequest(new { message = "Bu ödeme banka havalesi değil." });
        if (payment.Status == PaymentStatus.Paid)
            return Ok(new { message = "Ödeme zaten onaylı.", paymentId = payment.Id });
        if (payment.Status is PaymentStatus.Refunded or PaymentStatus.Cancelled)
            return BadRequest(new { message = "İade/iptal edilmiş ödeme onaylanamaz." });

        payment.Status = PaymentStatus.Paid;
        payment.PaidUtc = DateTime.UtcNow;
        payment.ProviderRef = string.IsNullOrWhiteSpace(req.ProviderRef) ? $"BT-{payment.Id}" : req.ProviderRef.Trim();
        payment.UpdatedUtc = DateTime.UtcNow;
        payment.UpdatedByUserId = CurrentUserId;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Havale onaylandı.", paymentId = payment.Id });
    }

    // ------------------------------------------------------------------ LİSTE

    /// <summary>
    /// Ödeme listesi (tenant bazlı): Admin tümünü; Bayi kendi; Müşteri sadece kendi işletmesini.
    /// Query filter zaten Payment'ı tenant'a kısıtlar; ek savunma için açık filtre de uygulanır.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Dealer,Customer")]
    public async Task<ActionResult<IReadOnlyList<PaymentResp>>> List(CancellationToken ct)
    {
        var q = _db.Payments.AsNoTracking().Where(p => !p.IsDeleted);

        if (!IsAdmin)
        {
            if (User.IsInRole("Dealer"))
            {
                var dealerId = CurrentDealerId;
                if (dealerId is null) return Forbid();
                q = q.Where(p => p.DealerId == dealerId);
            }
            else
            {
                var businessId = CurrentBusinessId;
                if (businessId is null) return Forbid();
                q = q.Where(p => p.BusinessId == businessId);
            }
        }

        var rows = await q.OrderByDescending(p => p.CreatedUtc).Select(p => new PaymentResp(
            p.Id, p.DealerId, p.BusinessId, p.LicenseId, p.Provider, p.BillingMode,
            p.Amount, p.MarginAmount, p.NetAmount, p.CurrencyCode, p.Status,
            p.ProviderRef, p.PaidUtc, p.CreatedUtc)).ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Tek ödeme detayı (tenant query filter ile sahiplik zorlanır).</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin,Dealer,Customer")]
    public async Task<ActionResult<PaymentResp>> Get(int id, CancellationToken ct)
    {
        var p = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (p is null) return NotFound(new { message = "Ödeme bulunamadı." });
        return Ok(new PaymentResp(
            p.Id, p.DealerId, p.BusinessId, p.LicenseId, p.Provider, p.BillingMode,
            p.Amount, p.MarginAmount, p.NetAmount, p.CurrencyCode, p.Status,
            p.ProviderRef, p.PaidUtc, p.CreatedUtc));
    }
}
