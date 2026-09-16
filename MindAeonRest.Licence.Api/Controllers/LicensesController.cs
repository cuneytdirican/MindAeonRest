using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Api.Services;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Controllers;

[ApiController]
[Route("api/v1/licenses")]
public class LicensesController(LicenceDbContext db, ILicenseSigner signer) : ControllerBase
{
    /// <summary>ADMIN: bir işletme için lisans üret (imzala + DB'ye kaydet + geçmiş). Bayi BU UCU ÇAĞIRAMAZ.</summary>
    [HttpPost("issue")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IssueLicenseResp>> Issue([FromBody] IssueLicenseReq req)
    {
        var biz = await db.Businesses.FindAsync(req.BusinessId);
        if (biz is null) return NotFound(new { message = "İşletme bulunamadı." });
        if (string.IsNullOrWhiteSpace(biz.HardwareId)) return BadRequest(new { message = "İşletmenin donanım kimliği yok (önce aktivasyon talebi gelmeli)." });

        // Tek (BusinessId+HardwareId) için aynı anda yalnız bir Active lisans olur (mükerrer üretimi engelle).
        // Aynı donanımda hâlâ aktif bir lisans varsa net hata dön; yenilemek için önce iptal/yükseltme gerekir.
        bool dupActive = await db.Licenses.IgnoreQueryFilters()
            .AnyAsync(l => !l.IsDeleted && l.BusinessId == biz.Id && l.HardwareId == biz.HardwareId && l.Status == LicenseStatus.Active);
        if (dupActive)
            return Conflict(new { message = "Bu işletme + donanım için zaten aktif bir lisans var. Önce mevcut lisansı iptal/yükseltin." });

        // Süreli (Aylık/Yıllık) lisansta bitiş tarihi zorunlu ve gelecekte olmalı (yanlışlıkla süresiz üretmeyi engelle).
        if (req.Duration != DurationKind.Perpetual && (req.ExpiresUtc is not { } exp || exp <= DateTime.UtcNow))
            return BadRequest(new { message = "Süreli (Aylık/Yıllık) lisans için gelecekte bir bitiş tarihi (ExpiresUtc) zorunludur." });

        var pkg = (LicensePackage)req.Package;
        var code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        // Nihai özellik = paket çekirdeği | satılan add-on modüller; limitler = paket tabanı + ekstra.
        var addOns = (Feature)req.AddOnFeatures;
        var features = PackageMatrix.Features(pkg) | addOns;
        bool multiBranch = features.HasFlag(Feature.MultiBranch);
        int maxUsers = PackageMatrix.UsersWith(pkg, req.ExtraUsers);
        int maxBranches = PackageMatrix.BranchesWith(pkg, req.ExtraBranches, multiBranch);

        // Terminal (KIOSK) limiti: işletmenin DİĞER donanımlardaki aktif lisansları toplam terminali aşmasın.
        int maxKiosks = PackageMatrix.KiosksWith(pkg, req.ExtraKiosks);
        if (maxKiosks != int.MaxValue)
        {
            int activeOther = await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == biz.Id && l.Status == LicenseStatus.Active && l.HardwareId != biz.HardwareId)
                .Select(l => l.HardwareId).Distinct().CountAsync();
            if (activeOther >= maxKiosks)
                return BadRequest(new { message = $"Bu paket en fazla {maxKiosks} terminal (KIOSK) içindir; işletmede {activeOther} aktif terminal var. Paketi yükseltin veya bir terminali iptal edin." });
        }

        string? dealerCode = biz.DealerId is int did ? (await db.Dealers.FindAsync(did))?.Code : null;
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
            DealerId = biz.DealerId,
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
            HardwareId = biz.HardwareId!,
            IssuedUtc = payload.IssuedUtc,
            ExpiresUtc = payload.ExpiresUtc,
            Status = LicenseStatus.Active,
            LicenseToken = token,
            Notes = req.Notes,
            IssuedByUserId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
        };
        lic.History.Add(new LicenseHistory { Action = LicenseAction.Issued, ToPackage = req.Package, ByUserId = lic.IssuedByUserId });
        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        return Ok(new IssueLicenseResp(lic.Id, code, token, req.Package, lic.IssuedUtc, lic.ExpiresUtc));
    }

    /// <summary>Masaüstü: talep anahtarıyla online aktivasyon. Onaylı/aktif lisans varsa imzalı token döner; yoksa talebi kuyruğa alır.</summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivateResp>> Activate([FromBody] ActivateReq req)
    {
        var decoded = signer.DecodeRequest(req.RequestToken);
        if (decoded is null) return BadRequest(new ActivateResp("none", null, "Talep anahtarı çözülemedi."));

        // İşletmeyi (yoksa oluştur) + donanımı bağla. ANONİM cihaz ucu → tenant filtresini ATLA.
        var biz = await db.Businesses.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyCode == decoded.CompanyCode && !x.IsDeleted);
        if (biz is null)
        {
            biz = new Business { CompanyCode = decoded.CompanyCode, Title = decoded.CompanyCode, HardwareId = decoded.HardwareId };
            db.Businesses.Add(biz);
        }
        else if (string.IsNullOrWhiteSpace(biz.HardwareId)) biz.HardwareId = decoded.HardwareId;

        // GÜVENLİK: Bayi atfı bu ANONİM/imzasız uçtan YAPILMAZ. Talep token'ındaki DealerCode imzasızdır;
        // ona güvenip işletmeyi bir bayiye bağlamak, üçüncü bir tarafın boş-bayili işletmeleri kendi
        // bayisine atfetmesine (komisyon/erişim kaçırma) yol açardı. DealerId yalnızca kimlik-doğrulanmış
        // Issue/Approve uçlarında, işletme/talep üzerinden sunucu tarafında çözülerek set edilir.

        var active = await db.Licenses.IgnoreQueryFilters()
            .Where(l => !l.IsDeleted && l.HardwareId == decoded.HardwareId && l.Status == LicenseStatus.Active)
            .OrderByDescending(l => l.IssuedUtc).FirstOrDefaultAsync();
        if (active is not null && (active.ExpiresUtc == null || active.ExpiresUtc > DateTime.UtcNow))
        {
            await db.SaveChangesAsync();
            return Ok(new ActivateResp("issued", active.LicenseToken, null));
        }

        // Onaylı lisans yok → talebi kuyruğa al (tekrarı önle)
        bool exists = await db.LicenseRequests.IgnoreQueryFilters().AnyAsync(r => !r.IsDeleted && r.HardwareId == decoded.HardwareId && r.Status == RequestStatus.Pending);
        if (!exists)
            db.LicenseRequests.Add(new LicenseRequest
            {
                BusinessId = biz.Id == 0 ? null : biz.Id,
                CompanyCode = decoded.CompanyCode,
                HardwareId = decoded.HardwareId,
                RequestedPackage = (int)decoded.RequestedPackage,
                RequestToken = req.RequestToken,
                Status = RequestStatus.Pending
            });
        await db.SaveChangesAsync();
        return Ok(new ActivateResp("pending", null, "Lisans talebiniz alındı; onay sonrası otomatik etkinleşir."));
    }

    /// <summary>Masaüstü heartbeat: iptal/expiry tespiti. İptal en geç bir sonraki doğrulamada etki eder.</summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<ActionResult<ValidateResp>> Validate([FromBody] ValidateReq req)
    {
        var lic = await db.Licenses.IgnoreQueryFilters().FirstOrDefaultAsync(l => !l.IsDeleted && l.LicenseCode == req.LicenseCode);
        // NUMARALANDIRMA ÖNLE: "lisans yok" ile "yanlış makine" durumları AYNI generik yanıta indirgenir;
        // aksi halde yalnızca kod tahmin ederek geçerli lisans kodları çıkarsanabilirdi (oracle). Tek "Invalid".
        if (lic is null || !string.Equals(lic.HardwareId, req.HardwareId, StringComparison.OrdinalIgnoreCase))
            return Ok(new ValidateResp("Invalid", null));
        if (lic.Status == LicenseStatus.Revoked) return Ok(new ValidateResp("Revoked", null));
        if (lic.ExpiresUtc is { } e && e < DateTime.UtcNow) return Ok(new ValidateResp("Expired", e));
        return Ok(new ValidateResp("Active", lic.ExpiresUtc));
    }

    // ===== MÜŞTERİ uçları =====
    // Müşteri portalı kendi lisanslarını GÖRMEK/İNDİRMEK için Admin uçlarını çağıramaz (403).
    // Buradaki uçlar JWT'den (Kind=Customer + business claim) işletmeyi belirler ve SADECE
    // o işletmenin lisanslarını döndürür. License query-filter zaten BusinessId'ye kısıtlar;
    // ayrıca açıkça BusinessId ile filtrelenir (defense-in-depth — başka işletme verisi sızmaz).

    private int? CurrentBusinessId =>
        int.TryParse(User.FindFirst(JwtTokenService.ClaimBusiness)?.Value, out var b) ? b : null;

    /// <summary>MÜŞTERİ: oturum açan işletmenin lisansları (özet liste). Sadece kendi işletmesi.</summary>
    [HttpGet("/api/v1/my/licenses")]
    [Authorize(Roles = "Customer,Admin")]
    public async Task<ActionResult<IReadOnlyList<LicenseListItem>>> MyLicenses()
    {
        if (CurrentBusinessId is not { } bizId)
            return Forbid();

        var items = await db.Licenses
            .AsNoTracking()
            .Include(l => l.Business)
            .Include(l => l.Dealer)
            .Where(l => l.BusinessId == bizId)
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

    /// <summary>MÜŞTERİ: tek lisans + token (.lic indirme için). Sadece kendi işletmesinin lisansı.</summary>
    [HttpGet("/api/v1/my/licenses/{id:int}")]
    [Authorize(Roles = "Customer,Admin")]
    public async Task<ActionResult<MyLicenseTokenResp>> MyLicenseToken(int id)
    {
        if (CurrentBusinessId is not { } bizId)
            return Forbid();

        var lic = await db.Licenses
            .AsNoTracking()
            .Where(l => l.Id == id && l.BusinessId == bizId)
            .Select(l => new MyLicenseTokenResp(l.Id, l.LicenseCode, l.LicenseToken))
            .FirstOrDefaultAsync();
        if (lic is null) return NotFound(new { message = "Lisans bulunamadı." });
        return Ok(lic);
    }
}
