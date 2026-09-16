using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MindAeonRest.Licence.Api.Payments;

/// <summary>
/// PaymentProviderConfig.ConfigJsonEncrypted alanını ASP.NET Core Data Protection ile
/// şifreler/çözer. Havale hesabı bilgisi gibi yapılandırma değerleri DB'de düz metin tutulmaz.
///
/// IDataProtectionProvider varsayılan olarak DI'da bulunur. Çok-sunuculu/IIS dönüşümlü
/// senaryolarda anahtar halkasının paylaşımlı kalıcı depoya alınması gerekir
/// (aksi halde yeniden başlatmada eski şifreli config'ler çözülemez).
/// </summary>
public sealed class PaymentConfigProtector
{
    // Amaç (purpose) dizesi anahtar türetmede kullanılır; değiştirilirse eski veriler çözülemez.
    private const string Purpose = "MindAeonRest.PaymentProviderConfig.v1";

    private readonly IDataProtector _protector;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public PaymentConfigProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Düz JSON config'i şifreler (DB'ye yazılacak değer).</summary>
    public string Protect(string plainJson) =>
        _protector.Protect(plainJson ?? "");

    /// <summary>Şifreli config'i düz JSON'a çözer.</summary>
    public string Unprotect(string encrypted) =>
        _protector.Unprotect(encrypted ?? "");

    /// <summary>Bir nesneyi JSON'a serileştirip şifreler.</summary>
    public string ProtectObject<T>(T value) =>
        Protect(JsonSerializer.Serialize(value, JsonOpts));

    /// <summary>Şifreli config'i çözüp tipli nesneye dönüştürür. Çözülemezse/boşsa null.</summary>
    public T? UnprotectObject<T>(string encrypted) where T : class
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try
        {
            var json = Unprotect(encrypted);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            // Bozuk/uyumsuz şifreli veri — sessizce null (sağlayıcı yapılandırılmamış say).
            return null;
        }
    }
}

/// <summary>
/// Tüm HTTP-tabanlı sağlayıcıların (İyzico/PayTR/Garanti) ortak yapılandırma şeması.
/// Alanlar sağlayıcıya göre kısmen kullanılır; şifreli JSON olarak saklanır.
/// </summary>
public sealed class ProviderCredentials
{
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? MerchantId { get; set; }
    public string? StoreKey { get; set; }       // Garanti VPOS hash anahtarı
    public string? TerminalId { get; set; }     // Garanti VPOS terminal
    public string? MerchantSalt { get; set; }   // PayTR salt
    public string? BaseUrl { get; set; }        // sağlayıcı uç adresi (sandbox/prod)
    public bool TestMode { get; set; } = true;
    public string? IbanInfo { get; set; }       // BankTransfer: havale yapılacak IBAN/açıklama (provider bunu okuyup müşteriye gösterir)

    // BankTransfer yapısal alanları (yönetim ucu round-trip'i için). Additive: eski şifreli config'lerde
    // bulunmazsa null döner; BankTransferProvider yalnızca IbanInfo'yu okuduğundan davranış değişmez.
    public string? Iban { get; set; }                   // BankTransfer: ham IBAN
    public string? BankAccountName { get; set; }        // BankTransfer: hesap sahibi adı
    public string? BankName { get; set; }               // BankTransfer: banka adı
    public string? BankTransferDescription { get; set; }// BankTransfer: ek açıklama
}
