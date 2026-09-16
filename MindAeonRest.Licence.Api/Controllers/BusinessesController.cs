using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// İşletme (Business) CRUD + müşteri hesabı + donanım/lisans geçmişi.
/// Yetki: Admin tümü; Bayi SADECE kendi işletmeleri; Müşteri salt-okuma kendi işletmesi.
/// Tenant filtresi DbContext query filter ile zorlanır (okuma); yazmada sahiplik elle doğrulanır.
/// </summary>
[ApiController]
[Route("api/v1/businesses")]
[Authorize]
public class BusinessesController(
    LicenceDbContext db,
    ITenantInfo tenant,
    UserManager<ApplicationUser> users) : ControllerBase
{
    // ---------------------------------------------------------------- LIST
    /// <summary>İşletmeleri listele. Query filter sayesinde Admin tümü, Bayi kendi, Müşteri kendi işletmesini görür.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BusinessListItem>>> List([FromQuery] string? q, [FromQuery] int? dealerId)
    {
        var query = db.Businesses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(b =>
                b.Title.Contains(term) ||
                b.CompanyCode.Contains(term) ||
                (b.City != null && b.City.Contains(term)) ||
                (b.Email != null && b.Email.Contains(term)));
        }

        // Admin isteğe bağlı bayi filtresi uygulayabilir; diğer roller zaten filtreyle kısıtlı.
        if (dealerId is { } d && tenant.IsAdmin)
            query = query.Where(b => b.DealerId == d);

        var list = await query
            .OrderBy(b => b.Title)
            .Select(b => new BusinessListItem(
                b.Id,
                b.DealerId,
                b.Dealer != null ? b.Dealer.Name : null,
                b.CompanyCode,
                b.Title,
                b.City,
                b.Phone,
                b.Email,
                b.HardwareId,
                b.Licenses.Count(l => !l.IsDeleted && l.Status == LicenseStatus.Active),
                b.CreatedUtc))
            .ToListAsync();

        return Ok(list);
    }

    // ---------------------------------------------------------------- DETAIL
    /// <summary>İşletme detayı + lisans/donanım/geçmiş. Yetkisiz/başka kiracının kaydı: 404 (filtreyle gizli).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BusinessDetail>> Get(int id)
    {
        var biz = await db.Businesses.AsNoTracking()
            .Include(b => b.Dealer)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });

        var licenses = await db.Licenses.AsNoTracking()
            .Where(l => l.BusinessId == id)
            .OrderByDescending(l => l.IssuedUtc)
            .Select(l => new BusinessLicenseInfo(
                l.Id,
                l.LicenseCode,
                l.Package,
                l.MaxBranches,
                l.MaxUsers,
                l.HardwareId,
                l.Status,
                l.IssuedUtc,
                l.ExpiresUtc,
                l.Notes,
                l.History
                    .Where(h => !h.IsDeleted)
                    .OrderByDescending(h => h.CreatedUtc)
                    .Select(h => new BusinessLicenseHistoryInfo(
                        h.Id, h.Action, h.FromPackage, h.ToPackage, h.Note, h.CreatedUtc))
                    .ToList()))
            .ToListAsync();

        return Ok(new BusinessDetail(
            biz.Id,
            biz.DealerId,
            biz.Dealer?.Name,
            biz.CompanyCode,
            biz.Title,
            biz.TaxOffice,
            biz.TaxNumber,
            biz.Address,
            biz.City,
            biz.Phone,
            biz.Email,
            biz.HardwareId,
            biz.CreatedUtc,
            biz.UpdatedUtc,
            licenses));
    }

    // ---------------------------------------------------------------- CREATE
    /// <summary>İşletme oluştur. Admin DealerId atayabilir; Bayi otomatik kendi DealerId'sine bağlanır.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Dealer")]
    public async Task<ActionResult<BusinessDetail>> Create([FromBody] CreateBusinessReq req)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyCode)) return BadRequest(new { message = "Şirket kodu zorunlu." });
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Unvan zorunlu." });

        // DealerId belirleme: Bayi kendi DealerId'sine sabitlenir; Admin serbest.
        int? dealerId;
        if (tenant.IsAdmin)
        {
            dealerId = req.DealerId;
        }
        else
        {
            if (tenant.DealerId is null) return Forbid();
            dealerId = tenant.DealerId; // Bayi başka bayiye işletme açamaz
        }

        // CompanyCode benzersizliği (silinmemişler arasında) — filtreyi yok say (global kontrol).
        bool codeExists = await db.Businesses.IgnoreQueryFilters()
            .AnyAsync(b => !b.IsDeleted && b.CompanyCode == req.CompanyCode.Trim());
        if (codeExists) return Conflict(new { message = "Bu şirket kodu zaten kayıtlı." });

        var biz = new Business
        {
            DealerId = dealerId,
            CompanyCode = req.CompanyCode.Trim(),
            Title = req.Title.Trim(),
            TaxOffice = req.TaxOffice,
            TaxNumber = req.TaxNumber,
            Address = req.Address,
            City = req.City,
            Phone = req.Phone,
            Email = req.Email,
            HardwareId = string.IsNullOrWhiteSpace(req.HardwareId) ? null : req.HardwareId.Trim()
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = biz.Id }, await BuildDetail(biz.Id));
    }

    // ---------------------------------------------------------------- UPDATE
    /// <summary>İşletme güncelle. Başka kiracının kaydı filtreyle gizli olduğundan 404 döner (yetki sızıntısı yok).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Dealer")]
    public async Task<ActionResult<BusinessDetail>> Update(int id, [FromBody] UpdateBusinessReq req)
    {
        var biz = await db.Businesses.FirstOrDefaultAsync(b => b.Id == id);
        if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });

        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Unvan zorunlu." });

        // Bayi DealerId'yi değiştiremez (devir Admin işi). Admin değiştirebilir.
        if (tenant.IsAdmin)
            biz.DealerId = req.DealerId;

        biz.Title = req.Title.Trim();
        biz.TaxOffice = req.TaxOffice;
        biz.TaxNumber = req.TaxNumber;
        biz.Address = req.Address;
        biz.City = req.City;
        biz.Phone = req.Phone;
        biz.Email = req.Email;
        biz.HardwareId = string.IsNullOrWhiteSpace(req.HardwareId) ? null : req.HardwareId.Trim();
        biz.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(await BuildDetail(biz.Id));
    }

    // ---------------------------------------------------------------- DELETE (soft)
    /// <summary>İşletmeyi sil (soft-delete). Aktif lisansı varsa engelle (finansal bütünlük).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Dealer")]
    public async Task<IActionResult> Delete(int id)
    {
        var biz = await db.Businesses.FirstOrDefaultAsync(b => b.Id == id);
        if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });

        bool hasActiveLicense = await db.Licenses
            .AnyAsync(l => l.BusinessId == id && l.Status == LicenseStatus.Active);
        if (hasActiveLicense)
            return Conflict(new { message = "Aktif lisansı olan işletme silinemez. Önce lisansı iptal edin." });

        biz.IsDeleted = true;
        biz.DeletedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------------------------------------------------------- CUSTOMER ACCOUNT
    /// <summary>İşletme için müşteri portal hesabı oluştur (Kind=Customer + BusinessId + rol Customer).</summary>
    [HttpPost("{id:int}/customer-account")]
    [Authorize(Roles = "Admin,Dealer")]
    public async Task<ActionResult<CreateBusinessCustomerResp>> CreateCustomerAccount(int id, [FromBody] CreateBusinessCustomerReq req)
    {
        // İşletme erişilebilir mi? (query filter → başka kiracıysa 404)
        var biz = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });

        if (string.IsNullOrWhiteSpace(req.Email)) return BadRequest(new { message = "E-posta zorunlu." });
        if (string.IsNullOrWhiteSpace(req.Password)) return BadRequest(new { message = "Parola zorunlu." });

        if (await users.FindByEmailAsync(req.Email) is not null)
            return Conflict(new { message = "Bu e-posta ile bir kullanıcı zaten var." });

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? biz.Title : req.FullName,
            Kind = UserKind.Customer,
            BusinessId = biz.Id,
            DealerId = biz.DealerId,
            IsActive = true,
            EmailConfirmed = true
        };

        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
            return BadRequest(new { message = "Hesap oluşturulamadı.", errors = created.Errors.Select(e => e.Description) });

        await users.AddToRoleAsync(user, "Customer");

        return Ok(new CreateBusinessCustomerResp(user.Id, user.Email!, user.FullName ?? "", biz.Id));
    }

    // ---------------------------------------------------------------- helper
    private async Task<BusinessDetail> BuildDetail(int id)
    {
        var actionResult = await Get(id);
        return ((actionResult.Result as OkObjectResult)!.Value as BusinessDetail)!;
    }
}
