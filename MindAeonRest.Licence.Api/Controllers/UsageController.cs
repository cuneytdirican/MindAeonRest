using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Masaüstünden periyodik kullanım/ciro raporu alımı + tenant bazlı listeleme.
/// </summary>
[ApiController]
[Route("api/v1/usage")]
public class UsageController(LicenceDbContext db) : ControllerBase
{
    private bool IsAdmin => User.IsInRole("Admin");

    private int? CurrentDealerId =>
        int.TryParse(User.FindFirst(JwtTokenService.ClaimDealer)?.Value, out var d) ? d : null;

    private int? CurrentBusinessId =>
        int.TryParse(User.FindFirst(JwtTokenService.ClaimBusiness)?.Value, out var b) ? b : null;

    /// <summary>
    /// Masaüstü: periyodik kullanım/ciro raporu gönderir. CompanyCode → Business eşlenir,
    /// (BusinessId, Period) anahtarıyla idempotent upsert yapılır.
    ///
    /// GÜVENLİK (cihaz kimliği): Bu uç açık (masaüstü JWT taşımaz) ama CompanyCode tek başına
    /// güvenilmez — aksi halde herkes herhangi bir işletme için sahte kullanım yazabilir.
    /// Bu yüzden cihaz, sahip olduğu lisansın HardwareId'sini de göndermek ZORUNDADIR ve
    /// (CompanyCode + HardwareId) için Active bir lisans satırı DB'de DOĞRULANIR. Lisansın
    /// imzalı token'ı yalnız o donanıma üretildiğinden, eşleşen HardwareId fiili cihaz kanıtıdır.
    /// Eşleşme yoksa istek reddedilir (sahte/yetkisiz rapor engellenir).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<UsageReportResp>> Report([FromBody] UsageReportReq req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyCode))
            return BadRequest(new { message = "Şirket kodu zorunludur." });
        if (string.IsNullOrWhiteSpace(req.HardwareId))
            return BadRequest(new { message = "Donanım kimliği (HardwareId) zorunludur." });
        if (string.IsNullOrWhiteSpace(req.Period))
            return BadRequest(new { message = "Dönem (Period) zorunludur. Örn: 2026-06." });

        var companyCode = req.CompanyCode.Trim();
        var hardwareId = req.HardwareId.Trim();
        var period = req.Period.Trim();

        // CompanyCode → Business eşle (varsa). ANONİM cihaz ucu → tenant filtresini ATLA.
        var biz = await db.Businesses.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompanyCode == companyCode && !b.IsDeleted);
        int? businessId = biz?.Id;

        // Cihaz kimliği doğrulaması: (CompanyCode + HardwareId) için Active lisans olmadan rapor kabul edilmez.
        bool deviceVerified = await db.Licenses.IgnoreQueryFilters().AnyAsync(l =>
            !l.IsDeleted
            && l.Status == LicenseStatus.Active
            && l.HardwareId == hardwareId
            && l.Business != null && l.Business.CompanyCode == companyCode);
        if (!deviceVerified)
            return Unauthorized(new { message = "Cihaz doğrulanamadı: bu şirket kodu ve donanım için aktif bir lisans bulunamadı." });

        // Idempotent upsert: aynı (BusinessId, Period) raporu varsa güncelle (tenant filtresi atlanır).
        UsageReport? existing = businessId is not null
            ? await db.UsageReports.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.BusinessId == businessId && u.Period == period && !u.IsDeleted)
            : await db.UsageReports.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.BusinessId == null && u.CompanyCode == companyCode && u.Period == period && !u.IsDeleted);

        if (existing is null)
        {
            existing = new UsageReport
            {
                BusinessId = businessId,
                CompanyCode = companyCode,
                Period = period
            };
            db.UsageReports.Add(existing);
        }
        else
        {
            existing.UpdatedUtc = DateTime.UtcNow;
        }

        existing.BusinessId = businessId;
        existing.ReportedUtc = DateTime.UtcNow;
        existing.BranchCount = req.BranchCount;
        existing.UserCount = req.UserCount;
        existing.KioskCount = req.KioskCount;
        existing.Revenue = req.Revenue;

        await db.SaveChangesAsync();
        return Ok(Map(existing));
    }

    /// <summary>
    /// Kullanım raporu listesi (tenant bazlı):
    /// Admin tümünü; Bayi kendi işletmelerini; Müşteri sadece kendi işletmesini görür.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Dealer,Customer")]
    public async Task<ActionResult<List<UsageReportResp>>> List()
    {
        var q = db.UsageReports.Where(u => !u.IsDeleted);

        if (!IsAdmin)
        {
            if (User.IsInRole("Dealer"))
            {
                var dealerId = CurrentDealerId;
                if (dealerId is null) return Forbid();
                // Bayinin işletmeleri üzerinden filtrele.
                var bizIds = db.Businesses.Where(b => b.DealerId == dealerId && !b.IsDeleted).Select(b => b.Id);
                q = q.Where(u => u.BusinessId != null && bizIds.Contains(u.BusinessId.Value));
            }
            else // Customer (en dar): sadece kendi işletmesi
            {
                var businessId = CurrentBusinessId;
                if (businessId is null) return Forbid();
                q = q.Where(u => u.BusinessId == businessId);
            }
        }

        var rows = await q.OrderByDescending(u => u.ReportedUtc).ToListAsync();
        return Ok(rows.Select(Map).ToList());
    }

    private static UsageReportResp Map(UsageReport u) => new(
        u.Id, u.BusinessId, u.CompanyCode, u.ReportedUtc,
        u.BranchCount, u.UserCount, u.KioskCount, u.Revenue, u.Period);
}
