using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Services;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Lisans/yükseltme talepleri (LicenseRequest).
/// - Admin: tüm talepleri (varsayılan Pending kuyruğu) görür; onaylar (lisans üretir) veya reddeder.
/// - Bayi: SADECE kendi taleplerini görür ve "yükseltme talebi" gönderir; LİSANS ÜRETEMEZ.
/// Tenant izolasyonu hem DbContext query-filter hem de açık [Authorize]/sahiplik ile zorlanır.
/// </summary>
[ApiController]
[Route("api/v1/requests")]
[Authorize]
public class RequestsController(
    LicenceDbContext db,
    ILicenseSigner signer,
    UserManager<ApplicationUser> users) : ControllerBase
{
    private string? CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

    private static string PackageName(int package) =>
        PackageMatrix.Name((LicensePackage)package);

    private static RequestListItem ToItem(LicenseRequest r) => new(
        r.Id, r.BusinessId, r.DealerId, r.CompanyCode, r.HardwareId,
        r.RequestedPackage, PackageName(r.RequestedPackage), r.Status,
        r.IssuedLicenseId, r.CreatedUtc, r.UpdatedUtc);

    /// <summary>
    /// ADMIN: talep listesi. Varsayılan: Pending kuyruğu. status=null verilirse tüm durumlar.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<RequestListItem>>> List([FromQuery] RequestStatus? status = RequestStatus.Pending)
    {
        var q = db.LicenseRequests.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(r => r.Status == s);
        var items = await q.OrderBy(r => r.Status).ThenByDescending(r => r.CreatedUtc)
            .Select(r => ToItem(r)).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// BAYI: kendi talepleri. Query-filter zaten DealerId'ye göre kısıtlar; ayrıca açıkça filtrelenir.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = "Dealer")]
    public async Task<ActionResult<IReadOnlyList<RequestListItem>>> Mine([FromQuery] RequestStatus? status = null)
    {
        var user = await users.FindByIdAsync(CurrentUserId ?? "");
        if (user?.DealerId is not { } dealerId)
            return Forbid();

        var q = db.LicenseRequests.AsNoTracking().Where(r => r.DealerId == dealerId);
        if (status is { } s) q = q.Where(r => r.Status == s);
        var items = await q.OrderByDescending(r => r.CreatedUtc)
            .Select(r => ToItem(r)).ToListAsync();
        return Ok(items);
    }

    /// <summary>Tek talep — sahiplik query-filter ile zorlanır (Bayi başkasının talebini göremez).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RequestListItem>> Get(int id)
    {
        var r = await db.LicenseRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Talep bulunamadı." });
        return Ok(ToItem(r));
    }

    /// <summary>
    /// BAYI: yükseltme/lisans talebi gönder. Bayi lisansı KENDİ üretemez; talep Pending olarak kuyruğa girer.
    /// Talep, oturum açan bayinin DealerId'sine bağlanır (gönderilen değere güvenilmez).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Dealer")]
    public async Task<ActionResult<CreateRequestResp>> Create([FromBody] CreateRequestReq req)
    {
        var user = await users.FindByIdAsync(CurrentUserId ?? "");
        if (user?.DealerId is not { } dealerId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(req.CompanyCode))
            return BadRequest(new { message = "Şirket kodu zorunludur." });
        if (string.IsNullOrWhiteSpace(req.HardwareId))
            return BadRequest(new { message = "Donanım kimliği zorunludur." });

        // İşletme verildiyse bayiye ait olduğunu doğrula (query-filter zaten DealerId'ye kısıtlar).
        int? businessId = null;
        if (req.BusinessId is { } bid)
        {
            var biz = await db.Businesses.FirstOrDefaultAsync(x => x.Id == bid);
            if (biz is null || biz.DealerId != dealerId)
                return BadRequest(new { message = "İşletme bulunamadı veya bu bayiye ait değil." });
            businessId = biz.Id;
        }

        // Aynı donanım için bekleyen talep varsa tekrarı önle.
        var existing = await db.LicenseRequests
            .FirstOrDefaultAsync(r => r.HardwareId == req.HardwareId && r.Status == RequestStatus.Pending);
        if (existing is not null)
            return Ok(new CreateRequestResp(existing.Id, existing.Status, "Bu donanım için zaten bekleyen bir talep var."));

        var entity = new LicenseRequest
        {
            BusinessId = businessId,
            DealerId = dealerId,
            CompanyCode = req.CompanyCode.Trim(),
            HardwareId = req.HardwareId.Trim(),
            RequestedPackage = req.RequestedPackage,
            RequestToken = "",
            Status = RequestStatus.Pending
        };
        db.LicenseRequests.Add(entity);
        await db.SaveChangesAsync();

        return Ok(new CreateRequestResp(entity.Id, entity.Status, "Yükseltme talebiniz alındı; onay sonrası lisans üretilecektir."));
    }

    /// <summary>
    /// ADMIN: talebi onayla → ILicenseSigner ile imzalı lisans üret, License + History kaydet,
    /// RequestStatus=Issued ve IssuedLicenseId set et. Bayi BU UCU ÇAĞIRAMAZ.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApproveRequestResp>> Approve(int id, [FromBody] ApproveRequestReq req)
    {
        var reqEntity = await db.LicenseRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (reqEntity is null) return NotFound(new { message = "Talep bulunamadı." });
        if (reqEntity.Status != RequestStatus.Pending)
            return Conflict(new { message = $"Talep işlenemez (durum: {reqEntity.Status})." });
        if (string.IsNullOrWhiteSpace(reqEntity.HardwareId))
            return BadRequest(new { message = "Talebin donanım kimliği yok." });

        // Süreli (Aylık/Yıllık) lisansta bitiş tarihi zorunlu ve gelecekte olmalı (yanlışlıkla süresiz üretmeyi engelle).
        if (req.Duration != DurationKind.Perpetual && (req.ExpiresUtc is not { } expChk || expChk <= DateTime.UtcNow))
            return BadRequest(new { message = "Süreli (Aylık/Yıllık) lisans için gelecekte bir bitiş tarihi (ExpiresUtc) zorunludur." });

        // İşletmeyi bul/oluştur ve donanımı bağla.
        Business? biz = null;
        if (reqEntity.BusinessId is { } bid)
            biz = await db.Businesses.FirstOrDefaultAsync(x => x.Id == bid);
        biz ??= await db.Businesses.FirstOrDefaultAsync(x => x.CompanyCode == reqEntity.CompanyCode);
        if (biz is null)
        {
            biz = new Business
            {
                CompanyCode = reqEntity.CompanyCode,
                Title = reqEntity.CompanyCode,
                DealerId = reqEntity.DealerId,
                HardwareId = reqEntity.HardwareId
            };
            db.Businesses.Add(biz);
            await db.SaveChangesAsync(); // biz.Id üret
        }
        else if (string.IsNullOrWhiteSpace(biz.HardwareId))
        {
            biz.HardwareId = reqEntity.HardwareId;
        }

        // Tek (BusinessId+HardwareId) için aynı anda yalnız bir Active lisans olur (LicensesController.Issue ile AYNI kural).
        // Aynı donanımda hâlâ aktif lisans varsa mükerrer üretimi engelle; yenileme için önce iptal/yükseltme gerekir.
        bool dupActive = await db.Licenses.IgnoreQueryFilters()
            .AnyAsync(l => !l.IsDeleted && l.BusinessId == biz.Id && l.HardwareId == biz.HardwareId && l.Status == LicenseStatus.Active);
        if (dupActive)
            return Conflict(new { message = "Bu işletme + donanım için zaten aktif bir lisans var. Önce mevcut lisansı iptal/yükseltin." });

        var pkg = (LicensePackage)reqEntity.RequestedPackage;
        var code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var addOns = (Feature)req.AddOnFeatures;
        var features = PackageMatrix.Features(pkg) | addOns;
        bool multiBranch = features.HasFlag(Feature.MultiBranch);
        int maxUsers = PackageMatrix.UsersWith(pkg, req.ExtraUsers);
        int maxKiosks = PackageMatrix.KiosksWith(pkg, req.ExtraKiosks);
        int maxBranches = PackageMatrix.BranchesWith(pkg, req.ExtraBranches, multiBranch);

        // Terminal (KIOSK) limiti: işletmenin BU donanım DIŞINDAKİ farklı donanımlardaki aktif lisansları
        // toplam terminali aşmasın (LicensesController.Issue ile AYNI kural).
        if (maxKiosks != int.MaxValue)
        {
            int activeOther = await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == biz.Id && l.Status == LicenseStatus.Active && l.HardwareId != biz.HardwareId)
                .Select(l => l.HardwareId).Distinct().CountAsync();
            if (activeOther >= maxKiosks)
                return BadRequest(new { message = $"Bu paket en fazla {maxKiosks} terminal (KIOSK) içindir; işletmede {activeOther} aktif terminal var. Paketi yükseltin veya bir terminali iptal edin." });
        }

        int? dealerId = biz.DealerId ?? reqEntity.DealerId;
        string? dealerCode = dealerId is int did ? (await db.Dealers.FindAsync(did))?.Code : null;

        var payload = new LicensePayload
        {
            LicenseId = code,
            CompanyCode = biz.CompanyCode,
            HardwareId = biz.HardwareId!,
            DealerCode = string.IsNullOrWhiteSpace(dealerCode) ? null : dealerCode,
            Package = pkg,
            MaxBranches = maxBranches,
            MaxUsers = maxUsers,
            MaxKiosks = maxKiosks,
            FeatureBits = (long)features,
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = req.Duration == DurationKind.Perpetual ? null : req.ExpiresUtc
        };
        var token = signer.SignLicense(payload);

        var lic = new License
        {
            BusinessId = biz.Id,
            DealerId = dealerId,
            LicenseCode = code,
            Package = reqEntity.RequestedPackage,
            MaxBranches = payload.MaxBranches,
            MaxUsers = payload.MaxUsers,
            MaxKiosks = maxKiosks,
            FeatureBits = payload.FeatureBits,
            AddOnFeatures = req.AddOnFeatures,
            ExtraUsers = req.ExtraUsers,
            ExtraKiosks = req.ExtraKiosks,
            ExtraBranches = req.ExtraBranches,
            HardwareId = biz.HardwareId!,
            IssuedUtc = payload.IssuedUtc,
            ExpiresUtc = payload.ExpiresUtc,
            Status = LicenseStatus.Active,
            LicenseToken = token,
            Notes = req.Notes,
            IssuedByUserId = CurrentUserId
        };
        lic.History.Add(new LicenseHistory
        {
            Action = LicenseAction.Issued,
            ToPackage = reqEntity.RequestedPackage,
            ByUserId = CurrentUserId,
            Note = "Talep onayı ile üretildi."
        });
        db.Licenses.Add(lic);
        await db.SaveChangesAsync(); // lic.Id üret

        reqEntity.Status = RequestStatus.Issued;
        reqEntity.IssuedLicenseId = lic.Id;
        await db.SaveChangesAsync();

        return Ok(new ApproveRequestResp(
            reqEntity.Id, lic.Id, code, token, reqEntity.RequestedPackage, lic.IssuedUtc, lic.ExpiresUtc));
    }

    /// <summary>ADMIN: talebi reddet (Status=Rejected). Bayi BU UCU ÇAĞIRAMAZ.</summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RejectRequestResp>> Reject(int id, [FromBody] RejectRequestReq? req = null)
    {
        var reqEntity = await db.LicenseRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (reqEntity is null) return NotFound(new { message = "Talep bulunamadı." });
        if (reqEntity.Status != RequestStatus.Pending)
            return Conflict(new { message = $"Talep işlenemez (durum: {reqEntity.Status})." });

        reqEntity.Status = RequestStatus.Rejected;
        await db.SaveChangesAsync();

        return Ok(new RejectRequestResp(reqEntity.Id, reqEntity.Status));
    }
}
