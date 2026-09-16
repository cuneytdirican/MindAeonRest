using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Puan/sadakat tam program: kazanım (ödemede), harcama (ödeme yöntemi), bakiye, ekstre, oran ayarları.</summary>
public class LoyaltyService
{
    public LoyaltySettings GetSettings()
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT IsActive, EarnRate, RedeemRate FROM dbo.LoyaltySettings WHERE Id=1;";
        using var r = cmd.ExecuteReader();
        if (r.Read()) return new LoyaltySettings { IsActive = r.GetBoolean(0), EarnRate = r.GetDecimal(1), RedeemRate = r.GetDecimal(2) };
        return new LoyaltySettings { IsActive = false, EarnRate = 0, RedeemRate = 1 };
    }

    public void SaveSettings(LoyaltySettings s, int? userId)
    {
        if (s.EarnRate < 0) throw new ArgumentException("Kazanım oranı negatif olamaz.");
        if (s.RedeemRate <= 0) throw new ArgumentException("Harcama oranı (1 puanın ₺ karşılığı) 0'dan büyük olmalı.");
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
MERGE dbo.LoyaltySettings AS t USING (SELECT 1 AS Id) AS s ON t.Id=s.Id
WHEN MATCHED THEN UPDATE SET IsActive=@act, EarnRate=@earn, RedeemRate=@redeem, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (Id, IsActive, EarnRate, RedeemRate, CreatedBy) VALUES (1,@act,@earn,@redeem,@by);";
        cmd.Parameters.AddWithValue("@act", s.IsActive);
        cmd.Parameters.AddWithValue("@earn", s.EarnRate);
        cmd.Parameters.AddWithValue("@redeem", s.RedeemRate);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Toplam puan yükümlülüğü (₺): Σ(müşteri puanı) × RedeemRate.</summary>
    public decimal GetTotalLiability()
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(SUM(Points),0) FROM dbo.LoyaltyAccounts;";
        decimal points = cmd.ExecuteScalar() is decimal d ? d : 0m;
        var s = GetSettings();
        return Math.Round(points * (s.RedeemRate <= 0 ? 1 : s.RedeemRate), 2);
    }

    public decimal GetBalance(int customerId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(Points,0) FROM dbo.LoyaltyAccounts WHERE CustomerId=@c;";
        cmd.Parameters.AddWithValue("@c", customerId);
        var v = cmd.ExecuteScalar();
        return v is decimal d ? d : 0m;
    }

    private static decimal GetSettingsTx(SqlConnection c, SqlTransaction tx, out decimal earn, out decimal redeem)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IsActive, EarnRate, RedeemRate FROM dbo.LoyaltySettings WHERE Id=1;";
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            bool act = r.GetBoolean(0); earn = r.GetDecimal(1); redeem = r.GetDecimal(2);
            return act ? 1 : 0;
        }
        earn = 0; redeem = 1; return 0;
    }

    private static void EnsureAccount(SqlConnection c, SqlTransaction tx, int customerId, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "IF NOT EXISTS (SELECT 1 FROM dbo.LoyaltyAccounts WHERE CustomerId=@c) INSERT INTO dbo.LoyaltyAccounts (CustomerId, Points, CreatedBy) VALUES (@c, 0, @by);";
        cmd.Parameters.AddWithValue("@c", customerId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adisyon ödemesi içinde (aynı tx) puan harcaması — bakiye yetersizse hata (fail-closed).</summary>
    internal static void RedeemTx(SqlConnection c, SqlTransaction tx, int customerId, decimal amount, int? orderId, int? clFicheId, int? userId)
    {
        if (amount <= 0) throw new InvalidOperationException("Geçersiz puan harcama tutarı.");
        decimal active = GetSettingsTx(c, tx, out _, out decimal redeem);
        if (active <= 0) throw new InvalidOperationException("Sadakat programı aktif değil.");
        if (redeem <= 0) redeem = 1;
        decimal pointsNeeded = Math.Round(amount / redeem, 2);
        EnsureAccount(c, tx, customerId, userId);
        decimal balance;
        using (var sel = c.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT Points FROM dbo.LoyaltyAccounts WITH (UPDLOCK, ROWLOCK) WHERE CustomerId=@c;";
            sel.Parameters.AddWithValue("@c", customerId);
            balance = (decimal)sel.ExecuteScalar()!;
        }
        if (Math.Round(balance, 2) < Math.Round(pointsNeeded, 2)) throw new InvalidOperationException($"Puan bakiyesi yetersiz. Bakiye {balance:N0} puan, gerekli {pointsNeeded:N0} puan.");
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.LoyaltyAccounts SET Points=Points-@p, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE CustomerId=@c;";
            upd.Parameters.AddWithValue("@p", pointsNeeded);
            upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@c", customerId);
            upd.ExecuteNonQuery();
        }
        PostMovement(c, tx, customerId, LoyaltyMovementType.Harcama, pointsNeeded, amount, orderId, clFicheId, "Adisyon puan harcaması", userId, redeem);
    }

    /// <summary>İade içinde (aynı tx) puan harcamasını GERİ EKLER (RedeemTx tersi): harcanan puan müşteriye iade edilir.
    /// İade ₺ tutarı, harcamadaki RedeemRate ile puana çevrilir ve hesaba geri yüklenir.</summary>
    internal static void RefundTx(SqlConnection c, SqlTransaction tx, int customerId, decimal amount, int? orderId, int? clFicheId, int? userId)
    {
        if (amount <= 0) throw new InvalidOperationException("Geçersiz puan iade tutarı.");
        GetSettingsTx(c, tx, out _, out decimal redeem);
        if (redeem <= 0) redeem = 1;
        // Harcama anındaki oranı kullan (oran o zamandan beri değişmiş olabilir); ilgili Harcama hareketini orderId/clFicheId ile bul.
        // Kayıtta oran yoksa (eski hareketler NULL) güncel orana düş → geriye uyumlu.
        decimal rateUsed = redeem;
        using (var sel = c.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = @"
SELECT TOP 1 RateUsed FROM dbo.LoyaltyMovements
WHERE CustomerId=@c AND Type=@t AND RateUsed IS NOT NULL
  AND ((@o IS NOT NULL AND OrderId=@o) OR (@ref IS NOT NULL AND RefClFicheId=@ref))
ORDER BY Id DESC;";
            sel.Parameters.AddWithValue("@c", customerId);
            sel.Parameters.AddWithValue("@t", (int)LoyaltyMovementType.Harcama);
            sel.Parameters.AddWithValue("@o", (object?)orderId ?? DBNull.Value);
            sel.Parameters.AddWithValue("@ref", (object?)clFicheId ?? DBNull.Value);
            if (sel.ExecuteScalar() is decimal rd && rd > 0) rateUsed = rd;
        }
        decimal pointsBack = Math.Round(amount / rateUsed, 2);
        if (pointsBack <= 0) return;
        EnsureAccount(c, tx, customerId, userId);
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.LoyaltyAccounts SET Points=Points+@p, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE CustomerId=@c;";
            upd.Parameters.AddWithValue("@p", pointsBack);
            upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@c", customerId);
            upd.ExecuteNonQuery();
        }
        PostMovement(c, tx, customerId, LoyaltyMovementType.Kazanim, pointsBack, amount, orderId, clFicheId, "Adisyon puan harcaması iadesi", userId, rateUsed);
    }

    /// <summary>Adisyon kapanışında (aynı tx) tahsil edilen tutar üzerinden puan kazanımı. Program pasifse/PA ise no-op.</summary>
    internal static void EarnTx(SqlConnection c, SqlTransaction tx, int customerId, decimal collectedAmount, int? orderId, int? clFicheId, int? userId)
    {
        if (collectedAmount <= 0) return;
        decimal active = GetSettingsTx(c, tx, out decimal earn, out _);
        if (active <= 0 || earn <= 0) return;
        decimal points = Math.Round(collectedAmount * earn, 2);
        if (points <= 0) return;
        EnsureAccount(c, tx, customerId, userId);
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.LoyaltyAccounts SET Points=Points+@p, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE CustomerId=@c;";
            upd.Parameters.AddWithValue("@p", points);
            upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@c", customerId);
            upd.ExecuteNonQuery();
        }
        PostMovement(c, tx, customerId, LoyaltyMovementType.Kazanim, points, collectedAmount, orderId, clFicheId, "Adisyon puan kazanımı", userId, earn);
    }

    public List<LoyaltyMovement> GetMovements(int customerId, int top = 200)
    {
        var list = new List<LoyaltyMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@n) Id, CustomerId, Type, Points, Amount, OrderId, RefClFicheId, Note, MoveDate
