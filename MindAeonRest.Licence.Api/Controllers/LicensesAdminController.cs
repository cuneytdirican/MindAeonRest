using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Services;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Endpoints/licenses-ext — ADMIN lisans yönetimi: liste (filtre), upgrade, renew, revoke.
/// Tümü [Authorize(Roles="Admin")] (UI gizleme tek güvenlik değil — yetki API'de zorlanır).
/// Mevcut LicensesController.cs'e DOKUNULMADI; bu ayrı bir denetleyici.
/// </summary>
[ApiController]
[Route("api/v1/admin/licenses")]
[Authorize(Roles = "Admin")]
public class LicensesAdminController(LicenceDbContext db, ILicenseSigner signer) : ControllerBase
{
    private string? CurrentUserId =>
        User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>ADMIN: lisansları listele — paket/durum/işletme/bayi filtreleriyle.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LicenseListItem>>> List([FromQuery] LicenseListQuery q)
    {
        var query = db.Licenses
            .AsNoTracking()
            .Include(l => l.Business)
            .Include(l => l.Dealer)
            .AsQueryable();

        if (q.Package is { } pkg) query = query.Where(l => l.Package == pkg);
        if (q.Status is { } st) query = query.Where(l => l.Status == st);
        if (q.BusinessId is { } bid) query = query.Where(l => l.BusinessId == bid);
        if (q.DealerId is { } did) query = query.Where(l => l.DealerId == did);

        var items = await query
            .OrderByDescending(l => l.IssuedUtc)
            .Select(l => new LicenseListItem(
                l.Id,
                l.LicenseCode,
                l.Package,
                PackageMatrix.Name((LicensePackage)l.Package),
                l.Status,
                l.DealerId,
                l.Dealer != null ? l.Dealer.Name : null,
                l.BusinessId,
                l.Business != null ? l.Business.Title : null,
                l.Business != null ? l.Business.CompanyCode : "",
                l.HardwareId,
                l.MaxBranches,
                l.MaxUsers,
                l.IssuedUtc,
                l.ExpiresUtc))
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>ADMIN: lisans detayı (token dahil) — tek kayıt.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<License>> Get(int id)
    {
        var lic = await db.Licenses
            .AsNoTracking()
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (lic is null) return NotFound(new { message = "Lisans bulunamadı." });
        return Ok(lic);
    }

    /// <summary>
    /// ADMIN: lisansı yeni pakete yükselt. Yeni imzalı token üretilir; YENİ License kaydı (Active)
    /// + History.Upgraded; eski lisans Status=Upgraded. Donanım/işletme bağı korunur.
    /// </summary>
    [HttpPost("{id:int}/upgrade")]
    public async Task<ActionResult<UpgradeLicenseResp>> Upgrade(int id, [FromBody] UpgradeLicenseReq req)
    {
        var old = await db.Licenses
            .Include(l => l.Business)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (old is null) return NotFound(new { message = "Lisans bulunamadı." });

        if (old.Status is LicenseStatus.Revoked or LicenseStatus.Upgraded)
            return BadRequest(new { message = "İptal edilmiş veya zaten yükseltilmiş lisans yükseltilemez." });

        if (!Enum.IsDefined(typeof(LicensePackage), req.Package))
            return BadRequest(new { message = "Geçersiz paket." });
        if (req.Package == old.Package)
            return BadRequest(new { message = "Hedef paket mevcut paketle aynı." });
        if (string.IsNullOrWhiteSpace(old.HardwareId))
            return BadRequest(new { message = "Lisansın donanım kimliği yok; yükseltme imzalanamaz." });

        // Süreli (Aylık/Yıllık) yükseltmede bitiş tarihi zorunlu ve gelecekte olmalı; aksi halde kazara süresiz üretilir.
        // (Upgrade'de tarih varsa süreli kabul edilir; null ise süresiz korunur.)
        if (req.ExpiresUtc is { } upExp && upExp <= DateTime.UtcNow)
            return BadRequest(new { message = "Yükseltmede bitiş tarihi (ExpiresUtc) gelecekte olmalıdır." });

        var newPkg = (LicensePackage)req.Package;
        var code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var addOns = (Feature)req.AddOnFeatures;
        var features = PackageMatrix.Features(newPkg) | addOns;
        bool multiBranch = features.HasFlag(Feature.MultiBranch);
        int maxUsers = PackageMatrix.UsersWith(newPkg, req.ExtraUsers);
        int maxKiosks = PackageMatrix.KiosksWith(newPkg, req.ExtraKiosks);
        int maxBranches = PackageMatrix.BranchesWith(newPkg, req.ExtraBranches, multiBranch);
        var byUser = CurrentUserId;

        // Terminal (KIOSK) limiti: işletmenin BU donanım DIŞINDAKİ farklı donanımlardaki aktif lisansları
        // toplam terminali aşmasın (LicensesController.Issue ile AYNI kural). Hedef lisansın donanımı hariç tutulur.
        if (maxKiosks != int.MaxValue)
        {
            int activeOther = await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == old.BusinessId && l.Status == LicenseStatus.Active && l.HardwareId != old.HardwareId)
                .Select(l => l.HardwareId).Distinct().CountAsync();
            if (activeOther >= maxKiosks)
                return BadRequest(new { message = $"Bu paket en fazla {maxKiosks} terminal (KIOSK) içindir; işletmede {activeOther} aktif terminal var. Paketi yükseltin veya bir terminali iptal edin." });
        }

        var companyCode = old.Business?.CompanyCode ?? "";

        var payload = new LicensePayload
        {
            LicenseId = code,
            CompanyCode = companyCode,
            HardwareId = old.HardwareId,
            Package = newPkg,
            MaxBranches = maxBranches,
            MaxUsers = maxUsers,
            MaxKiosks = maxKiosks,
            FeatureBits = (long)features,
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = req.ExpiresUtc
        };
        var token = signer.SignLicense(payload);

        var lic = new License
        {
            BusinessId = old.BusinessId,
            DealerId = old.DealerId,
            LicenseCode = code,
            Package = req.Package,
            MaxBranches = payload.MaxBranches,
            MaxUsers = payload.MaxUsers,
            MaxKiosks = maxKiosks,
            FeatureBits = payload.FeatureBits,
            AddOnFeatures = req.AddOnFeatures,
            ExtraUsers = req.ExtraUsers,
            ExtraKiosks = req.ExtraKiosks,
            ExtraBranches = req.ExtraBranches,
            HardwareId = old.HardwareId,
            IssuedUtc = payload.IssuedUtc,
            ExpiresUtc = payload.ExpiresUtc,
            Status = LicenseStatus.Active,
            LicenseToken = token,
            Notes = req.Note,
            IssuedByUserId = byUser
        };
        lic.History.Add(new LicenseHistory
        {
            Action = LicenseAction.Upgraded,
            FromPackage = old.Package,
            ToPackage = req.Package,
            ByUserId = byUser,
            Note = req.Note
        });

        // Eski lisansı yükseltilmiş olarak işaretle + iz bırak.
        old.Status = LicenseStatus.Upgraded;
        old.History.Add(new LicenseHistory
        {
            Action = LicenseAction.Upgraded,
            FromPackage = old.Package,
            ToPackage = req.Package,
            ByUserId = byUser,
            Note = req.Note
        });

        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        return Ok(new UpgradeLicenseResp(lic.Id, code, token, req.Package, lic.IssuedUtc, lic.ExpiresUtc));
    }

