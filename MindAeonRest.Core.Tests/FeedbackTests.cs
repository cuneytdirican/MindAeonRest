using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>FAZ 5d Geri-bildirim hunisi — saf (DB/ağ'sız) birim testi: yüksek puan (>=4) + ayar AÇIK → Google
/// yorum URL'ine yönlendirilir; düşük puan → yalnız içeride kaydedilir (yönlendirme YOK). Üretim koduyla aynı sınıf.</summary>
public class FeedbackTests
{
    private static readonly FeedbackSettings Enabled =
        new() { BranchId = 1, IsEnabled = true, GoogleReviewUrl = "https://g.page/r/abc/review" };

    [Fact]
    public void HighRating_WithEnabledSettings_RedirectsToGoogle()
    {
        Assert.True(FeedbackService.IsHighRating(4));
        Assert.Equal("https://g.page/r/abc/review", FeedbackService.ResolveRedirect(5, Enabled));
    }

    [Fact]
    public void LowRating_DoesNotRedirect_EvenWhenEnabled()
    {
        Assert.False(FeedbackService.IsHighRating(3));
        Assert.Null(FeedbackService.ResolveRedirect(3, Enabled));
    }
}