FROM dbo.LoyaltyMovements WHERE CustomerId=@c ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@n", top);
        cmd.Parameters.AddWithValue("@c", customerId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LoyaltyMovement
            {
                Id = r.GetInt32(0), CustomerId = r.GetInt32(1), Type = (LoyaltyMovementType)r.GetInt32(2),
                Points = r.GetDecimal(3), Amount = r.GetDecimal(4),
                OrderId = r.IsDBNull(5) ? null : r.GetInt32(5), RefClFicheId = r.IsDBNull(6) ? null : r.GetInt32(6),
                Note = r.IsDBNull(7) ? null : r.GetString(7), MoveDate = r.GetDateTime(8)
            });
        return list;
    }

    private static void PostMovement(SqlConnection c, SqlTransaction tx, int customerId, LoyaltyMovementType type,
        decimal points, decimal amount, int? orderId, int? clFicheId, string? note, int? userId, decimal? rateUsed = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.LoyaltyMovements (CustomerId, Type, Points, Amount, OrderId, RefClFicheId, Note, MoveDate, CreatedBy, RateUsed)
VALUES (@c,@t,@p,@a,@o,@ref,@note,SYSUTCDATETIME(),@by,@rate);";
        cmd.Parameters.AddWithValue("@c", customerId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@p", points);
        cmd.Parameters.AddWithValue("@a", amount);
        cmd.Parameters.AddWithValue("@o", (object?)orderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ref", (object?)clFicheId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rate", (object?)rateUsed ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
