using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Çek/Senet portföyü: ekleme (cari entegre), durum değişimi (karşılıksız → cari ters), listeleme.</summary>
public class CheckNoteService
{
    /// <summary>Yeni çek/senet. Alınan → cari alacak; Verilen → cari borç (aynı tx).</summary>
    public int Add(CheckNote n, int? userId)
    {
        if (n.CustomerId == 0) throw new ArgumentException("Cari seçiniz.");
        if (n.Amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (n.DueDate < n.IssueDate) throw new ArgumentException("Vade, düzenleme tarihinden önce olamaz.");

        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            bool alinan = n.Direction == CheckDirection.Alinan;
            int cariId = CariHareketService.PostTx(c, tx, new CariHareket
            {
                BranchId = n.BranchId, CustomerId = n.CustomerId, TxnDate = DateTime.Now,
                DocType = n.Kind == CheckNoteKind.Senet ? CariDocType.Senet : CariDocType.Cek,
                DocRefType = "CekSenet", DocNo = n.Serial,
                Description = $"{n.DirectionText} {n.KindText}" + (n.Serial is null ? "" : $" · {n.Serial}"),
                CurrencyId = n.CurrencyId,
                Debt = alinan ? 0 : n.Amount,     // verilen → cari borç
                Credit = alinan ? n.Amount : 0    // alınan → cari alacak
            }, userId);

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO dbo.CheckNotes (BranchId, Kind, Direction, CustomerId, Serial, BankName, Amount, CurrencyId, IssueDate, DueDate, Status, Note, CariHareketId, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@b,@k,@d,@cust,@ser,@bank,@amt,@cur,@iss,@due,@st,@note,@cari,@by);";
            cmd.Parameters.AddWithValue("@b", n.BranchId);
            cmd.Parameters.AddWithValue("@k", (int)n.Kind);
            cmd.Parameters.AddWithValue("@d", (int)n.Direction);
            cmd.Parameters.AddWithValue("@cust", n.CustomerId);
            cmd.Parameters.AddWithValue("@ser", (object?)n.Serial ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bank", (object?)n.BankName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@amt", n.Amount);
            cmd.Parameters.AddWithValue("@cur", n.CurrencyId);
            cmd.Parameters.AddWithValue("@iss", n.IssueDate.Date);
            cmd.Parameters.AddWithValue("@due", n.DueDate.Date);
            cmd.Parameters.AddWithValue("@st", (int)n.Status);
            cmd.Parameters.AddWithValue("@note", (object?)n.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cari", cariId);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            int id = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return id;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Durum değiştir. Karşılıksız'a geçişte cari hareket ters çevrilir.</summary>
    public void ChangeStatus(int id, CheckStatus status, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            CheckNote? n = ReadOne(c, tx, id);
            if (n is null) throw new InvalidOperationException("Kayıt bulunamadı.");

            if (status == CheckStatus.Karsiliksiz && n.Status != CheckStatus.Karsiliksiz)
            {
                // Daha önce bu çek/senet için ters kayıt atılmış mı? (Karşılıksız→Tahsil→Karşılıksız çift-ters engeli)
                bool alreadyReversed;
                using (var chk = c.CreateCommand())
                {
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT COUNT(1) FROM dbo.CariHareketler WHERE DocRefType='CekSenet-İade' AND DocRefId=@id;";
                    chk.Parameters.AddWithValue("@id", id);
                    alreadyReversed = (int)chk.ExecuteScalar()! > 0;
                }
                if (!alreadyReversed)
                {
                    // alınan çek alacak yazmıştı → ters: borç; verilen çek borç → ters: alacak
                    bool alinan = n.Direction == CheckDirection.Alinan;
                    CariHareketService.PostTx(c, tx, new CariHareket
                    {
                        BranchId = n.BranchId, CustomerId = n.CustomerId, TxnDate = DateTime.Now,
                        DocType = n.Kind == CheckNoteKind.Senet ? CariDocType.Senet : CariDocType.Cek,
                        DocRefType = "CekSenet-İade", DocRefId = id, DocNo = n.Serial,
                        Description = $"Karşılıksız {n.KindText}" + (n.Serial is null ? "" : $" · {n.Serial}"),
                        CurrencyId = n.CurrencyId,
                        Debt = alinan ? n.Amount : 0,
                        Credit = alinan ? 0 : n.Amount
                    }, userId);
                }
            }

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE dbo.CheckNotes SET Status=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@s", (int)status);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Çeki/senedi bankaya verir: Alınan → banka GİRİŞ + Tahsil; Verilen → banka ÇIKIŞ + Ödendi.
    /// Cari hareketi EKLEMEDE işlendiği için burada tekrar cari yazılmaz (yalnız para banka hesabına taşınır).</summary>
    public void CollectToBank(int id, int bankAccountId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var n = ReadOne(c, tx, id);
            if (n is null) throw new InvalidOperationException("Kayıt bulunamadı.");
            if (n.Status is CheckStatus.Tahsil or CheckStatus.Odendi or CheckStatus.Karsiliksiz or CheckStatus.Ciro)
                throw new InvalidOperationException("Bu çek/senet zaten kapanmış (tahsil/ödendi/ciro/karşılıksız).");

            int bankCur;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "SELECT CurrencyId FROM dbo.BankAccounts WHERE Id=@a;";
                q.Parameters.AddWithValue("@a", bankAccountId);
                var o = q.ExecuteScalar();
                if (o is null) throw new InvalidOperationException("Banka hesabı bulunamadı.");
                bankCur = Convert.ToInt32(o);
            }

            bool alinan = n.Direction == CheckDirection.Alinan;
            BankMovementService.Post(c, tx, n.BranchId, bankAccountId, bankCur,
                alinan ? BankMovementType.Giris : BankMovementType.Cikis, n.Amount, null, null,
                $"{n.DirectionText} {n.KindText} bankaya {(alinan ? "tahsil" : "ödeme")}" + (n.Serial is null ? "" : $" · {n.Serial}"), userId);

            using (var up = c.CreateCommand())
            {
                up.Transaction = tx;
                up.CommandText = "UPDATE dbo.CheckNotes SET Status=@s, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                up.Parameters.AddWithValue("@s", (int)(alinan ? CheckStatus.Tahsil : CheckStatus.Odendi));
                up.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                up.Parameters.AddWithValue("@id", id);
                up.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private static CheckNote? ReadOne(Microsoft.Data.SqlClient.SqlConnection c, Microsoft.Data.SqlClient.SqlTransaction tx, int id)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id, BranchId, Kind, Direction, CustomerId, Serial, Amount, CurrencyId, Status FROM dbo.CheckNotes WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new CheckNote
        {
            Id = r.GetInt32(0), BranchId = r.GetInt32(1), Kind = (CheckNoteKind)r.GetInt32(2),
            Direction = (CheckDirection)r.GetInt32(3), CustomerId = r.GetInt32(4),
            Serial = r.IsDBNull(5) ? null : r.GetString(5), Amount = r.GetDecimal(6),
            CurrencyId = r.GetInt32(7), Status = (CheckStatus)r.GetInt32(8)
        };
    }

    /// <summary>Tek çek/senet (silinmemiş) — cari adıyla. Web düzenleme ekranı için additive.</summary>
    public CheckNote? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT n.Id, n.BranchId, n.Kind, n.Direction, n.CustomerId, n.Serial, n.BankName, n.Amount, n.CurrencyId,
       n.IssueDate, n.DueDate, n.Status, n.Note, n.CariHareketId, cu.Name
FROM dbo.CheckNotes n
JOIN dbo.Customers cu ON cu.Id=n.CustomerId
WHERE n.Id=@id AND n.IsDeleted=0;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new CheckNote
        {
            Id = r.GetInt32(0), BranchId = r.GetInt32(1), Kind = (CheckNoteKind)r.GetInt32(2),
            Direction = (CheckDirection)r.GetInt32(3), CustomerId = r.GetInt32(4),
            Serial = r.IsDBNull(5) ? null : r.GetString(5), BankName = r.IsDBNull(6) ? null : r.GetString(6),
            Amount = r.GetDecimal(7), CurrencyId = r.GetInt32(8),
            IssueDate = r.GetDateTime(9), DueDate = r.GetDateTime(10), Status = (CheckStatus)r.GetInt32(11),
            Note = r.IsDBNull(12) ? null : r.GetString(12),
            CariHareketId = r.IsDBNull(13) ? null : r.GetInt32(13),
            CustomerName = r.GetString(14)
        };
    }

    public List<CheckNote> GetAll(CheckDirection? direction = null)
    {
        var list = new List<CheckNote>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT n.Id, n.BranchId, n.Kind, n.Direction, n.CustomerId, n.Serial, n.BankName, n.Amount, n.CurrencyId,
       n.IssueDate, n.DueDate, n.Status, n.Note, n.CariHareketId, cu.Name
FROM dbo.CheckNotes n
JOIN dbo.Customers cu ON cu.Id=n.CustomerId
WHERE (@dir IS NULL OR n.Direction=@dir) AND n.IsDeleted=0
ORDER BY n.DueDate, n.Id;";
        cmd.Parameters.AddWithValue("@dir", (object?)(direction is null ? null : (int)direction) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CheckNote
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), Kind = (CheckNoteKind)r.GetInt32(2),
                Direction = (CheckDirection)r.GetInt32(3), CustomerId = r.GetInt32(4),
                Serial = r.IsDBNull(5) ? null : r.GetString(5), BankName = r.IsDBNull(6) ? null : r.GetString(6),
                Amount = r.GetDecimal(7), CurrencyId = r.GetInt32(8),
                IssueDate = r.GetDateTime(9), DueDate = r.GetDateTime(10), Status = (CheckStatus)r.GetInt32(11),
                Note = r.IsDBNull(12) ? null : r.GetString(12),
                CariHareketId = r.IsDBNull(13) ? null : r.GetInt32(13),
                CustomerName = r.GetString(14)
            });
        return list;
    }
}
