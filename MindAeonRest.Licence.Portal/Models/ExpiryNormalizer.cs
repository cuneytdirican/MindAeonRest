namespace MindAeonRest.Licence.Portal.Models;

/// <summary>
/// Admin formlarındaki &lt;input type="date"&gt; alanları yalnız tarih taşır; model binder bunu
/// Kind=Unspecified, saat 00:00 olarak bağlar. Süreli lisansta bu değer "gün başı" sayıldığında
/// lisans, seçilen günün başında dolmuş gibi yanlış reddedilebilir. Bu yardımcı bitiş tarihini
/// seçilen günün SONUNA (23:59:59) çekip UTC olarak işaretler; böylece o gün boyunca geçerli kalır.
/// </summary>
public static class ExpiryNormalizer
{
    public static DateTime? ToEndOfDayUtc(DateTime? expiresUtc)
        => expiresUtc is { } e
            ? DateTime.SpecifyKind(e.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc)
            : null;
}
