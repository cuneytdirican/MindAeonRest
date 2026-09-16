using System.Globalization;

namespace MindAeonRest.Models;

/// <summary>FAZ 5d — Müşteri geri bildirimi (anket): 1-5 yıldız + serbest yorum. KENDİ tablosu (dbo.Feedback).
/// Adisyon kapanınca/ödeme sonrası opsiyonel token'lı anket linki ile toplanır. Yüksek puan (>=4) Google
/// yorum sayfasına yönlendirilir (huni); düşük puan yalnız kaydedilir (içeride kalır, memnuniyetsizlik izlenir).</summary>
public class Feedback : AuditableEntity
{
    public int BranchId { get; set; }
    /// <summary>İlgili adisyon (varsa). Anonim/genel anket için null olabilir.</summary>
    public int? OrderId { get; set; }
    /// <summary>Müşteri telefonu (varsa; SMS ile davet edilmişse). Zorunlu değil.</summary>
    public string? CustomerPhone { get; set; }
    /// <summary>1-5 arası yıldız puanı.</summary>
    public int Rating { get; set; }
    public string? Comment { get; set; }
    /// <summary>Bu geri bildirimde müşteri Google yorum sayfasına yönlendirildi mi (huni ölçümü).</summary>
    public bool GoogleRedirected { get; set; }

    private int Clamped => Math.Clamp(Rating, 0, 5);
    public string Stars => new string('★', Clamped) + new string('☆', 5 - Clamped);
    public string CreatedAtText => CreatedAt == default ? "" : CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR"));
}

/// <summary>Şube bazlı geri bildirim/huni ayarı (opsiyonel). Yüksek puanlı müşteri <see cref="GoogleReviewUrl"/>'e
/// yönlendirilir. Kayıt yoksa/kapalıysa yönlendirme YAPILMAZ (fail-closed) — yalnız içeride kaydedilir.</summary>
public class FeedbackSettings
{
    public int BranchId { get; set; }
    public string? GoogleReviewUrl { get; set; }
    public bool IsEnabled { get; set; }
}

/// <summary>Geri bildirim rapor özeti: adet + ortalama puan + kayıt listesi.</summary>
public class FeedbackStats
{
    public int Count { get; set; }
    public double Average { get; set; }
    public List<Feedback> Items { get; set; } = new();

    public string AverageText => Count == 0 ? "-" : Average.ToString("0.0", CultureInfo.GetCultureInfo("tr-TR"));
}