    /// <summary>
    /// ADMIN: lisansı yenile — yeni bitiş tarihiyle token yeniden imzalanır (aynı paket/kod/donanım).
    /// Status Active'e döner, History.Renewed eklenir.
    /// </summary>
    [HttpPost("{id:int}/renew")]
    public async Task<ActionResult<RenewLicenseResp>> Renew(int id, [FromBody] RenewLicenseReq req)
    {
        var lic = await db.Licenses
            .Include(l => l.Business)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (lic is null) return NotFound(new { message = "Lisans bulunamadı." });

        if (lic.Status is LicenseStatus.Revoked or LicenseStatus.Upgraded)
            return BadRequest(new { message = "İptal edilmiş veya yükseltilmiş lisans yenilenemez." });
        if (string.IsNullOrWhiteSpace(lic.HardwareId))
            return BadRequest(new { message = "Lisansın donanım kimliği yok; yenileme imzalanamaz." });

        // Yenileme süre uzatmaktır: bitiş tarihi verilmişse gelecekte olmalı (süresi geçmiş bir tarihle
        // Expired→Active diriltmeyi engelle). Null verilirse lisans süresiz yenilenir.
        if (req.ExpiresUtc is { } reExp && reExp <= DateTime.UtcNow)
            return BadRequest(new { message = "Yenilemede bitiş tarihi (ExpiresUtc) gelecekte olmalıdır." });

        var pkg = (LicensePackage)lic.Package;
        var byUser = CurrentUserId;

        // Yenilemede MaxKiosks/add-on/ekstra KORUNUR (paket tabanına düşürülmez).
        int renewKiosks = lic.MaxKiosks > 0 ? lic.MaxKiosks : PackageMatrix.MaxKiosks(pkg);

        // Terminal (KIOSK) limit tutarlılığını yeniden doğrula: işletmenin BU donanım DIŞINDAKİ
        // farklı donanımlardaki aktif lisansları, bu lisansın terminal limitini aşmasın
        // (Issue/Approve/Upgrade ile AYNI kural — yenileme limit denetimini atlatmasın).
        if (renewKiosks != int.MaxValue)
        {
            int activeOther = await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == lic.BusinessId && l.Id != lic.Id
                            && l.Status == LicenseStatus.Active && l.HardwareId != lic.HardwareId)
                .Select(l => l.HardwareId).Distinct().CountAsync();
            if (activeOther >= renewKiosks)
                return BadRequest(new { message = $"Bu lisans en fazla {renewKiosks} terminal (KIOSK) içindir; işletmede {activeOther} başka aktif terminal var. Yenileme limit aşımına yol açar; önce bir terminali iptal edin." });
        }

