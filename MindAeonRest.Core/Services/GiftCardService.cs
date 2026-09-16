using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Hediye çeki tam program: satış (yükleme), bakiye sorgu, ödeme (kullanım), iptal, ekstre.</summary>
public class GiftCardService
{
    private static string SelectBase => $@"
SELECT g.Id, g.Code, g.BranchId, g.InitialAmount, g.Balance, g.CurrencyId, cur.Code, g.Status, g.CustomerId, cu.Name, g.IssuedUtc, g.ExpiresUtc, g.Note
FROM dbo.GiftCards g
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id=g.CurrencyId
LEFT JOIN dbo.Customers cu ON cu.Id=g.CustomerId";

    private static GiftCard Read(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Code = r.GetString(1), BranchId = r.GetInt32(2),
        InitialAmount = r.GetDecimal(3), Balance = r.GetDecimal(4), CurrencyId = r.GetInt32(5),
        CurrencyCode = r.IsDBNull(6) ? null : r.GetString(6), Status = (GiftCardStatus)r.GetInt32(7),
        CustomerId = r.IsDBNull(8) ? null : r.GetInt32(8), CustomerName = r.IsDBNull(9) ? null : r.GetString(9),
        IssuedUtc = r.GetDateTime(10), ExpiresUtc = r.IsDBNull(11) ? null : r.GetDateTime(11),
        Note = r.IsDBNull(12) ? null : r.GetString(12)
    };

    public List<GiftCard> GetAll(bool includeInactive = true)
    {
        var list = new List<GiftCard>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + (includeInactive ? "" : " WHERE g.Status=0") + " ORDER BY g.Id DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public GiftCard? GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE g.Code=@c;";
        cmd.Parameters.AddWithValue("@c", code.Trim());
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public GiftCard? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE g.Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>Yeni hediye çeki üret + bakiye yükle + SATIŞ BEDELİNİ kasaya/POS'a tahsil olarak yaz (parasal açık olmasın).
    /// saleKind: Nakit→kasa, kart-türevi→POS; saleInstrumentId zorunlu. Kod verilmezse otomatik üretilir.</summary>
    public GiftCard Issue(decimal amount, int? currencyId, int? customerId, DateTime? expiresUtc, string? code, string? note,
        PaymentKind saleKind, int? saleInstrumentId, int? userId)
    {
        if (amount <= 0) throw new ArgumentException("Hediye çeki tutarı 0'dan büyük olmalı.");
        bool cash = saleKind == PaymentKind.Nakit;
        bool card = saleKind.IsCardType();
        if (!cash && !card) throw new ArgumentException("Hediye çeki satış bedeli yalnız Nakit veya Kart ile tahsil edilebilir.");
        if (saleInstrumentId is not int instr) throw new ArgumentException(cash ? "Satış için kasa seçin." : "Satış için POS cihazı seçin.");
        int branch = Session.CurrentBranch?.Id ?? 0;
        int cur = currencyId is int ci && ci > 0 ? ci : new CurrencyService().GetDefaultId() ?? 1;
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            string finalCode = string.IsNullOrWhiteSpace(code) ? DocCounter.NextCode(c, tx, branch, "HC") : code.Trim().ToUpperInvariant();
            int id;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.GiftCards (Code, BranchId, InitialAmount, Balance, CurrencyId, Status, CustomerId, IssuedUtc, ExpiresUtc, Note, CreatedBy)
OUTPUT INSERTED.Id VALUES (@code,@b,@amt,@amt,@cur,0,@cust,SYSUTCDATETIME(),@exp,@note,@by);";
                cmd.Parameters.AddWithValue("@code", finalCode);
                cmd.Parameters.AddWithValue("@b", branch);
                cmd.Parameters.AddWithValue("@amt", amount);
                cmd.Parameters.AddWithValue("@cur", cur);
                cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@exp", (object?)expiresUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                id = (int)cmd.ExecuteScalar()!;
            }
            PostMovement(c, tx, id, branch, GiftCardMovementType.Yukleme, amount, null, null, "Hediye çeki satışı/yükleme", userId);
            // SATIŞ BEDELİ tahsilatı (para girişi) — yükümlülüğün karşılığı:
            if (cash) CashMovementService.Post(c, tx, branch, instr, cur, CashMovementType.Tahsilat, amount, null, null, $"Hediye çeki satışı {finalCode}", userId);
            else      PosMovementService.Post(c, tx, branch, instr, cur, BankMovementType.Tahsilat, amount, customerId, null, $"Hediye çeki satışı {finalCode}", userId);
            tx.Commit();
            return GetById(id)!;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Dolaşımdaki toplam hediye çeki yükümlülüğü (aktif çeklerin bakiye toplamı).</summary>
    public decimal GetTotalLiability()
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(SUM(Balance),0) FROM dbo.GiftCards WHERE Status=0;";
        var v = cmd.ExecuteScalar();
        return v is decimal d ? d : 0m;
    }

