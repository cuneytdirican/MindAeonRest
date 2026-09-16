using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Api.Payments;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Ödeme sağlayıcı yapılandırması (Admin yönetim ucu). Şu an yalnızca Banka Havalesi/EFT
/// yapılandırılabilir: havale yapılacak IBAN/hesap bilgisi burada girilir ve DB'de ŞİFRELİ
/// (Data Protection) saklanır — daha önce yalnızca DB'ye elle yazılarak görünürdü.
///
/// SÖZLEŞME (Portal ajanı buna bağlanır):
///   GET  /api/v1/payment-config/banktransfer  → mevcut config (iban, accountName, bankName, description, active)
///   POST /api/v1/payment-config/banktransfer  → aynı gövdeyi kaydeder (şifreli PaymentProviderConfig'e)
///
/// PROVIDER UYUMU (BİREBİR): <see cref="BankTransferProvider"/> ödeme başlatırken
/// <see cref="ProviderCredentials.IbanInfo"/> alanını okuyup müşteriye gösterir. Bu nedenle POST,
/// yapısal alanlardan okunaklı bir <c>IbanInfo</c> metni komponoze eder VE ham alanları (Iban/
/// BankAccountName/BankName/BankTransferDescription) round-trip için saklar. Şifreleme/serileştirme
/// deseni PaymentsController/PaymentProviderFactory ile aynıdır (PaymentConfigProtector + Web JSON).
///
/// Kapsam: global config (DealerId == null) — factory'nin bayi-yoksa-global fallback'iyle uyumlu.
/// Yetki: yalnız Admin. Enumerasyon yok (tek, kimliksiz kaynak).
///
/// DI NOTU: PaymentConfigProtector burada elle kurulur (Program.cs'e dokunulmaması deseni);
/// IDataProtectionProvider çerçeveden gelir.
/// </summary>
[ApiController]
[Route("api/v1/payment-config")]
[Authorize(Roles = "Admin")]
public class PaymentConfigController : ControllerBase
{
    private readonly LicenceDbContext _db;
    private readonly PaymentConfigProtector _protector;

    public PaymentConfigController(LicenceDbContext db, IDataProtectionProvider dp)
    {
        _db = db;
        _protector = new PaymentConfigProtector(dp);
    }

    private string? CurrentUserId =>
        User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

    /// <summary>ADMIN: mevcut Banka Havalesi yapılandırmasını (şifresi çözülmüş) döndürür.</summary>
    [HttpGet("banktransfer")]
    public async Task<ActionResult<BankTransferConfigDto>> GetBankTransfer(CancellationToken ct)
    {
        var cfg = await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == PaymentProviderKind.BankTransfer
                                   && c.DealerId == null && !c.IsDeleted, ct);

        if (cfg is null)
            return Ok(new BankTransferConfigDto(null, null, null, null, false));

        var creds = _protector.UnprotectObject<ProviderCredentials>(cfg.ConfigJsonEncrypted);
        // Eski (elle yazılmış) config'lerde yalnız IbanInfo dolu olabilir → iban için graceful fallback.
        return Ok(new BankTransferConfigDto(
            creds?.Iban ?? creds?.IbanInfo,
            creds?.BankAccountName,
            creds?.BankName,
            creds?.BankTransferDescription,
            cfg.IsActive));
    }

    /// <summary>
    /// ADMIN: Banka Havalesi yapılandırmasını kaydeder (global config'e upsert).
    /// IBAN zorunludur. Değerler ŞİFRELİ saklanır; provider'ın okuduğu IbanInfo alanı komponoze edilir.
    /// </summary>
    [HttpPost("banktransfer")]
    public async Task<ActionResult<BankTransferConfigDto>> SaveBankTransfer(
        [FromBody] BankTransferConfigDto req, CancellationToken ct)
    {
        var iban = (req.Iban ?? "").Trim();
        if (string.IsNullOrWhiteSpace(iban))
            return BadRequest(new { message = "IBAN zorunludur." });

        var accountName = string.IsNullOrWhiteSpace(req.AccountName) ? null : req.AccountName.Trim();
        var bankName = string.IsNullOrWhiteSpace(req.BankName) ? null : req.BankName.Trim();
        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();

        // Provider IbanInfo'yu düz metin gösterdiğinden, yapısal alanlardan okunaklı bir hesap metni üret.
        var parts = new List<string>();
        if (bankName is not null) parts.Add(bankName);
        if (accountName is not null) parts.Add(accountName);
        parts.Add(iban);
        if (description is not null) parts.Add(description);
        var ibanInfo = string.Join(" - ", parts);

        var creds = new ProviderCredentials
        {
            Iban = iban,
            BankAccountName = accountName,
            BankName = bankName,
            BankTransferDescription = description,
            IbanInfo = ibanInfo   // BankTransferProvider'ın okuduğu alan (birebir uyum)
        };
        var encrypted = _protector.ProtectObject(creds);

        var cfg = await _db.PaymentProviderConfigs
            .FirstOrDefaultAsync(c => c.Provider == PaymentProviderKind.BankTransfer
                                   && c.DealerId == null && !c.IsDeleted, ct);
        if (cfg is null)
        {
            cfg = new PaymentProviderConfig
            {
                Provider = PaymentProviderKind.BankTransfer,
                DealerId = null,
                IsActive = req.Active,
                ConfigJsonEncrypted = encrypted,
                CreatedByUserId = CurrentUserId
            };
            _db.PaymentProviderConfigs.Add(cfg);
        }
        else
        {
            cfg.IsActive = req.Active;
            cfg.ConfigJsonEncrypted = encrypted;
            cfg.UpdatedUtc = DateTime.UtcNow;
            cfg.UpdatedByUserId = CurrentUserId;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new BankTransferConfigDto(iban, accountName, bankName, description, cfg.IsActive));
    }
}
