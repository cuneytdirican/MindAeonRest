using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>KAMPANYA OTOMASYONU (Faz 5b): hedef segment seç → NotificationService ile toplu SMS/e-posta/WhatsApp gönder →
/// dbo.CampaignSendLog (kendi tablosu). Segment sorguları (tümü/grup/doğum günü/geri kazanım) burada; kanal→adres ve
/// geri-kazanım eşiği SAF <see cref="CampaignSendLogic"/> içindedir. EF YOK (ADO.NET). FAIL-CLOSED: gönderim
/// NotificationService'e (TEST modu/kapalı kanal → simülasyon/başarısız) devredilir; log best-effort.</summary>
public class CampaignSendService
{
    private readonly NotificationService _notify = new();

    /// <summary>Segmenti çözüp alıcı listesini döndürür (önizleme + gönderim ortak kaynağı; istemciye GÜVENİLMEZ).
    /// <paramref name="now"/> tr-TR yerel zaman (doğum günü gün/ay eşleşmesi + geri kazanım eşiği için).</summary>
    public List<CampaignSendRecipient> GetRecipients(CampaignSegment segment, NotificationChannel channel,
        int? groupId, int recoveryDays, DateTime now)
    {
        var list = new List<CampaignSendRecipient>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();

        // Ortak: aktif, silinmemiş, sabit perakende kartı (PA) hariç.
        var sql = @"SELECT c.Id, c.Name, c.Phone, c.Email
                    FROM dbo.Customers c
                    WHERE c.IsDeleted = 0 AND c.IsActive = 1 AND c.Code <> N'PA'";

        switch (segment)
        {
            case CampaignSegment.MusteriGrubu:
                sql += " AND c.CustomerGroupId = @grp";
                cmd.Parameters.AddWithValue("@grp", (object?)groupId ?? DBNull.Value);
                break;
            case CampaignSegment.DogumGunu:
                // Bugünün gün+ay'ı (yıldan bağımsız). BirthDate boşsa hedef değildir.
                sql += " AND c.BirthDate IS NOT NULL AND MONTH(c.BirthDate) = @m AND DAY(c.BirthDate) = @d";
                cmd.Parameters.AddWithValue("@m", now.Month);
                cmd.Parameters.AddWithValue("@d", now.Day);
                break;
            case CampaignSegment.GeriKazanim:
                // Siparişi OLAN ama en son siparişi eşik günden ESKİ olan müşteriler (hiç sipariş vermeyen dahil değil).
                sql += @" AND c.Id IN (
                            SELECT o.CustomerId FROM dbo.Orders o
                            WHERE o.IsDeleted = 0
                            GROUP BY o.CustomerId
                            HAVING MAX(o.OpenedAt) < DATEADD(DAY, -@days, @now))";
                cmd.Parameters.AddWithValue("@days", recoveryDays > 0 ? recoveryDays : 30);
                cmd.Parameters.AddWithValue("@now", now);
                break;
            case CampaignSegment.Tumu:
            default:
                break;
        }
        sql += " ORDER BY c.Name;";
        cmd.CommandText = sql;

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string? phone = r.IsDBNull(2) ? null : r.GetString(2);
            string? email = r.IsDBNull(3) ? null : r.GetString(3);
            // Kanala uygun adres yoksa alıcı listesine GİRMEZ (saf mantık — boş adrese gönderim denenmez).
            var contact = CampaignSendLogic.ContactFor(channel, phone, email);
            if (contact is null) continue;
            list.Add(new CampaignSendRecipient { CustomerId = r.GetInt32(0), Name = r.GetString(1), Contact = contact });
        }
        return list;
    }

    /// <summary>Alıcı listesine toplu gönderim yapar (her biri NotificationService üzerinden) ve CampaignSendLog'a yazar.
    /// Kampanya seçiliyse {kod}/{tutar} yer-tutucuları o kampanyadan doldurulur.</summary>
    public CampaignSendResult SendBulk(IReadOnlyList<CampaignSendRecipient> recipients, NotificationChannel channel,
        CampaignSegment segment, int? campaignId, string? templateKey, string? body, int branchId, int? userId)
    {
        // Kampanya bilgisi (yer-tutucu modeli için) — tek sefer çöz.
        Campaign? camp = campaignId is > 0 ? new CampaignService().GetById(campaignId.Value) : null;

        var result = new CampaignSendResult { Total = recipients.Count };
        foreach (var rcp in recipients)
        {
            var model = CampaignSendLogic.BuildModel(rcp.Name, camp?.Code, camp?.ValueText);
            NotifyResult res;
            try
            {
                res = _notify.Send(channel, rcp.Contact, templateKey, body, model, branchId,
                    subject: camp?.Name ?? "Kampanya", userId: userId);
            }
            catch (Exception ex)
            {
                res = NotifyResult.Fail(channel.ToTr(), ex.Message);
            }

            switch (res.Status)
            {
                case NotificationStatus.Gonderildi: result.Sent++; break;
                case NotificationStatus.Simulasyon: result.Simulated++; break;
                default: result.Failed++; break;
            }
            result.Items.Add((rcp.Contact, res.Status, res.Error ?? res.ProviderResponse));
            TryLog(campaignId, branchId, segment, channel, rcp.CustomerId, rcp.Contact, res);
        }
        return result;
    }

    /// <summary>Son gönderim kayıtları (en yeni önce).</summary>
    public List<CampaignSendLogRow> GetRecentLog(int take = 100)
    {
        var list = new List<CampaignSendLogRow>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT TOP (@n) Id, CampaignId, BranchId, Segment, Channel, CustomerId, Recipient, Status, Error, SentAt
                            FROM dbo.CampaignSendLog ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@n", take);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CampaignSendLogRow
            {
                Id = r.GetInt32(0),
                CampaignId = r.IsDBNull(1) ? null : r.GetInt32(1),
                BranchId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Segment = (CampaignSegment)r.GetInt32(3),
                Channel = (NotificationChannel)r.GetInt32(4),
                CustomerId = r.IsDBNull(5) ? null : r.GetInt32(5),
                Recipient = r.IsDBNull(6) ? null : r.GetString(6),
                Status = (NotificationStatus)r.GetInt32(7),
                Error = r.IsDBNull(8) ? null : r.GetString(8),
                SentAt = r.GetDateTime(9)
            });
        return list;
    }

    /// <summary>CampaignSendLog kaydı (best-effort; hata yut → gönderim sonucunu etkilemez).</summary>
    private static void TryLog(int? campaignId, int branchId, CampaignSegment segment, NotificationChannel channel,
        int customerId, string recipient, NotifyResult res)
    {
        try
        {
            using var c = Database.OpenCompany();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO dbo.CampaignSendLog
                (CampaignId, BranchId, Segment, Channel, CustomerId, Recipient, Status, Error)
                VALUES (@camp, @br, @seg, @ch, @cust, @to, @st, @err);";
            cmd.Parameters.AddWithValue("@camp", (object?)campaignId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@br", branchId);
            cmd.Parameters.AddWithValue("@seg", (int)segment);
            cmd.Parameters.AddWithValue("@ch", (int)channel);
            cmd.Parameters.AddWithValue("@cust", customerId);
            CompanyService.AddNullable(cmd, "@to", recipient);
            cmd.Parameters.AddWithValue("@st", (int)res.Status);
            CompanyService.AddNullable(cmd, "@err", res.Error);
            cmd.ExecuteNonQuery();
        }
        catch { /* log yazılamadı → gönderim akışını bozma */ }
    }
}
