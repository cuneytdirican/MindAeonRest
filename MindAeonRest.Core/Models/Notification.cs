namespace MindAeonRest.Models;

/// <summary>Bildirim kanalı (rezervasyon onayı, kampanya, teslimat bildirimi vb. buna dayanır).</summary>
public enum NotificationChannel { Sms = 0, Email = 1, WhatsApp = 2 }

/// <summary>Bildirim sağlayıcısı (kanal bazlı). Netgsm/İletiMerkezi=SMS, Smtp=e-posta, WhatsAppBusiness=WhatsApp.</summary>
public enum NotificationProvider { Yok = 0, Netgsm = 1, IletiMerkezi = 2, Smtp = 3, WhatsAppBusiness = 4 }

/// <summary>Gönderim sonucu durumu (NotificationLog.Status).</summary>
public enum NotificationStatus { Basarisiz = 0, Gonderildi = 1, Simulasyon = 2 }

public static class NotificationEnumExtensions
{
    public static string ToTr(this NotificationChannel c) => c switch
    {
        NotificationChannel.Sms => "SMS",
        NotificationChannel.Email => "E-posta",
        NotificationChannel.WhatsApp => "WhatsApp",
        _ => c.ToString()
    };

    public static string ToTr(this NotificationProvider p) => p switch
    {
        NotificationProvider.Netgsm => "Netgsm (SMS)",
        NotificationProvider.IletiMerkezi => "İleti Merkezi (SMS)",
        NotificationProvider.Smtp => "SMTP (E-posta)",
        NotificationProvider.WhatsAppBusiness => "WhatsApp Business",
        _ => "Yok / Kapalı"
    };

    public static string ToTr(this NotificationStatus s) => s switch
    {
        NotificationStatus.Gonderildi => "Gönderildi",
        NotificationStatus.Simulasyon => "Simülasyon",
        _ => "Başarısız"
    };
}

/// <summary>Bildirim kanal ayarı — ŞUBE + KANAL BAZLI (benzersiz). Sırlar (ApiKey/ApiSecret)
/// DB'de SecretProtector ile ŞİFRELİ tutulur; servis okurken çözer, yazarken şifreler.</summary>
public class NotificationSetting : AuditableEntity
{
    public int BranchId { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationProvider Provider { get; set; } = NotificationProvider.Yok;
    /// <summary>SMS: kullanıcı adı / WhatsApp: erişim jetonu / SMTP: kullanıcı adı.</summary>
    public string? ApiKey { get; set; }
    /// <summary>SMS: parola / SMTP: parola (WhatsApp'ta boş bırakılabilir).</summary>
    public string? ApiSecret { get; set; }
    /// <summary>SMS başlığı / e-posta "From" adresi / WhatsApp gönderen numara kimliği.</summary>
    public string? Sender { get; set; }
    /// <summary>SMTP sunucu adı (yalnız e-posta).</summary>
    public string? Host { get; set; }
    /// <summary>SMTP portu (yalnız e-posta).</summary>
    public int? Port { get; set; }
    /// <summary>SMTP SSL/TLS (yalnız e-posta).</summary>
    public bool UseSsl { get; set; } = true;
    public bool IsEnabled { get; set; }
    /// <summary>TEST modu: gerçek gönderim YAPILMAZ, SIMULASYON döner (fail-closed).</summary>
    public bool TestMode { get; set; } = true;

    public string ChannelText => Channel.ToTr();
    public string ProviderText => Provider.ToTr();
}

/// <summary>Sağlayıcıya iletilen gönderim mesajı (gövde şablon çözümü SERVİSTE yapılmış olarak gelir).</summary>
public class NotifyMessage
{
    public NotificationChannel Channel { get; set; }
    public string To { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? TemplateKey { get; set; }
}

/// <summary>Gönderim sonucu (çağırana döner; NotificationLog'a da yansır).</summary>
public class NotifyResult
{
    public bool Success { get; set; }
    public bool Simulated { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderResponse { get; set; }
    public string? Error { get; set; }

    public NotificationStatus Status =>
        !Success ? NotificationStatus.Basarisiz
        : Simulated ? NotificationStatus.Simulasyon
        : NotificationStatus.Gonderildi;

    public static NotifyResult Fail(string provider, string error)
        => new() { Success = false, ProviderName = provider, Error = error };
    public static NotifyResult Sent(string provider, string response)
        => new() { Success = true, Simulated = false, ProviderName = provider, ProviderResponse = response };
    public static NotifyResult Simulation(string provider, string response)
        => new() { Success = true, Simulated = true, ProviderName = provider, ProviderResponse = response };
}
