using System.Security.Cryptography;
using System.Text;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Online teslimat platformu sağlayıcı arayüzü (Trendyol/Getir/Yemeksepeti…).
/// Gerçek entegrasyon (REST/webhook) bu arayüzü implemente eder; çekirdek yalnız arayüzü + seçimi bilir.
/// <para>FAIL-CLOSED: kimlik/anahtar yoksa gerçek API çağrısı YAPILMAZ. Yalnız <see cref="SimulateInbound"/>
/// (TEST) gerçek çağrı olmadan örnek sipariş üretir — uçtan uca iç akış testi için.</para></summary>
public interface IDeliveryPlatformClient
{
    DeliveryPlatform Platform { get; }
    string Name { get; }

    /// <summary>Polling: platformdan bekleyen siparişleri çeker. GERÇEK entegrasyon yok → yapılandırılsa bile
    /// bu turda boş liste + "aktif değil" (fail-closed). İleride gerçek REST çağrısı buraya bağlanır.</summary>
    IReadOnlyList<InboundOrder> PullOrders(DeliveryPlatformSetting setting);

    /// <summary>Webhook imza/secret doğrulaması (fail-closed). Secret tanımsız → false (reddet).</summary>
    bool VerifyWebhookSignature(string payload, string? signature, DeliveryPlatformSetting setting);

    /// <summary>Webhook ham gövdesini normalize edilmiş <see cref="InboundOrder"/>'a çevirir (çözemezse null).</summary>
    InboundOrder? ParseWebhook(string payload, DeliveryPlatformSetting setting);

    /// <summary>Yerel durum değişimini platforma geri bildirir (kabul/hazır/yolda/teslim). GERÇEK çağrı YOK
    /// (fail-closed / simülasyon) — para/işlem yan etkisi olmayan salt bildirim.</summary>
    DeliveryClientResult PushStatus(DeliveryPlatformSetting setting, string externalOrderNo, DeliveryStatus status);

    /// <summary>TEST/SİMÜLASYON: gerçek API çağrısı OLMADAN örnek bir gelen sipariş üretir (uçtan uca iç akış testi).
    /// <paramref name="sampleItems"/> verilirse (katalogdan gerçek ürünler) kalemler onlarla kurulur → mutfağa
    /// gerçekten düşen satırlar oluşur; verilmezse jenerik/eşleşmeyebilecek kalemler üretilir.</summary>
    InboundOrder SimulateInbound(DeliveryPlatformSetting setting, IReadOnlyList<(string Code, string Name, decimal Price)>? sampleItems = null);
}

/// <summary>Platform sağlayıcı çağrısının sonucu (gerçek gönderim yok → Simulated/başarısız bilgisi taşır).</summary>
public sealed class DeliveryClientResult
{
    public bool Success { get; init; }
    public bool Simulated { get; init; }
    public string Message { get; init; } = string.Empty;

    public static DeliveryClientResult Fail(string msg) => new() { Success = false, Message = msg };
    public static DeliveryClientResult Sim(string msg) => new() { Success = true, Simulated = true, Message = msg };
}

/// <summary>Ortak sağlayıcı davranışı: FAIL-CLOSED kontrol + HMAC imza doğrulama + simülasyon üreteci.
/// (EInvoiceStub deseninin teslimat karşılığı.)</summary>
public abstract class DeliveryClientBase : IDeliveryPlatformClient
{
    public abstract DeliveryPlatform Platform { get; }
    public abstract string Name { get; }

    /// <summary>Gerçek gönderim öncesi yapılandırma kontrolü (DeliveryPlatformService.IsConfigured aynası).</summary>
    protected bool IsConfigured(DeliveryPlatformSetting s) => DeliveryPlatformService.IsConfigured(s);

    public IReadOnlyList<InboundOrder> PullOrders(DeliveryPlatformSetting setting)
    {
        // Gerçek REST entegrasyonu eklenti modülde gelecek. O gelene kadar polling FAIL-CLOSED:
        // yapılandırılmamışsa da, yapılandırılmışsa da GERÇEK ÇAĞRI YOK → boş liste.
        return Array.Empty<InboundOrder>();
    }

