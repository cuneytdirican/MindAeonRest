using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>FAZ 5d — Geri-bildirim / yorum hunisi. Anket kaydı (1-5 yıldız + yorum) + istatistik (ort. puan) +
/// şube bazlı Google yorum ayarı. EF YOK, ADO.NET (Database.OpenCompany). Additive; mevcut akışları etkilemez.
///
/// HUNİ MANTIĞI (saf, test edilebilir): puan >= <see cref="HighRatingThreshold"/> ve şube ayarı AÇIK + geçerli
/// Google linki varsa müşteri Google yorum sayfasına yönlendirilir (dış itibar); düşük puan YALNIZ içeride
/// kaydedilir (memnuniyetsizlik izlenir, dışarı sızmaz). Ayar yoksa/kapalıysa → fail-closed, yönlendirme YOK.</summary>
public class FeedbackService
{
    /// <summary>"Yüksek puan" eşiği — bu değer ve üzeri Google'a yönlendirilebilir.</summary>
    public const int HighRatingThreshold = 4;

    /// <summary>Puan yüksek mi (huni eşiği). Saf fonksiyon — DB/ağ yok.</summary>
    public static bool IsHighRating(int rating) => rating >= HighRatingThreshold;

    /// <summary>Huni kararı (saf fonksiyon): yüksek puan + ayar AÇIK + geçerli link → hedef URL; aksi halde null
    /// (yalnız içeride kaydet). Yönlendirme URL'i döndürülürse çağıran müşteriyi oraya yollar.</summary>
    public static string? ResolveRedirect(int rating, FeedbackSettings? settings)
        => IsHighRating(rating)
           && settings is { IsEnabled: true }
           && !string.IsNullOrWhiteSpace(settings.GoogleReviewUrl)
            ? settings.GoogleReviewUrl!.Trim()
            : null;

    /// <summary>Anket kaydını ekler (rating 1-5 aralığına kırpılır). Kaydın Id'sini döndürür.</summary>
    public int Save(Feedback f, int? userId = null)
    {
        int rating = Math.Clamp(f.Rating, 1, 5);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO dbo.Feedback
            (BranchId, OrderId, CustomerPhone, Rating, Comment, GoogleRedirected, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES (@b,@o,@ph,@r,@cm,@g,@by);";
        cmd.Parameters.AddWithValue("@b", f.BranchId);
        cmd.Parameters.AddWithValue("@o", (object?)f.OrderId ?? DBNull.Value);
        CompanyService.AddNullable(cmd, "@ph", f.CustomerPhone);
        cmd.Parameters.AddWithValue("@r", rating);
        CompanyService.AddNullable(cmd, "@cm", f.Comment);
        cmd.Parameters.AddWithValue("@g", f.GoogleRedirected);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        return (int)cmd.ExecuteScalar();
    }

    /// <summary>Şube geri bildirim ayarı (kayıt yoksa varsayılan kapalı/boş ayar).</summary>
    public FeedbackSettings GetSettings(int branchId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BranchId, GoogleReviewUrl, IsEnabled FROM dbo.FeedbackSettings WHERE BranchId=@b;";
        cmd.Parameters.AddWithValue("@b", branchId);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new FeedbackSettings { BranchId = r.GetInt32(0), GoogleReviewUrl = r.IsDBNull(1) ? null : r.GetString(1), IsEnabled = r.GetBoolean(2) }
            : new FeedbackSettings { BranchId = branchId };
    }

    /// <summary>Şube geri bildirim ayarını ekler/günceller (şube başına tek satır).</summary>
    public void SaveSettings(FeedbackSettings s, int? userId = null)
    {
        using var c = Database.OpenCompany();
        int? id;
        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Id FROM dbo.FeedbackSettings WHERE BranchId=@b;";
            q.Parameters.AddWithValue("@b", s.BranchId);
            id = q.ExecuteScalar() as int?;
        }
        using var cmd = c.CreateCommand();
        if (id is null)
        {
            cmd.CommandText = @"INSERT INTO dbo.FeedbackSettings (BranchId, GoogleReviewUrl, IsEnabled, CreatedBy)
                                VALUES (@b,@u,@e,@by);";
        }
        else
        {
            cmd.CommandText = @"UPDATE dbo.FeedbackSettings SET GoogleReviewUrl=@u, IsEnabled=@e,
                                UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id.Value);
        }
        cmd.Parameters.AddWithValue("@b", s.BranchId);
        CompanyService.AddNullable(cmd, "@u", s.GoogleReviewUrl);
        cmd.Parameters.AddWithValue("@e", s.IsEnabled);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rapor istatistiği: tarih aralığındaki (CreatedAt) geri bildirimler + adet + ortalama puan.
    /// branchId null → tüm şubeler. Soft-delete edilmiş kayıtlar hariç. En yeni önce.</summary>
    public FeedbackStats GetStats(int? branchId, DateTime fromUtc, DateTime toUtc)
    {
        var stats = new FeedbackStats();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT Id, BranchId, OrderId, CustomerPhone, Rating, Comment, GoogleRedirected, CreatedAt
                            FROM dbo.Feedback
                            WHERE IsDeleted=0 AND CreatedAt >= @f AND CreatedAt < @t
                              AND (@b IS NULL OR BranchId=@b)
                            ORDER BY CreatedAt DESC;";
        cmd.Parameters.AddWithValue("@f", fromUtc);
        cmd.Parameters.AddWithValue("@t", toUtc);
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            stats.Items.Add(new Feedback
            {
                Id = r.GetInt32(0),
                BranchId = r.GetInt32(1),
                OrderId = r.IsDBNull(2) ? null : r.GetInt32(2),
                CustomerPhone = r.IsDBNull(3) ? null : r.GetString(3),
                Rating = r.GetInt32(4),
                Comment = r.IsDBNull(5) ? null : r.GetString(5),
                GoogleRedirected = r.GetBoolean(6),
                CreatedAt = r.GetDateTime(7)
            });
        }
        stats.Count = stats.Items.Count;
        stats.Average = stats.Count == 0 ? 0 : stats.Items.Average(x => x.Rating);
        return stats;
    }

    /// <summary>Adisyon kapanınca/ödeme sonrası OPSİYONEL SMS daveti — hazır anket linkini müşteriye yollar.
    /// Best-effort: NotificationService fail-closed'dır (kanal kapalıysa gerçek gönderim yok, simülasyon/hata döner);
    /// bu metot hatayı YUTAR ve akışı bozmaz (anket daveti çekirdek satış akışına bağımlı olmamalı).</summary>
    public NotifyResult TrySendInvite(int branchId, string phone, string surveyUrl, int? userId = null)
    {
        try
        {
            var body = $"Bizi tercih ettiğiniz için teşekkürler! Deneyiminizi değerlendirin: {surveyUrl}";
            return new NotificationService().Send(
                NotificationChannel.Sms, phone, templateKey: null, body: body,
                model: null, branchId: branchId, subject: null, userId: userId);
        }
        catch (Exception ex)
        {
            return NotifyResult.Fail("SMS", ex.Message);
        }
    }
}
