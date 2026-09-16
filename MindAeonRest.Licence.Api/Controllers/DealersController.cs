using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Bayi (Dealer) yönetimi — yalnızca ADMIN. CRUD + bayi portal kullanıcısı oluşturma + marj kazanç özeti.
/// Bayi kendi verisini PORTAL üzerinden görür; burada bayi oluşturup/düzenleyemez (yetki Admin'de).
/// </summary>
[ApiController]
[Route("api/v1/dealers")]
[Authorize(Roles = "Admin")]
public class DealersController(LicenceDbContext db, UserManager<ApplicationUser> users) : ControllerBase
{
    /// <summary>ADMIN: tüm bayiler (işletme/lisans sayıları ile).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DealerListItem>>> List([FromQuery] bool includeInactive = true)
    {
        var q = db.Dealers.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(d => d.IsActive);

        var list = await q
            .OrderBy(d => d.Name)
            .Select(d => new DealerListItem(
                d.Id, d.Code, d.Name, d.ContactName, d.Email, d.Phone,
                d.MarginPercent, d.BillingMode, d.IsActive,
                d.Businesses.Count(b => !b.IsDeleted),
                d.Licenses.Count(l => !l.IsDeleted),
                d.CreatedUtc))
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>ADMIN: tek bayi detayı.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DealerDetail>> Get(int id)
    {
        var d = await db.Dealers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new DealerDetail(
                x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone,
                x.MarginPercent, x.BillingMode, x.IsActive, x.Notes,
                x.Businesses.Count(b => !b.IsDeleted),
                x.Licenses.Count(l => !l.IsDeleted),
                x.CreatedUtc, x.UpdatedUtc))
            .FirstOrDefaultAsync();

        if (d is null) return NotFound(new { message = "Bayi bulunamadı." });
        return Ok(d);
    }

    /// <summary>ADMIN: yeni bayi oluştur.</summary>
    [HttpPost]
    public async Task<ActionResult<DealerDetail>> Create([FromBody] DealerCreateReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Bayi adı zorunludur." });
        if (req.MarginPercent < 0 || req.MarginPercent > 100)
            return BadRequest(new { message = "Marj yüzdesi 0-100 aralığında olmalıdır." });

        // Bayi kodu: verilmişse normalize+benzersizlik; verilmemişse otomatik üret. Bu kod bayiye verilir; lisans atfı bununla yapılır.
        string code = (req.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length > 0)
        {
            if (await db.Dealers.AnyAsync(d => d.Code == code && !d.IsDeleted))
                return BadRequest(new { message = $"'{code}' bayi kodu zaten kullanımda." });
        }
        else
        {
            do { code = "BY" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(); }
            while (await db.Dealers.AnyAsync(d => d.Code == code));
        }

        var dealer = new Dealer
        {
            Code = code,
            Name = req.Name.Trim(),
            ContactName = req.ContactName,
            Email = req.Email,
            Phone = req.Phone,
            MarginPercent = req.MarginPercent,
            BillingMode = req.BillingMode,
            IsActive = true,
            Notes = req.Notes
        };
        db.Dealers.Add(dealer);

        // Yarış (race): yukarıdaki benzersizlik kontrolü ile insert arasında aynı kod eklenebilir.
        // Tek güvence DB'deki unique index'tir; ihlalde 500 yerine temiz mesaj döndür, oto-üretimde yeniden dene.
        bool userProvidedCode = !string.IsNullOrWhiteSpace(req.Code);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (IsCodeUniqueViolation(ex))
            {
                if (userProvidedCode)
                    return BadRequest(new { message = $"'{dealer.Code}' bayi kodu zaten kullanımda." });
                if (attempt >= 4)
                    return Conflict(new { message = "Benzersiz bayi kodu üretilemedi; lütfen tekrar deneyin." });
                // Oto-üretim: çakışmada yeni kod ile yeniden dene.
                dealer.Code = "BY" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            }
        }

