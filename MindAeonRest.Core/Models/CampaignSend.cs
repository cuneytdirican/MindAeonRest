namespace MindAeonRest.Models;

/// <summary>Kampanya gönderimi hedef segmenti: Tümü / Müşteri Grubu / (bugün) Doğum Günü / Geri Kazanım (son-N-gün gelmeyen).</summary>
public enum CampaignSegment { Tumu = 0, MusteriGrubu = 1, DogumGunu = 2, GeriKazanim = 3 }

public static class CampaignSegmentExtensions
{
    public static string ToTr(this CampaignSegment s) => s switch
    {
        CampaignSegment.Tumu => "Tüm Müşteriler",
        CampaignSegment.MusteriGrubu => "Müşteri Grubu",
        CampaignSegment.DogumGunu => "Doğum Günü (Bugün)",
        CampaignSegment.GeriKazanim => "Geri Kazanım (Uzun Süredir Gelmeyen)",
        _ => s.ToString()
    };
}

/// <summary>Toplu gönderim alıcısı — segment sorgusundan çözülmüş TEK iletişim adresi (kanal bazlı) ile.</summary>
public class CampaignSendRecipient
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Kanala göre çözülmüş adres: SMS/WhatsApp = telefon, E-posta = e-posta.</summary>
    public string Contact { get; set; } = string.Empty;
}

/// <summary>dbo.CampaignSendLog satırı (yalnız INSERT + okuma; NotificationLog muadili, standart audit YOK).</summary>
public class CampaignSendLogRow
{
    public int Id { get; set; }
    public int? CampaignId { get; set; }
    public int? BranchId { get; set; }
    public CampaignSegment Segment { get; set; }
    public NotificationChannel Channel { get; set; }
    public int? CustomerId { get; set; }
    public string? Recipient { get; set; }
    public NotificationStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime SentAt { get; set; }

    public string ChannelText => Channel.ToTr();
    public string StatusText => Status.ToTr();
    public string SegmentText => Segment.ToTr();
}

/// <summary>Toplu gönderim özeti (sayaçlar) + gönderilen alıcı-sonuç ayrıntısı.</summary>
public class CampaignSendResult
{
    public int Total { get; set; }
    public int Sent { get; set; }
    public int Simulated { get; set; }
    public int Failed { get; set; }
    public List<(string Contact, NotificationStatus Status, string? Info)> Items { get; } = new();
}

/// <summary>Kampanya gönderim SAF (DB/ağ'sız) mantığı — birim test edilebilir çekirdek.
/// Kanal→iletişim adresi seçimi ve geri-kazanım eşik kararı burada; segment SQL'i servistedir.</summary>
public static class CampaignSendLogic
{
    /// <summary>Kanala uygun iletişim adresini seçer. SMS/WhatsApp = telefon, E-posta = e-posta.
    /// Adres yok/boşsa null döner → alıcı listesine GİRMEZ (fail-safe: boş numaraya gönderim denenmez).</summary>
    public static string? ContactFor(NotificationChannel channel, string? phone, string? email)
    {
        string? raw = channel == NotificationChannel.Email ? email : phone;
        raw = raw?.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    /// <summary>Geri kazanım: müşterinin SON sipariş tarihi, eşik günden daha eskiyse (ya da hiç yoksa değil) hedeftir.
    /// <paramref name="lastOrder"/> null ise (hiç sipariş yok) hedef DEĞİLDİR (geri "kazanılacak" bir ilişki yok).</summary>
    public static bool IsRecoveryDue(DateTime? lastOrder, DateTime now, int days)
        => lastOrder is { } last && days > 0 && last < now.AddDays(-days);

    /// <summary>Alıcı + (opsiyonel) kampanya için şablon yer-tutucu modeli: {ad},{kod},{tutar}.</summary>
    public static IReadOnlyDictionary<string, string?> BuildModel(string customerName, string? campaignCode, string? campaignValueText)
        => new Dictionary<string, string?>
        {
            ["ad"] = customerName,
            ["kod"] = campaignCode ?? string.Empty,
            ["tutar"] = campaignValueText ?? string.Empty,
        };
}
