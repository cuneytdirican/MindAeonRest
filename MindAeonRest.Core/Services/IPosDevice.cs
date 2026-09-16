namespace MindAeonRest.Services;

public record PosResult(bool Approved, string? RefNo = null, string? Message = null);

/// <summary>
/// Kredi kartı / ÖKC cihaz arayüzü. Yeni Nesil ÖKC lisansı varsa gerçek sürücü implemente eder
/// (tutarı POS'a gönderir, banka onayını bekler). Lisans yoksa ManualPosDevice (manuel onay) kullanılır.
/// </summary>
public interface IPosDevice
{
    PosResult Charge(decimal amount, int currencyId);
}

/// <summary>ÖKC lisansı yokken: ödeme fiziksel POS'ta elle alınır, sistemde onaylanmış kabul edilir.</summary>
public class ManualPosDevice : IPosDevice
{
    public PosResult Charge(decimal amount, int currencyId) => new(true, null, "Manuel POS");
}

/// <summary>Aktif POS cihazını sağlar (ileride ÖKC lisansına göre seçilecek).</summary>
public static class PosDeviceFactory
{
    public static IPosDevice Current { get; set; } = new ManualPosDevice();
}