    public bool VerifyWebhookSignature(string payload, string? signature, DeliveryPlatformSetting setting)
    {
        // FAIL-CLOSED: kanal etkin değilse ya da secret yoksa doğrulanamaz → reddet.
        if (!setting.IsEnabled || string.IsNullOrWhiteSpace(setting.SecretKey)) return false;
        if (string.IsNullOrWhiteSpace(signature)) return false;
        string expected = ComputeHmac(payload, setting.SecretKey!);
        // sabit-zamanlı karşılaştırma (timing attack'e kapalı)
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature.Trim()));
    }

    /// <summary>Webhook gövdesini normalize eder. Ortak biçim (JSON) çözülemezse null.
    /// Gerçek platform şemaları eklenti modülde; burada sağlayıcıya özgü alan adları ez-geçilir (override).</summary>
    public virtual InboundOrder? ParseWebhook(string payload, DeliveryPlatformSetting setting)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            var ord = System.Text.Json.JsonSerializer.Deserialize<InboundOrder>(payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (ord is null || string.IsNullOrWhiteSpace(ord.ExternalOrderNo)) return null;
            ord.Platform = Platform;
            ord.StoreId ??= setting.StoreId;
            ord.RawJson = payload;
            return ord;
        }
        catch { return null; }
    }

    public DeliveryClientResult PushStatus(DeliveryPlatformSetting setting, string externalOrderNo, DeliveryStatus status)
    {
        if (!IsConfigured(setting))
            return DeliveryClientResult.Fail($"{Name} yapılandırılmadı; durum geri bildirimi yapılamadı.");
        // GERÇEK entegrasyon henüz yok → platforma sahte "gönderildi" dönmeyiz; açıkça SİMÜLASYON.
        return DeliveryClientResult.Sim(
            $"{Name} durum senkronu SİMÜLASYON — sipariş {externalOrderNo} → {status.ToTr()} (gerçek gönderim YOK).");
    }

    public InboundOrder SimulateInbound(DeliveryPlatformSetting setting,
        IReadOnlyList<(string Code, string Name, decimal Price)>? sampleItems = null)
    {
        string ext = $"SIM-{Platform}-{DateTime.Now:yyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
        var order = new InboundOrder
        {
            Platform = Platform,
            ExternalOrderNo = ext,
            StoreId = setting.StoreId,
            CustomerName = $"{Name} Test Müşteri",
            CustomerPhone = "05000000000",
            CustomerAddress = "Simülasyon Mah. Test Cad. No:1",
            Note = $"{Name} SİMÜLASYON siparişi (gerçek platform çağrısı YOK).",
            IsSimulation = true
        };
        if (sampleItems is { Count: > 0 })
            foreach (var (code, name, price) in sampleItems.Take(3))
                order.Items.Add(new InboundOrderItem { ExternalCode = code, Name = name, Qty = 1, UnitPrice = price });
        else
            // Fiyat BELİRTİLMEDİ (null) → içeri alınırken katalogdan çözülür (eski '0 ise katalog' davranışının karşılığı).
            order.Items.Add(new InboundOrderItem { ExternalCode = "SIM-URUN", Name = "Simülasyon Ürünü", Qty = 1, UnitPrice = null });

        order.Total = order.Items.Sum(i => i.Qty * (i.UnitPrice ?? 0m));
        order.RawJson = System.Text.Json.JsonSerializer.Serialize(order);
        return order;
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

/// <summary>Trendyol Yemek entegrasyon yuvası (gerçek REST/webhook eklenti modülde bağlanacak).</summary>
public sealed class TrendyolClient : DeliveryClientBase
{
    public override DeliveryPlatform Platform => DeliveryPlatform.Trendyol;
    public override string Name => "Trendyol";
}

/// <summary>Getir Yemek entegrasyon yuvası (gerçek REST/webhook eklenti modülde bağlanacak).</summary>
public sealed class GetirClient : DeliveryClientBase
{
    public override DeliveryPlatform Platform => DeliveryPlatform.Getir;
    public override string Name => "Getir";
}

/// <summary>Yemeksepeti entegrasyon yuvası (gerçek REST/webhook eklenti modülde bağlanacak).</summary>
public sealed class YemekSepetiClient : DeliveryClientBase
{
    public override DeliveryPlatform Platform => DeliveryPlatform.YemekSepeti;
    public override string Name => "Yemeksepeti";
}

/// <summary>Platform enum'ından uygun sağlayıcı istemcisini üretir (fabrika).</summary>
public static class DeliveryClientFactory
{
    public static IDeliveryPlatformClient? Get(DeliveryPlatform platform) => platform switch
    {
        DeliveryPlatform.Trendyol => new TrendyolClient(),
        DeliveryPlatform.Getir => new GetirClient(),
        DeliveryPlatform.YemekSepeti => new YemekSepetiClient(),
        _ => null
    };
}