    /// <summary>Adisyon ödemesi içinde (aynı tx) hediye çeki bakiyesinden düşer. Bakiye yetersizse hata (fail-closed).
    /// paymentCurrencyId: adisyon ödeme dövizi; çek dövizinden farklıysa hata (döviz karışmasın).</summary>
    internal static void RedeemTx(SqlConnection c, SqlTransaction tx, int giftCardId, decimal amount, int paymentCurrencyId, int? orderId, int? clFicheId, int? userId)
    {
        if (amount <= 0) throw new InvalidOperationException("Geçersiz hediye çeki tutarı.");
        int branch; decimal balance; int status; int giftCardCurrencyId;
        using (var sel = c.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT BranchId, Balance, Status, CurrencyId FROM dbo.GiftCards WITH (UPDLOCK, ROWLOCK) WHERE Id=@id;";
            sel.Parameters.AddWithValue("@id", giftCardId);
            using var r = sel.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("Hediye çeki bulunamadı.");
            branch = r.GetInt32(0); balance = r.GetDecimal(1); status = r.GetInt32(2); giftCardCurrencyId = r.GetInt32(3);
        }
        if (status != (int)GiftCardStatus.Aktif) throw new InvalidOperationException("Hediye çeki aktif değil.");
        if (paymentCurrencyId != giftCardCurrencyId) throw new InvalidOperationException("Hediye çeki dövizi adisyon dövizinden farklı.");
        if (Math.Round(balance, 2) < Math.Round(amount, 2)) throw new InvalidOperationException($"Hediye çeki bakiyesi yetersiz. Bakiye {balance:N2} ₺.");
        decimal newBal = Math.Round(balance - amount, 2);
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.GiftCards SET Balance=@bal, Status=CASE WHEN @bal<=0.005 THEN 1 ELSE Status END, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            upd.Parameters.AddWithValue("@bal", newBal);
            upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id", giftCardId);
            upd.ExecuteNonQuery();
        }
        PostMovement(c, tx, giftCardId, branch, GiftCardMovementType.Kullanim, amount, orderId, clFicheId, "Adisyon hediye çeki kullanımı", userId);
    }

    /// <summary>İade içinde (aynı tx) hediye çeki bakiyesini GERİ EKLER (RedeemTx tersi):
    /// Balance += amount, Status=Aktif; Yukleme tipinde iade hareketi yazılır.</summary>
    internal static void RefundTx(SqlConnection c, SqlTransaction tx, int giftCardId, decimal amount, int? orderId, int? clFicheId, int? userId)
    {
        if (amount <= 0) throw new InvalidOperationException("Geçersiz hediye çeki iade tutarı.");
        int branch; decimal balance;
        using (var sel = c.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT BranchId, Balance FROM dbo.GiftCards WITH (UPDLOCK, ROWLOCK) WHERE Id=@id;";
            sel.Parameters.AddWithValue("@id", giftCardId);
            using var r = sel.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("Hediye çeki bulunamadı.");
            branch = r.GetInt32(0); balance = r.GetDecimal(1);
        }
        decimal newBal = Math.Round(balance + amount, 2);
        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            // Bakiye geri yüklendiği için çek yeniden Aktif olur — İPTAL (Status=2) edilmiş çek dahil:
            // aksi halde iade tutarı iptal çekine eklenir ama RedeemTx yalnız Aktif çeki kabul ettiğinden değer kullanılamaz kalır (buharlaşır).
            // amount>0 (yukarıda garanti) → Status=Aktif(0).
            upd.CommandText = "UPDATE dbo.GiftCards SET Balance=@bal, Status=0, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            upd.Parameters.AddWithValue("@bal", newBal);
            upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id", giftCardId);
            upd.ExecuteNonQuery();
        }
        PostMovement(c, tx, giftCardId, branch, GiftCardMovementType.Yukleme, amount, orderId, clFicheId, "Adisyon hediye çeki iadesi (bakiye geri yükleme)", userId);
    }

    public void Cancel(int giftCardId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            int branch; decimal balance; int status;
            using (var sel = c.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT BranchId, Balance, Status FROM dbo.GiftCards WITH (UPDLOCK, ROWLOCK) WHERE Id=@id;";
                sel.Parameters.AddWithValue("@id", giftCardId);
                using var r = sel.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException("Hediye çeki bulunamadı.");
                branch = r.GetInt32(0); balance = r.GetDecimal(1); status = r.GetInt32(2);
            }
            if (status != (int)GiftCardStatus.Aktif) throw new InvalidOperationException("Yalnız aktif hediye çeki iptal edilebilir.");
            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE dbo.GiftCards SET Balance=0, Status=2, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                upd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                upd.Parameters.AddWithValue("@id", giftCardId);
                upd.ExecuteNonQuery();
            }
            if (balance > 0) PostMovement(c, tx, giftCardId, branch, GiftCardMovementType.Iptal, balance, null, null, "Hediye çeki iptal (kalan bakiye)", userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public List<GiftCardMovement> GetMovements(int giftCardId, int top = 200)
    {
        var list = new List<GiftCardMovement>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (@n) Id, GiftCardId, BranchId, Type, Amount, OrderId, RefClFicheId, Note, MoveDate
FROM dbo.GiftCardMovements WHERE GiftCardId=@g ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@n", top);
        cmd.Parameters.AddWithValue("@g", giftCardId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new GiftCardMovement
            {
                Id = r.GetInt32(0), GiftCardId = r.GetInt32(1), BranchId = r.GetInt32(2),
                Type = (GiftCardMovementType)r.GetInt32(3), Amount = r.GetDecimal(4),
                OrderId = r.IsDBNull(5) ? null : r.GetInt32(5), RefClFicheId = r.IsDBNull(6) ? null : r.GetInt32(6),
                Note = r.IsDBNull(7) ? null : r.GetString(7), MoveDate = r.GetDateTime(8)
            });
        return list;
    }

    private static void PostMovement(SqlConnection c, SqlTransaction tx, int giftCardId, int branchId,
        GiftCardMovementType type, decimal amount, int? orderId, int? clFicheId, string? note, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.GiftCardMovements (GiftCardId, BranchId, Type, Amount, OrderId, RefClFicheId, Note, MoveDate, CreatedBy)
VALUES (@g,@b,@t,@a,@o,@ref,@note,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@g", giftCardId);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@a", Math.Abs(amount));
        cmd.Parameters.AddWithValue("@o", (object?)orderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ref", (object?)clFicheId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
