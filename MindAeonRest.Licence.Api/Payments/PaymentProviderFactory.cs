using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Payments;

/// <summary>
/// Aktif yapılandırmaya göre uygun <see cref="IPaymentProvider"/> örneğini kurar.
///
/// Şu an yalnızca Banka Havalesi/EFT (BankTransfer) etkin tahsilat yöntemidir. Online PSP
/// yöntemleri (İyzico/PayTR/Garanti VPOS) etkin değildir ve fail-closed bir sağlayıcıyla
/// (<see cref="UnavailableOnlineProvider"/>) temsil edilir: yanlışlıkla çağrılsalar bile
/// hiçbir sahte başarı/yönlendirme üretmezler.
///
/// BankTransfer config çözümü DealerId kapsamlıdır: önce bayiye özel (DealerId == dealerId)
/// aktif config aranır, yoksa global (DealerId == null) config'e düşülür. Şifreli config
/// çözülerek havale hesabı bilgisi (ProviderCredentials.IbanInfo) verilir.
///
/// NOT: Provider'lar DI'da kayıtlı DEĞİL; her istek için config'e göre new'lenir
/// (Program.cs'e dokunulmaması kuralı gereği). Factory scoped kullanılabilir.
/// </summary>
public sealed class PaymentProviderFactory
{
    private readonly LicenceDbContext _db;
    private readonly PaymentConfigProtector _protector;

    public PaymentProviderFactory(LicenceDbContext db, PaymentConfigProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    /// <summary>
    /// Belirtilen sağlayıcı türü için provider döndürür.
    /// BankTransfer için (varsa) bayiye özel aktif config (havale hesabı bilgisi) çözülür;
    /// config zorunlu değildir (IBAN bilgisi opsiyonel). Online yöntemler etkin olmadığından
    /// fail-closed <see cref="UnavailableOnlineProvider"/> döner.
    /// </summary>
    public async Task<IPaymentProvider> CreateAsync(PaymentProviderKind kind, int? dealerId, CancellationToken ct = default)
    {
        if (kind != PaymentProviderKind.BankTransfer)
            return new UnavailableOnlineProvider(kind);

        var creds = await ResolveCredentialsAsync(kind, dealerId, ct);
        return new BankTransferProvider(creds);
    }

    /// <summary>Bu sağlayıcı etkin/kullanılabilir mi? Yalnızca BankTransfer etkindir.</summary>
    public bool IsAvailable(PaymentProviderKind kind) => kind == PaymentProviderKind.BankTransfer;

    private async Task<ProviderCredentials?> ResolveCredentialsAsync(PaymentProviderKind kind, int? dealerId, CancellationToken ct)
    {
        var cfg = await FindActiveConfigAsync(kind, dealerId, ct);
        if (cfg is null) return null;
        return _protector.UnprotectObject<ProviderCredentials>(cfg.ConfigJsonEncrypted);
    }

    private async Task<PaymentProviderConfig?> FindActiveConfigAsync(PaymentProviderKind kind, int? dealerId, CancellationToken ct)
    {
        // PaymentProviderConfig'in tenant query filter'ı YOK (yalnız soft-delete) — bilinçli:
        // ödeme başlatırken sunucu, ilgili bayinin/global config'ini güvenle çözebilmeli.
        var q = _db.PaymentProviderConfigs.AsNoTracking()
            .Where(c => c.Provider == kind && c.IsActive && !c.IsDeleted);

        // Önce bayiye özel.
        if (dealerId is { } did)
        {
            var dealerCfg = await q.FirstOrDefaultAsync(c => c.DealerId == did, ct);
            if (dealerCfg is not null) return dealerCfg;
        }

        // Sonra global (DealerId == null).
        return await q.FirstOrDefaultAsync(c => c.DealerId == null, ct);
    }
}
