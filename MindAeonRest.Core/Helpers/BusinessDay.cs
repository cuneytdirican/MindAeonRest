namespace MindAeonRest.Helpers;

/// <summary>İŞ GÜNÜ (gün başlangıç saati / kesim) yardımcıları.
/// Şube bazlı <c>DayStartMinutes</c> (gece yarısından sonra geçen dakika; 0 = gece yarısı = mevcut davranış)
/// ile, bir yerel/UTC zamanın hangi "iş gününe" düştüğünü hesaplar.
///
/// <para>SQL KARŞILIĞI (bir UTC kolonun iş-günü tarihi). <c>@cutoff</c> = ilgili şubenin DayStartMinutes:</para>
/// <code>
/// CAST(
///   DATEADD(MINUTE,
///           DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff,
///           &lt;utcKol&gt;)
///   AS DATE)
/// </code>
/// <para>Açıklama: <c>DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME())</c> sunucunun yerel saat ofsetini
/// (dakika) verir; UTC kolon önce yerele çevrilir, sonra kesim (<c>@cutoff</c>) geri çekilir ve gün (DATE) alınır.
/// <c>@cutoff = 0</c> iken sonuç, yerel takvim gününe eşittir (geriye uyumlu).</para>
/// </summary>
public static class BusinessDay
{
    /// <summary>Şu anki yerel zamana göre aktif iş günü tarihi (saat 00:00).
    /// <paramref name="dayStartMinutes"/> kadar geri çekip günü alır.</summary>
    public static DateTime CurrentDate(int dayStartMinutes)
        => DateTime.Now.AddMinutes(-dayStartMinutes).Date;

    /// <summary>Verilen YEREL zamanın düştüğü iş günü tarihi (saat 00:00).</summary>
    public static DateTime DateOf(DateTime localWhen, int dayStartMinutes)
        => localWhen.AddMinutes(-dayStartMinutes).Date;
}
