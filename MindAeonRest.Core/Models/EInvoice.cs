namespace MindAeonRest.Models;

/// <summary>e-Fatura/e-Arşiv entegrasyon sağlayıcısı (eklenti modül).</summary>
public enum EInvoiceProvider { Yok = 0, EDM = 1, Uyumsoft = 2 }

public static class EInvoiceProviderExtensions
{
    public static string ToTr(this EInvoiceProvider p) => p switch
    {
        EInvoiceProvider.EDM => "EDM e-Fatura",
        EInvoiceProvider.Uyumsoft => "Uyumsoft e-Fatura",
        _ => "Yok / Kapalı"
    };
}

/// <summary>e-Fatura entegrasyon ayarları (şirket geneli, tek kayıt).</summary>
public class EInvoiceSettings : AuditableEntity
{
    public EInvoiceProvider Provider { get; set; } = EInvoiceProvider.Yok;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool IsTest { get; set; } = true;
    public bool IsEnabled { get; set; }
}

public class EInvoiceLine
{
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>Sağlayıcıya gönderilecek fatura isteği (ClFiche/Order'dan türetilir).</summary>
public class EInvoiceRequest
{
    public bool IsEArsiv { get; set; }          // bireysel/PA → e-Arşiv, kurumsal VKN → e-Fatura
    public string CustomerName { get; set; } = string.Empty;
    public string? TaxNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? Address { get; set; }
    public DateTime Date { get; set; }
    public List<EInvoiceLine> Lines { get; set; } = new();
    public decimal MatrahTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
}

public class EInvoiceResult
{
    public bool Success { get; set; }
    public string? Ettn { get; set; }
    public string? InvoiceNo { get; set; }
    public string? Message { get; set; }
    /// <summary>TEST/SİMÜLASYON sonucu mu (gerçek GİB gönderimi yok).</summary>
    public bool Simulated { get; set; }
    /// <summary>Sağlayıcının ham yanıtı (SOAP/REST gövdesi / hata) — log/teşhis için.</summary>
    public string? ProviderResponse { get; set; }
    public EInvoiceStatus Status { get; set; } = EInvoiceStatus.Bekliyor;
}

/// <summary>e-Fatura gönderim durumu (EInvoiceLog.Status).</summary>
public enum EInvoiceStatus { Bekliyor = 0, Gonderildi = 1, Iptal = 2, Hata = 3 }

public static class EInvoiceStatusExtensions
{
    public static string ToTr(this EInvoiceStatus s) => s switch
    {
        EInvoiceStatus.Gonderildi => "Gönderildi",
        EInvoiceStatus.Iptal => "İptal",
        EInvoiceStatus.Hata => "Hata",
        _ => "Bekliyor"
    };
}

/// <summary>Bir siparişe ait e-Fatura gönderim kaydı (fiş→ETTN eşlemesi, durum, sağlayıcı yanıtı).</summary>
public class EInvoiceLogEntry
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? BranchId { get; set; }
    public EInvoiceProvider Provider { get; set; }
    public bool IsEArsiv { get; set; }
    public bool IsTest { get; set; }
    public EInvoiceStatus Status { get; set; }
    public string? Ettn { get; set; }
    public string? InvoiceNo { get; set; }
    public string? CustomerName { get; set; }
    public string? TaxNumber { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public string? Message { get; set; }
    public string? ProviderResponse { get; set; }
    public DateTime CreatedAt { get; set; }

    public string StatusText => Status.ToTr();
    public string TypeText => IsEArsiv ? "e-Arşiv" : "e-Fatura";
    public string DateText => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