        var payload = new LicensePayload
        {
            LicenseId = lic.LicenseCode,
            CompanyCode = lic.Business?.CompanyCode ?? "",
            HardwareId = lic.HardwareId,
            Package = pkg,
            MaxBranches = lic.MaxBranches,
            MaxUsers = lic.MaxUsers,
            MaxKiosks = renewKiosks,   // yenilemede add-on/ekstra korunur
            FeatureBits = lic.FeatureBits,
            IssuedUtc = lic.IssuedUtc,
            ExpiresUtc = req.ExpiresUtc
        };
        var token = signer.SignLicense(payload);

        lic.ExpiresUtc = req.ExpiresUtc;
        lic.Status = LicenseStatus.Active;
        lic.LicenseToken = token;
        lic.History.Add(new LicenseHistory
        {
            Action = LicenseAction.Renewed,
            FromPackage = lic.Package,
            ToPackage = lic.Package,
            ByUserId = byUser,
            Note = req.Note
        });

        await db.SaveChangesAsync();

        return Ok(new RenewLicenseResp(lic.Id, lic.LicenseCode, token, lic.ExpiresUtc));
    }

    /// <summary>ADMIN: lisansı iptal et — Status=Revoked + History.Revoked. Heartbeat'te etkisini gösterir.</summary>
    [HttpPost("{id:int}/revoke")]
    public async Task<ActionResult<RevokeLicenseResp>> Revoke(int id, [FromBody] RevokeLicenseReq req)
    {
        var lic = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id);
        if (lic is null) return NotFound(new { message = "Lisans bulunamadı." });

        if (lic.Status == LicenseStatus.Revoked)
            return BadRequest(new { message = "Lisans zaten iptal edilmiş." });

        lic.Status = LicenseStatus.Revoked;
        lic.History.Add(new LicenseHistory
        {
            Action = LicenseAction.Revoked,
            FromPackage = lic.Package,
            ByUserId = CurrentUserId,
            Note = req.Note
        });

        await db.SaveChangesAsync();

        return Ok(new RevokeLicenseResp(lic.Id, lic.LicenseCode, lic.Status));
    }
}
