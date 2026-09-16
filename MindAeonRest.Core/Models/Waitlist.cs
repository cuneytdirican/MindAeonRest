namespace MindAeonRest.Models;

/// <summary>Bekleme listesi durumu. Online rezervasyon talebi geldiğinde uygun masa yoksa müşteri bu listeye alınır.</summary>
public enum WaitlistStatus
{
    Bekliyor = 0,
    Cagrildi = 1,
    Geldi = 2,
    Iptal = 3
}

public static class WaitlistStatusExtensions
{
    public static string ToTr(this WaitlistStatus s) => s switch
    {
        WaitlistStatus.Bekliyor => "Bekliyor",
        WaitlistStatus.Cagrildi => "Çağrıldı",
        WaitlistStatus.Geldi => "Geldi",
        WaitlistStatus.Iptal => "İptal",
        _ => s.ToString()
    };
}

/// <summary>Bekleme listesi kaydı (şirket DB). Rezervasyon için uygun masa kalmadığında müşteriyi sıraya alır.
/// Rezervasyon tablosundan BAĞIMSIZDIR (kendi tablosu <c>dbo.Waitlist</c>). Şube kapsamlıdır.</summary>
public class WaitlistEntry : AuditableEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int PartySize { get; set; }
    public DateTime RequestedAt { get; set; }
    public WaitlistStatus Status { get; set; } = WaitlistStatus.Bekliyor;
    public string? Note { get; set; }

    // gösterim
    public string StatusText => Status.ToTr();
    public string RequestedAtText => RequestedAt.ToString("dd.MM.yyyy HH:mm");
    public string Summary => $"{RequestedAt:HH:mm} · {Name} · {PartySize} kişi";
}
