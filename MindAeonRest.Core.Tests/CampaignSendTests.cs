using System;
using System.Collections.Generic;
using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 5b Kampanya Otomasyonu — SAF (DB/ağ'sız) birim testleri: kanal→iletişim adresi seçimi (segment mantığı çekirdeği)
/// + geri-kazanım eşiği + şablon+model çözümü. Üretim koduyla aynı sınıflar (ProjectReference).
/// </summary>
public class CampaignSendTests
{
    // ---- Kanal → iletişim adresi (segment alıcı çözümünün çekirdeği) ----

    [Fact]
    public void ContactFor_PicksChannelSpecificAddress_And_NullWhenMissing()
    {
        // E-posta kanalı e-postayı, SMS/WhatsApp telefonu seçer.
        Assert.Equal("a@b.com", CampaignSendLogic.ContactFor(NotificationChannel.Email, "5551112233", "a@b.com"));
        Assert.Equal("5551112233", CampaignSendLogic.ContactFor(NotificationChannel.Sms, "5551112233", "a@b.com"));
        Assert.Equal("5551112233", CampaignSendLogic.ContactFor(NotificationChannel.WhatsApp, "5551112233", null));
        // Kanala uygun adres yoksa null → alıcı listesine girmez (fail-safe).
        Assert.Null(CampaignSendLogic.ContactFor(NotificationChannel.Email, "5551112233", "   "));
        Assert.Null(CampaignSendLogic.ContactFor(NotificationChannel.Sms, null, "a@b.com"));
    }

    // ---- Geri kazanım eşiği ----

    [Fact]
    public void IsRecoveryDue_OnlyPastCustomersBeyondThreshold()
    {
        var now = new DateTime(2026, 7, 3);
        Assert.True(CampaignSendLogic.IsRecoveryDue(new DateTime(2026, 5, 1), now, 30));  // 63 gün önce → hedef
        Assert.False(CampaignSendLogic.IsRecoveryDue(new DateTime(2026, 6, 25), now, 30)); // 8 gün önce → değil
        Assert.False(CampaignSendLogic.IsRecoveryDue(null, now, 30));                       // hiç sipariş yok → değil
    }

    // ---- Şablon + model (yer-tutucu) çözümü ----

    [Fact]
    public void BuildModel_And_Render_FillsCampaignPlaceholders()
    {
        var model = CampaignSendLogic.BuildModel("Ahmet", "YAZ25", "%25 indirim");
        var tpl = NotificationTemplates.Get("loyalty-campaign");
        Assert.NotNull(tpl);
        string body = NotificationTemplateEngine.Render(tpl, model);
        Assert.Contains("Ahmet", body);
        Assert.Contains("YAZ25", body);
        Assert.DoesNotContain("{", body); // tüm yer-tutucular dolduruldu
    }
}