        return CreatedAtAction(nameof(Get), new { id = dealer.Id }, new DealerDetail(
            dealer.Id, dealer.Code, dealer.Name, dealer.ContactName, dealer.Email, dealer.Phone,
            dealer.MarginPercent, dealer.BillingMode, dealer.IsActive, dealer.Notes,
            0, 0, dealer.CreatedUtc, dealer.UpdatedUtc));
    }

    /// <summary>ADMIN: bayi güncelle (marj/tahsilat modeli/aktiflik dâhil).</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<DealerDetail>> Update(int id, [FromBody] DealerUpdateReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "Bayi adı zorunludur." });
        if (req.MarginPercent < 0 || req.MarginPercent > 100)
            return BadRequest(new { message = "Marj yüzdesi 0-100 aralığında olmalıdır." });

        var dealer = await db.Dealers.FirstOrDefaultAsync(x => x.Id == id);
        if (dealer is null) return NotFound(new { message = "Bayi bulunamadı." });

        dealer.Name = req.Name.Trim();
        dealer.ContactName = req.ContactName;
        dealer.Email = req.Email;
        dealer.Phone = req.Phone;
        dealer.MarginPercent = req.MarginPercent;
        dealer.BillingMode = req.BillingMode;
        dealer.IsActive = req.IsActive;
        dealer.Notes = req.Notes;
        dealer.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new DealerDetail(
            dealer.Id, dealer.Code, dealer.Name, dealer.ContactName, dealer.Email, dealer.Phone,
            dealer.MarginPercent, dealer.BillingMode, dealer.IsActive, dealer.Notes,
            await db.Businesses.CountAsync(b => b.DealerId == dealer.Id),
            await db.Licenses.CountAsync(l => l.DealerId == dealer.Id),
            dealer.CreatedUtc, dealer.UpdatedUtc));
    }

    /// <summary>ADMIN: bayiyi pasife al (soft-delete). Finansal kayıtlar korunur.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var dealer = await db.Dealers.FirstOrDefaultAsync(x => x.Id == id);
        if (dealer is null) return NotFound(new { message = "Bayi bulunamadı." });

        dealer.IsActive = false;
        dealer.IsDeleted = true;
        dealer.DeletedUtc = DateTime.UtcNow;
        dealer.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>ADMIN: bayiye portal giriş hesabı oluştur (Kind=Dealer + role Dealer + DealerId).</summary>
    [HttpPost("{id:int}/user")]
    public async Task<ActionResult<DealerUserResp>> CreateUser(int id, [FromBody] DealerUserCreateReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "E-posta ve parola zorunludur." });

        var dealer = await db.Dealers.FirstOrDefaultAsync(x => x.Id == id);
        if (dealer is null) return NotFound(new { message = "Bayi bulunamadı." });

        if (await users.FindByEmailAsync(req.Email) is not null)
            return Conflict(new { message = "Bu e-posta ile bir kullanıcı zaten var." });

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName ?? dealer.ContactName ?? dealer.Name,
            Kind = UserKind.Dealer,
            DealerId = dealer.Id,
            IsActive = true,
            EmailConfirmed = true
        };

        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
            return BadRequest(new { message = "Kullanıcı oluşturulamadı.", errors = created.Errors.Select(e => e.Description) });

        var roled = await users.AddToRoleAsync(user, "Dealer");
        if (!roled.Succeeded)
        {
            // Rol atanamadıysa yarım kalmış hesabı geri al.
            await users.DeleteAsync(user);
            return BadRequest(new { message = "Rol atanamadı.", errors = roled.Errors.Select(e => e.Description) });
        }

        return Ok(new DealerUserResp(user.Id, user.Email!, user.FullName, dealer.Id, "Dealer"));
    }

    /// <summary>ADMIN: bayinin marj/kazanç özeti — tahsil edilen (Paid) ödemelerden.</summary>
    [HttpGet("{id:int}/earnings")]
    public async Task<ActionResult<DealerEarningsResp>> Earnings(int id)
    {
        var dealer = await db.Dealers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (dealer is null) return NotFound(new { message = "Bayi bulunamadı." });

        var payments = db.Payments.AsNoTracking().Where(p => p.DealerId == id);

        var paid = await payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Gross = g.Sum(p => p.Amount),
                Margin = g.Sum(p => p.MarginAmount),
                Net = g.Sum(p => p.NetAmount)
            })
            .FirstOrDefaultAsync();

        var pending = await payments
            .Where(p => p.Status == PaymentStatus.Pending)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Gross = g.Sum(p => p.Amount) })
            .FirstOrDefaultAsync();

        // Para birimi: bayinin tahsil edilen ödemelerinde en çok kullanılan; yoksa TRY.
        var currency = await payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .GroupBy(p => p.CurrencyCode)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync() ?? "TRY";

        return Ok(new DealerEarningsResp(
            dealer.Id,
            dealer.Name,
            currency,
            paid?.Count ?? 0,
            paid?.Gross ?? 0m,
            paid?.Margin ?? 0m,
            paid?.Net ?? 0m,
            pending?.Count ?? 0,
            pending?.Gross ?? 0m));
    }

    /// <summary>
    /// SQL Server UNIQUE ihlali (2627=PK/UNIQUE constraint, 2601=unique index) ve ihlalin Code kolonuna
    /// ait olup olmadığını tespit eder. Yalnız Code çakışmasını yakalar; başka bir unique ihlali (örn. Name)
    /// yeniden fırlatılır (yanlış mesaj/yanlış retry'ı önler). Code üzerinde DB unique index yoksa bu kod
    /// devreye girmez ve önceki açık benzersizlik kontrolü tek koruma kalır.
    /// </summary>
    private static bool IsCodeUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql
        && sql.Number is 2627 or 2601
        && sql.Message.Contains("Code", StringComparison.OrdinalIgnoreCase);
}
