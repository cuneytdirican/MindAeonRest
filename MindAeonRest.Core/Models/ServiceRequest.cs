namespace MindAeonRest.Models;

/// <summary>Garson/masa servis isteği tipi.</summary>
public enum ServiceRequestType
{
    CagriGarson = 0,    // Masaya çağırıyor (call waiter)
    MasaTemizlik = 1,   // Servis açılışı / masa temizliği (Yeni Servis)
    Hesap = 2,          // Hesap istiyor
    Diger = 3
}

public enum ServiceRequestStatus { Bekliyor = 0, Tamamlandi = 1 }

/// <summary>Masaya bağlı açık servis isteği (çağrı / temizlik / hesap). Garson terminali "Yeni Servis" sekmesi + masa rozetleri kullanır.</summary>
public class ServiceRequest : AuditableEntity
{
    public int BranchId { get; set; }
    public int? TableId { get; set; }
    public string? TableCode { get; set; }     // gösterim
    public int? ZoneId { get; set; }
    public string? ZoneName { get; set; }       // gösterim
    public int? OrderId { get; set; }
    public ServiceRequestType Type { get; set; }
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Bekliyor;
    public string? Note { get; set; }
    public int KioskNo { get; set; } = 1;
    public DateTime? CompletedAt { get; set; }

    public string TypeText => Type switch
    {
        ServiceRequestType.CagriGarson => "Masaya Çağırıyor",
        ServiceRequestType.MasaTemizlik => "Servis açılışı / masa temizliği bekliyor",
        ServiceRequestType.Hesap => "Hesap İstiyor",
        _ => "Diğer"
    };

    public string ElapsedText
    {
        get
        {
            var s = DateTime.UtcNow - CreatedAt;
            if (s.TotalMinutes < 0) s = TimeSpan.Zero;
            return s.TotalHours >= 1 ? $"{(int)s.TotalHours}sa {s.Minutes}dk" : $"{s.Minutes}dk";
        }
    }
}
