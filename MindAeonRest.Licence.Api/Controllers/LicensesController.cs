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

        var (resp, error) = await IssueCoreAsync(biz, biz.HardwareId!, new IssueSpec(
            req.Package, req.Duration, req.ExpiresUtc, req.Notes,
            req.AddOnFeatures, req.ExtraUsers, req.ExtraKiosks, req.ExtraBranches), supersede: []);
        return error ?? Ok(resp);
    }

    /// <summary>
    /// ADMIN: MERKEZÎ web tenant'ına lisans ata (Portal "Lisans Ata"). Business CompanyCode ile bulunur, yoksa
    /// oluşturulur. Lisans sabit <see cref="CentralLicense.HardwareId"/> ("WEB-CENTRAL") için imzalanır —
    /// işletmenin masaüstü donanım bağı (Business.HardwareId) BOZULMAZ; yalnız boşsa WEB-CENTRAL yazılır.
    /// TEKRARLANABİLİR: (işletme, WEB-CENTRAL) için mevcut Active/Trial lisans yeni lisansla AYNI kayıtta devre dışı
    /// bırakılır (yüksek pakete geçişte Upgraded, aynı/düşük pakete geçişte Revoked + geçmiş notu) → yükseltme de
    /// düşürme de aynı uçla yapılır. Dönen token'ı Portal POS'a kurar (POST /api/provision/tenant/{code}/license).
    /// </summary>
    [HttpPost("/api/v1/admin/tenant-licenses")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IssueLicenseResp>> AssignTenantLicense([FromBody] AssignTenantLicenseReq req)
    {
        var code = (req.CompanyCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return BadRequest(new { message = "Şirket kodu (companyCode) zorunlu." });
        if (code.Length > 50) return BadRequest(new { message = "Şirket kodu en fazla 50 karakter olabilir." });

        // Admin ucu; yine de global arama için tenant filtresi yok sayılır (silinmemişler, büyük/küçük harf duyarsız).
        var biz = await db.Businesses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.CompanyCode.ToUpper() == code);
        if (biz is null)
        {
            var title = string.IsNullOrWhiteSpace(req.CompanyName) ? code : req.CompanyName.Trim();
            biz = new Business
            {
                CompanyCode = code,
                Title = title.Length > 200 ? title[..200] : title,
                HardwareId = CentralLicense.HardwareId
            };
            db.Businesses.Add(biz);
        }
        else if (string.IsNullOrWhiteSpace(biz.HardwareId))
        {
            // Masaüstü bağı yoksa merkezî kimlik yazılır; VARSA (masaüstü) dokunulmaz — lisans yine WEB-CENTRAL için üretilir.
            biz.HardwareId = CentralLicense.HardwareId;
            biz.UpdatedUtc = DateTime.UtcNow;
        }

        // Aynı (işletme, WEB-CENTRAL) için hâlâ geçerli lisans(lar) → yeni lisansla değiştirilecek (supersede).
        var previous = biz.Id == 0
            ? new List<License>()
            : await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == biz.Id && l.HardwareId == CentralLicense.HardwareId
                            && (l.Status == LicenseStatus.Active || l.Status == LicenseStatus.Trial))
                .ToListAsync();

        var (resp, error) = await IssueCoreAsync(biz, CentralLicense.HardwareId, new IssueSpec(
            req.Package, req.Duration, req.ExpiresUtc, req.Notes,
            req.AddOnFeatures, req.ExtraUsers, req.ExtraKiosks, req.ExtraBranches), previous);
        return error ?? Ok(resp);
    }

    /// <summary>İmzalama + kalıcılaştırma girdisi (Issue ve AssignTenantLicense ortak).</summary>
    private sealed record IssueSpec(int Package, DurationKind Duration, DateTime? ExpiresUtc, string? Notes,
        long AddOnFeatures, int ExtraUsers, int ExtraKiosks, int ExtraBranches);

    /// <summary>
    /// ORTAK lisans üretim çekirdeği: doğrulama (paket, süre, terminal limiti) → paket/limit hesabı (PackageMatrix)
    /// → imza → License + History kaydı. Donanım kimliği AÇIKÇA verilir (masaüstü: Business.HardwareId; merkezî:
    /// WEB-CENTRAL). <paramref name="supersede"/> lisansları YALNIZ doğrulama geçtikten sonra devre dışı bırakılır ve
    /// yeni lisansla AYNI SaveChanges'te yazılır (yarım durum kalmaz). Hata → (null, ActionResult).
    /// </summary>
    private async Task<(IssueLicenseResp? Resp, ActionResult? Error)> IssueCoreAsync(
        Business biz, string hardwareId, IssueSpec req, IReadOnlyList<License> supersede)
    {
        if (!Enum.IsDefined(typeof(LicensePackage), req.Package))
            return (null, BadRequest(new { message = "Geçersiz paket." }));

        // Süreli (Aylık/Yıllık) lisansta bitiş tarihi zorunlu ve gelecekte olmalı (yanlışlıkla süresiz üretmeyi engelle).
        if (req.Duration != DurationKind.Perpetual && (req.ExpiresUtc is not { } exp || exp <= DateTime.UtcNow))
            return (null, BadRequest(new { message = "Süreli (Aylık/Yıllık) lisans için gelecekte bir bitiş tarihi (ExpiresUtc) zorunludur." }));

        var pkg = (LicensePackage)req.Package;
        var code = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        // Nihai özellik = paket çekirdeği | satılan add-on modüller; limitler = paket tabanı + ekstra.
        var addOns = (Feature)req.AddOnFeatures;
        var features = PackageMatrix.Features(pkg) | addOns;
        bool multiBranch = features.HasFlag(Feature.MultiBranch);
        int maxUsers = PackageMatrix.UsersWith(pkg, req.ExtraUsers);
        int maxBranches = PackageMatrix.BranchesWith(pkg, req.ExtraBranches, multiBranch);

        // Terminal (KIOSK) limiti: işletmenin DİĞER donanımlardaki aktif lisansları toplam terminali aşmasın.
        // (Yeni oluşturulan işletmenin — Id=0 — başka lisansı olamaz.)
        int maxKiosks = PackageMatrix.KiosksWith(pkg, req.ExtraKiosks);
        if (maxKiosks != int.MaxValue && biz.Id != 0)
        {
            int activeOther = await db.Licenses.IgnoreQueryFilters()
                .Where(l => !l.IsDeleted && l.BusinessId == biz.Id && l.Status == LicenseStatus.Active && l.HardwareId != hardwareId)
                .Select(l => l.HardwareId).Distinct().CountAsync();
            if (activeOther >= maxKiosks)
                return (null, BadRequest(new { message = $"Bu paket en fazla {maxKiosks} terminal (KIOSK) içindir; işletmede {activeOther} aktif terminal var. Paketi yükseltin veya bir terminali iptal edin." }));
        }

        string? dealerCode = biz.DealerId is int did ? (await db.Dealers.FindAsync(did))?.Code : null;
        var payload = new LicensePayload
        {
            LicenseId = code,
            CompanyCode = biz.CompanyCode,
            HardwareId = hardwareId,
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
        var byUser = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        var lic = new License
        {
            Business = biz,   // yeni işletmede (Id=0) FK aynı SaveChanges'te çözülür
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
            HardwareId = hardwareId,
            IssuedUtc = payload.IssuedUtc,
            ExpiresUtc = payload.ExpiresUtc,
            Status = LicenseStatus.Active,
            LicenseToken = token,
            Notes = req.Notes,
            IssuedByUserId = byUser
        };
        lic.History.Add(new LicenseHistory { Action = LicenseAction.Issued, ToPackage = req.Package, ByUserId = byUser });

        // Yerine geçilen lisanslar: yüksek pakete geçiş → Upgraded; aynı/düşük paket (yeniden atama/düşürme) → Revoked.
        foreach (var old in supersede)
        {
            bool up = req.Package > old.Package;
            old.Status = up ? LicenseStatus.Upgraded : LicenseStatus.Revoked;
            old.UpdatedUtc = DateTime.UtcNow;
            var note = $"Merkezî lisans yeniden atandı: {PackageMatrix.Name((LicensePackage)old.Package)} → {PackageMatrix.Name(pkg)} (yeni lisans {code}).";
            if (!string.IsNullOrWhiteSpace(req.Notes)) note += " " + req.Notes.Trim();
            old.History.Add(new LicenseHistory
            {
                Action = up ? LicenseAction.Upgraded : LicenseAction.Revoked,
                FromPackage = old.Package,
                ToPackage = req.Package,
                ByUserId = byUser,
                Note = note.Length > 500 ? note[..500] : note
            });
        }

        db.Licenses.Add(lic);
        await db.SaveChangesAsync();

        return (new IssueLicenseResp(lic.Id, code, token, req.Package, lic.IssuedUtc, lic.ExpiresUtc), null);
    }

    /// <summary>Masaüstü: talep anahtarıyla online aktivasyon. Onaylı/aktif lisans varsa imzalı token döner; yoksa talebi kuyruğa alır.</summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivateResp>> Activate([FromBody] ActivateReq req)
    {
        var decoded = signer.DecodeRequest(req.RequestToken);
        if (decoded is null) return BadRequest(new ActivateResp("none", null, "Talep anahtarı çözülemedi."));

        // GÜVENLİK: merkezî (WEB-*) donanım kimliği TAHMİN EDİLEBİLİR sabittir; bu anonim uç aktif lisansı yalnız
        // HardwareId ile aradığından, "WEB-CENTRAL" talebi başka bir işletmenin merkezî token'ını sızdırırdı.
        // Merkezî lisanslar yalnız Admin "Lisans Ata" akışıyla (provisioning anahtarlı POS kurulumu) dağıtılır.
        if (CentralLicense.IsCentral(decoded.HardwareId))
            return BadRequest(new ActivateResp("none", null, "Bu donanım kimliği ile online aktivasyon yapılamaz."));

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
