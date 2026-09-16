using System.Net;
using System.Net.Mail;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// Bildirim sağlayıcı arayüzü (SMS / e-posta / WhatsApp). Gerçek entegrasyon bu arayüzü implemente eder.
/// EInvoice/DeliveryPlatform deseni gibi FAIL-CLOSED: yapılandırma eksikse gerçek gönderim YAPILMAZ,
/// TEST modunda açıkça SIMULASYON döner (kullanıcı sahte "gönderildi" ile yanılmaz).
/// Not: <paramref name="settings"/> içindeki ApiKey/ApiSecret servis tarafından ÇÖZÜLMÜŞ (düz) gelir.
/// </summary>
public interface INotificationProvider
{
    string Name { get; }
    NotificationChannel Channel { get; }
    Task<NotifyResult> SendAsync(NotifyMessage msg, NotificationSetting settings);
}

/// <summary>Netgsm SMS sağlayıcısı (Türkiye) — HTTP API yuvası. Gerçek gönderim eklenti modülde gelecek.</summary>
public class NetgsmProvider : INotificationProvider
{
    public string Name => "Netgsm";
    public NotificationChannel Channel => NotificationChannel.Sms;
    public Task<NotifyResult> SendAsync(NotifyMessage msg, NotificationSetting s)
        => Task.FromResult(NotificationStub.Send(Name, msg, s, SmsConfigured, "kullanıcı/parola/başlık"));

    internal static bool SmsConfigured(NotificationSetting s)
        => !string.IsNullOrWhiteSpace(s.ApiKey) && !string.IsNullOrWhiteSpace(s.ApiSecret) && !string.IsNullOrWhiteSpace(s.Sender);
}

/// <summary>İleti Merkezi SMS sağlayıcısı (Türkiye) — HTTP API yuvası. Gerçek gönderim eklenti modülde gelecek.</summary>
public class IletiMerkeziProvider : INotificationProvider
{
    public string Name => "İleti Merkezi";
    public NotificationChannel Channel => NotificationChannel.Sms;
    public Task<NotifyResult> SendAsync(NotifyMessage msg, NotificationSetting s)
        => Task.FromResult(NotificationStub.Send(Name, msg, s, NetgsmProvider.SmsConfigured, "kullanıcı/parola/başlık"));
}

/// <summary>WhatsApp Business Cloud API sağlayıcısı — yuva (fail-closed / simülasyon).</summary>
public class WhatsAppBusinessProvider : INotificationProvider
{
    public string Name => "WhatsApp Business";
    public NotificationChannel Channel => NotificationChannel.WhatsApp;
    public Task<NotifyResult> SendAsync(NotifyMessage msg, NotificationSetting s)
        => Task.FromResult(NotificationStub.Send(Name, msg, s, WaConfigured, "erişim jetonu/gönderen"));

    private static bool WaConfigured(NotificationSetting s)
        => !string.IsNullOrWhiteSpace(s.ApiKey) && !string.IsNullOrWhiteSpace(s.Sender);
}

/// <summary>SMTP e-posta sağlayıcısı — System.Net.Mail ile GERÇEK gönderim yapabilir (ayar tamsa, TEST kapalıysa).
/// TEST modunda simüle eder. SMTP gerçek gönderimi güvenli/basit olduğundan (harici anahtar gerektirmez) uygulanmıştır.</summary>
public class SmtpProvider : INotificationProvider
{
    public string Name => "SMTP";
    public NotificationChannel Channel => NotificationChannel.Email;

    internal static bool Configured(NotificationSetting s)
        => !string.IsNullOrWhiteSpace(s.Host) && s.Port is > 0 && !string.IsNullOrWhiteSpace(s.Sender);

    public async Task<NotifyResult> SendAsync(NotifyMessage msg, NotificationSetting s)
    {
        if (!Configured(s))
            return NotifyResult.Fail(Name, $"{Name} yapılandırılmadı (sunucu/port/gönderen eksik).");
        if (string.IsNullOrWhiteSpace(msg.To))
            return NotifyResult.Fail(Name, "Alıcı e-posta adresi boş.");

        // TEST modu: gerçek gönderim YOK — açıkça simülasyon.
        if (s.TestMode)
            return NotifyResult.Simulation(Name, "SIMULASYON-" + Guid.NewGuid().ToString("N")[..12]);

        try
        {
            using var client = new SmtpClient(s.Host!, s.Port!.Value) { EnableSsl = s.UseSsl };
            if (!string.IsNullOrWhiteSpace(s.ApiKey))
                client.Credentials = new NetworkCredential(s.ApiKey, s.ApiSecret ?? "");
            using var mail = new MailMessage(s.Sender!, msg.To)
            {
                Subject = msg.Subject ?? "",
                Body = msg.Body,
                IsBodyHtml = false
            };
            await client.SendMailAsync(mail);
            return NotifyResult.Sent(Name, "E-posta gönderildi.");
        }
        catch (Exception ex)
        {
            return NotifyResult.Fail(Name, ex.Message);
        }
    }
}

/// <summary>SMS/WhatsApp iskele sağlayıcıları için ORTAK fail-closed davranışı (EInvoiceStub muadili).
/// Üretimde asla sahte başarı dönmez; yalnız TEST modunda açıkça SIMULASYON döner.</summary>
internal static class NotificationStub
{
    public static NotifyResult Send(string name, NotifyMessage msg, NotificationSetting s,
        Func<NotificationSetting, bool> configured, string configHint)
    {
        if (!configured(s))
            return NotifyResult.Fail(name, $"{name} yapılandırılmadı ({configHint} eksik); gönderim yapılamaz.");
        if (string.IsNullOrWhiteSpace(msg.To))
            return NotifyResult.Fail(name, "Alıcı boş.");

        if (!s.TestMode)
            return NotifyResult.Fail(name, $"{name} entegrasyonu henüz aktif değil (eklenti modül). GERÇEK GÖNDERİM YAPILMADI.");

        return NotifyResult.Simulation(name, "SIMULASYON-" + Guid.NewGuid().ToString("N")[..12]);
    }
}

/// <summary>Sağlayıcı seçimi: ayardaki NotificationProvider → uygulama. Bilinmeyen/Yok → null.</summary>
public static class NotificationProviderFactory
{
    public static INotificationProvider? Get(NotificationProvider p) => p switch
    {
        NotificationProvider.Netgsm => new NetgsmProvider(),
        NotificationProvider.IletiMerkezi => new IletiMerkeziProvider(),
        NotificationProvider.Smtp => new SmtpProvider(),
        NotificationProvider.WhatsAppBusiness => new WhatsAppBusinessProvider(),
        _ => null
    };
}
