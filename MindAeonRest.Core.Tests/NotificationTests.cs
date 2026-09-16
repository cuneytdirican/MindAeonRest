using System.Collections.Generic;
using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 1b Bildirim altyapısı — saf (DB/ağ'sız) birim testleri: şablon yer-tutucu değişimi +
/// sağlayıcı seçim mantığı + fail-closed davranış. Üretim koduyla aynı sınıflar (ProjectReference).
/// </summary>
public class NotificationTests
{
    // ---- Şablon motoru (yer-tutucu) ----

    [Fact]
    public void Render_ReplacesAllPlaceholders()
    {
        var model = new Dictionary<string, string?> { ["ad"] = "Ahmet", ["tutar"] = "150 TL", ["kod"] = "RZ42" };
        string body = NotificationTemplateEngine.Render("Sayın {ad}, tutar {tutar}, kod {kod}.", model);
        Assert.Equal("Sayın Ahmet, tutar 150 TL, kod RZ42.", body);
    }

    [Fact]
    public void Render_UnknownPlaceholderLeftIntact_And_NullModelReturnsTemplate()
    {
        var model = new Dictionary<string, string?> { ["ad"] = "Ayşe" };
        Assert.Equal("Merhaba Ayşe, {eksik}", NotificationTemplateEngine.Render("Merhaba {ad}, {eksik}", model));
        Assert.Equal("Sabit metin", NotificationTemplateEngine.Render("Sabit metin", null));
    }

    [Fact]
    public void BuiltinTemplate_ResolvesAndRenders()
    {
        var tpl = NotificationTemplates.Get("reservation-confirm");
        Assert.NotNull(tpl);
        var model = new Dictionary<string, string?> { ["ad"] = "Mehmet", ["tarih"] = "05.07.2026", ["kod"] = "R100" };
        string body = NotificationTemplateEngine.Render(tpl, model);
        Assert.Contains("Sayın Mehmet", body);
        Assert.Contains("R100", body);
        Assert.DoesNotContain("{", body); // tüm yer-tutucular dolduruldu
    }

    // ---- Sağlayıcı seçimi ----

    [Fact]
    public void Factory_MapsProviderEnumToImplementation()
    {
        Assert.IsType<NetgsmProvider>(NotificationProviderFactory.Get(NotificationProvider.Netgsm));
        Assert.IsType<IletiMerkeziProvider>(NotificationProviderFactory.Get(NotificationProvider.IletiMerkezi));
        Assert.IsType<SmtpProvider>(NotificationProviderFactory.Get(NotificationProvider.Smtp));
        Assert.IsType<WhatsAppBusinessProvider>(NotificationProviderFactory.Get(NotificationProvider.WhatsAppBusiness));
        Assert.Null(NotificationProviderFactory.Get(NotificationProvider.Yok));
    }

    [Fact]
    public void ProviderChannel_MatchesExpectedChannel()
    {
        Assert.Equal(NotificationChannel.Sms, new NetgsmProvider().Channel);
        Assert.Equal(NotificationChannel.Email, new SmtpProvider().Channel);
        Assert.Equal(NotificationChannel.WhatsApp, new WhatsAppBusinessProvider().Channel);
    }

    // ---- Fail-closed davranış (gerçek gönderim YOK) ----

    [Fact]
    public async Task Sms_TestMode_ReturnsSimulation_NotRealSend()
    {
        var s = new NotificationSetting
        {
            Channel = NotificationChannel.Sms, Provider = NotificationProvider.Netgsm,
            ApiKey = "user", ApiSecret = "pass", Sender = "MAGAZA", IsEnabled = true, TestMode = true
        };
        var res = await new NetgsmProvider().SendAsync(
            new NotifyMessage { Channel = NotificationChannel.Sms, To = "5551112233", Body = "test" }, s);
        Assert.True(res.Success);
        Assert.True(res.Simulated);
        Assert.Equal(NotificationStatus.Simulasyon, res.Status);
        Assert.StartsWith("SIMULASYON-", res.ProviderResponse);
    }

    [Fact]
    public async Task Sms_ProductionMode_DoesNotReallySend_FailClosed()
    {
        var s = new NotificationSetting
        {
            Channel = NotificationChannel.Sms, Provider = NotificationProvider.Netgsm,
            ApiKey = "user", ApiSecret = "pass", Sender = "MAGAZA", IsEnabled = true, TestMode = false
        };
        var res = await new NetgsmProvider().SendAsync(
            new NotifyMessage { Channel = NotificationChannel.Sms, To = "5551112233", Body = "test" }, s);
        Assert.False(res.Success);
        Assert.Contains("GERÇEK GÖNDERİM YAPILMADI", res.Error);
    }

    [Fact]
    public async Task Sms_MissingConfig_FailsWithoutSending()
    {
        var s = new NotificationSetting
        {
            Channel = NotificationChannel.Sms, Provider = NotificationProvider.Netgsm,
            IsEnabled = true, TestMode = true // anahtar/başlık eksik
        };
        var res = await new NetgsmProvider().SendAsync(
            new NotifyMessage { Channel = NotificationChannel.Sms, To = "5551112233", Body = "test" }, s);
        Assert.False(res.Success);
        Assert.Contains("yapılandırılmadı", res.Error);
    }
}
