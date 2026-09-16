using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Evrak motoru: tek genel Documents/DocumentLines/DocumentLinks (sipariş/irsaliye/fatura/transfer/sayım/fire/gider).
/// Kaydederken atomik FişNo (tip+şube serisi) + USD/EUR kur snapshot + satır/başlık toplamları.
/// Cari/Stok'a kayıt (posting) tipe göre ilgili fazlarda yapılır.</summary>
public class DocumentService
{
    public Document Save(Document doc, List<DocumentLine> lines, int? userId)
    {
        if (doc.BranchId == 0) throw new ArgumentException("Şube seçilmeli.");
        if (lines.Count == 0) throw new ArgumentException("En az bir satır girilmeli.");
        if (doc.CurrencyId == 0) doc.CurrencyId = new CurrencyService().GetDefaultId() ?? 0;

        // kur snapshot (kayıt anındaki USD/EUR)
        if (doc.UsdRate is null || doc.EurRate is null)
        {
            try { var (usd, eur) = new CurrencyRateService().GetUsdEur(); doc.UsdRate ??= usd; doc.EurRate ??= eur; }
            catch { /* kur yoksa boş geçilir */ }
        }

        doc.Recalc(lines);

        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            if (doc.Id == 0)
                doc.FisNo = CodeSeriesService.NextDocCode(c, tx, (CodeSeriesType)(int)doc.DocType, doc.BranchId, userId);
            else
            {
                // K1: yalnızca Taslak evrak düzenlenebilir; işlenmiş/iptal evrak değiştirilemez (defterler bozulmasın)
                using var st = c.CreateCommand();
                st.Transaction = tx;
                st.CommandText = "SELECT Status FROM dbo.Documents WHERE Id=@id;";
                st.Parameters.AddWithValue("@id", doc.Id);
                var cur = st.ExecuteScalar();
                if (cur is int s && s != (int)DocStatus.Taslak)
                    throw new InvalidOperationException("İşlenmiş veya iptal edilmiş evrak düzenlenemez. Önce iptal edin.");
            }

            if (doc.Id == 0) InsertHeader(c, tx, doc, userId);
            else { UpdateHeader(c, tx, doc, userId); DeleteLines(c, tx, doc.Id); }

            int no = 1;
            foreach (var l in lines)
            {
                l.DocumentId = doc.Id;
                l.LineNo = no++;
                l.LineCurrencyId = doc.CurrencyId;   // K5: tüm satırlar evrak dövizinde (karışık döviz toplamı engellenir)
                InsertLine(c, tx, l, userId);
            }
            tx.Commit();
            return doc;
        }
        catch { tx.Rollback(); throw; }
    }

    private static void InsertHeader(SqlConnection c, SqlTransaction tx, Document d, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.Documents (DocType, FisNo, BelgeNo, DocDate, BelgeTarihi, BranchId, WarehouseId, TargetBranchId, TargetWarehouseId,
    ProjectId, CustomerId, AddressId, PersonnelId, ExpenseTypeId, CurrencyId, UsdRate, EurRate, Status, Note,
    SubTotal, DiscountTotal, TaxTotal, GrandTotal, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@type,@fis,@belge,@date,@bt,@br,@wh,@tbr,@twh,@prj,@cust,@adr,@pers,@exp,@cur,@usd,@eur,@st,@note,
    @sub,@disc,@tax,@grand,@by);";
        BindHeader(cmd, d, userId);
        d.Id = (int)cmd.ExecuteScalar()!;
    }

    private static void UpdateHeader(SqlConnection c, SqlTransaction tx, Document d, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
UPDATE dbo.Documents SET BelgeNo=@belge, DocDate=@date, BelgeTarihi=@bt, WarehouseId=@wh, TargetBranchId=@tbr, TargetWarehouseId=@twh,
    ProjectId=@prj, CustomerId=@cust, AddressId=@adr, PersonnelId=@pers, ExpenseTypeId=@exp, CurrencyId=@cur, UsdRate=@usd, EurRate=@eur,
    Status=@st, Note=@note, SubTotal=@sub, DiscountTotal=@disc, TaxTotal=@tax, GrandTotal=@grand, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id;";
        BindHeader(cmd, d, userId);
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.ExecuteNonQuery();
    }

    private static void BindHeader(SqlCommand cmd, Document d, int? userId)
    {
        cmd.Parameters.AddWithValue("@type", (int)d.DocType);
        cmd.Parameters.AddWithValue("@fis", d.FisNo);
        cmd.Parameters.AddWithValue("@belge", (object?)d.BelgeNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date", d.DocDate);
        cmd.Parameters.AddWithValue("@bt", (object?)d.BelgeTarihi ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@br", d.BranchId);
        cmd.Parameters.AddWithValue("@wh", (object?)d.WarehouseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tbr", (object?)d.TargetBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@twh", (object?)d.TargetWarehouseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prj", (object?)d.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)d.CustomerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@adr", (object?)d.AddressId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pers", (object?)d.PersonnelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exp", (object?)d.ExpenseTypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cur", d.CurrencyId);
        cmd.Parameters.AddWithValue("@usd", (object?)d.UsdRate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eur", (object?)d.EurRate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", (int)d.Status);
        cmd.Parameters.AddWithValue("@note", (object?)d.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sub", d.SubTotal);
        cmd.Parameters.AddWithValue("@disc", d.DiscountTotal);
        cmd.Parameters.AddWithValue("@tax", d.TaxTotal);
        cmd.Parameters.AddWithValue("@grand", d.GrandTotal);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
    }

    private static void DeleteLines(SqlConnection c, SqlTransaction tx, int docId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM dbo.DocumentLines WHERE DocumentId=@d;";
        cmd.Parameters.AddWithValue("@d", docId);
        cmd.ExecuteNonQuery();
    }

    private static void InsertLine(SqlConnection c, SqlTransaction tx, DocumentLine l, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO dbo.DocumentLines (DocumentId, [LineNo], ProductId, Description, Qty, UnitId, UnitPrice, LineCurrencyId,
    Disc1,Disc2,Disc3,Disc4,Disc5,Disc6,Disc7,Disc8,Disc9,Disc10, TaxGroupId, TaxRate,
    LineGross, DiscountAmount, LineNet, LineTax, LineTotal, ReceivedQty, CreatedBy)
VALUES (@doc,@no,@p,@desc,@qty,@unit,@price,@cur,
    @d1,@d2,@d3,@d4,@d5,@d6,@d7,@d8,@d9,@d10,@tg,@tr,
    @gross,@disc,@net,@tax,@total,@recv,@by);";
        cmd.Parameters.AddWithValue("@doc", l.DocumentId);
        cmd.Parameters.AddWithValue("@no", l.LineNo);
        cmd.Parameters.AddWithValue("@p", l.ProductId);
        cmd.Parameters.AddWithValue("@desc", (object?)l.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qty", l.Qty);
        cmd.Parameters.AddWithValue("@unit", (object?)l.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@price", l.UnitPrice);
        cmd.Parameters.AddWithValue("@cur", l.LineCurrencyId);
        cmd.Parameters.AddWithValue("@d1", l.Disc1); cmd.Parameters.AddWithValue("@d2", l.Disc2);
        cmd.Parameters.AddWithValue("@d3", l.Disc3); cmd.Parameters.AddWithValue("@d4", l.Disc4);
        cmd.Parameters.AddWithValue("@d5", l.Disc5); cmd.Parameters.AddWithValue("@d6", l.Disc6);
        cmd.Parameters.AddWithValue("@d7", l.Disc7); cmd.Parameters.AddWithValue("@d8", l.Disc8);
        cmd.Parameters.AddWithValue("@d9", l.Disc9); cmd.Parameters.AddWithValue("@d10", l.Disc10);
        cmd.Parameters.AddWithValue("@tg", (object?)l.TaxGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tr", l.TaxRate);
        cmd.Parameters.AddWithValue("@gross", l.LineGross);
        cmd.Parameters.AddWithValue("@disc", l.DiscountAmount);
        cmd.Parameters.AddWithValue("@net", l.LineNet);
        cmd.Parameters.AddWithValue("@tax", l.LineTax);
        cmd.Parameters.AddWithValue("@total", l.LineTotal);
        cmd.Parameters.AddWithValue("@recv", l.ReceivedQty);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public Document? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT {HeaderCols}, cu.Name, p.Code
FROM dbo.Documents d
LEFT JOIN dbo.Customers cu ON cu.Id=d.CustomerId
LEFT JOIN dbo.Projects p ON p.Id=d.ProjectId
WHERE d.Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapHeader(r) : null;
    }

    public List<DocumentLine> GetLines(int documentId)
    {
        var list = new List<DocumentLine>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT l.Id, l.DocumentId, l.[LineNo], l.ProductId, l.Description, l.Qty, l.UnitId, l.UnitPrice, l.LineCurrencyId,
    l.Disc1,l.Disc2,l.Disc3,l.Disc4,l.Disc5,l.Disc6,l.Disc7,l.Disc8,l.Disc9,l.Disc10, l.TaxGroupId, l.TaxRate,
    l.LineGross, l.DiscountAmount, l.LineNet, l.LineTax, l.LineTotal, l.ReceivedQty, pr.Name, pr.Code
FROM dbo.DocumentLines l
LEFT JOIN dbo.Products pr ON pr.Id=l.ProductId
WHERE l.DocumentId=@d ORDER BY l.[LineNo];";
        cmd.Parameters.AddWithValue("@d", documentId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DocumentLine
            {
                Id = r.GetInt32(0), DocumentId = r.GetInt32(1), LineNo = r.GetInt32(2), ProductId = r.GetInt32(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4), Qty = r.GetDecimal(5),
                UnitId = r.IsDBNull(6) ? null : r.GetInt32(6), UnitPrice = r.GetDecimal(7), LineCurrencyId = r.GetInt32(8),
                Disc1 = r.GetDecimal(9), Disc2 = r.GetDecimal(10), Disc3 = r.GetDecimal(11), Disc4 = r.GetDecimal(12),
                Disc5 = r.GetDecimal(13), Disc6 = r.GetDecimal(14), Disc7 = r.GetDecimal(15), Disc8 = r.GetDecimal(16),
                Disc9 = r.GetDecimal(17), Disc10 = r.GetDecimal(18),
                TaxGroupId = r.IsDBNull(19) ? null : r.GetInt32(19), TaxRate = r.GetDecimal(20),
                LineGross = r.GetDecimal(21), DiscountAmount = r.GetDecimal(22), LineNet = r.GetDecimal(23),
                LineTax = r.GetDecimal(24), LineTotal = r.GetDecimal(25), ReceivedQty = r.GetDecimal(26),
                ProductName = r.IsDBNull(27) ? null : r.GetString(27), ProductCode = r.IsDBNull(28) ? null : r.GetString(28)
            });
        return list;
    }

    public List<Document> GetList(DocType type, DateTime? from = null, DateTime? to = null, int? branchId = null, int? customerId = null, string? search = null)
    {
        var list = new List<Document>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT {HeaderCols}, cu.Name, p.Code
FROM dbo.Documents d
LEFT JOIN dbo.Customers cu ON cu.Id=d.CustomerId
LEFT JOIN dbo.Projects p ON p.Id=d.ProjectId
WHERE d.DocType=@t
  AND (@from IS NULL OR d.DocDate >= @from) AND (@to IS NULL OR d.DocDate < @to)
  AND (@br IS NULL OR d.BranchId=@br) AND (@cust IS NULL OR d.CustomerId=@cust)
  AND (@s IS NULL OR d.FisNo LIKE @s OR d.BelgeNo LIKE @s OR cu.Name LIKE @s)
ORDER BY d.DocDate DESC, d.Id DESC;";
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{search.Trim()}%");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapHeader(r));
        return list;
    }

    /// <summary>Kaynak evraklardan (sipariş/irsaliye) satırları kopyalayıp yeni taslak evrak oluşturur ve bağlantı kurar.
    /// Bir hedefe birden fazla kaynak bağlanabilir (Sipariş→İrsaliye→Fatura hiyerarşisi).</summary>
    public int CreateDraftFromSources(DocType targetType, IList<int> sourceIds, int? userId)
    {
        if (sourceIds.Count == 0) throw new ArgumentException("En az bir kaynak evrak seçilmeli.");
        var sources = sourceIds.Select(GetById).Where(x => x != null).Cast<Document>().ToList();
        if (sources.Count == 0) throw new InvalidOperationException("Kaynak evrak bulunamadı.");
        var first = sources[0];
        if (sources.Any(s => s.CustomerId != first.CustomerId))
            throw new InvalidOperationException("Seçilen kaynak evraklar aynı cariye ait olmalı.");

        // Miktar bazlı KISMİ dönüşüm: her kaynak satırından yalnız KALAN (henüz dönüştürülmemiş) miktar kopyalanır.
        var lines = new List<DocumentLine>();
        var specs = new List<(int srcDoc, int srcLine, decimal qty)>();
        foreach (var s in sources)
            foreach (var l in GetLines(s.Id))
            {
                decimal already = ConvertedQty(l.Id, targetType);
                decimal remaining = l.Qty - already;
                if (remaining <= 0.0001m) continue;   // bu satır tamamen dönüştürülmüş
                lines.Add(new DocumentLine
                {
                    ProductId = l.ProductId, ProductName = l.ProductName, ProductCode = l.ProductCode,
                    Qty = remaining, UnitId = l.UnitId, UnitName = l.UnitName, UnitPrice = l.UnitPrice,
                    Disc1 = l.Disc1, Disc2 = l.Disc2, Disc3 = l.Disc3, Disc4 = l.Disc4, Disc5 = l.Disc5,
                    Disc6 = l.Disc6, Disc7 = l.Disc7, Disc8 = l.Disc8, Disc9 = l.Disc9, Disc10 = l.Disc10,
                    TaxGroupId = l.TaxGroupId, TaxRate = l.TaxRate
                });
                specs.Add((s.Id, l.Id, remaining));
            }
        if (lines.Count == 0) throw new InvalidOperationException("Seçilen kaynak(lar)ın tamamı zaten dönüştürülmüş; kalan miktar yok.");

        var doc = new Document
        {
            DocType = targetType, BranchId = first.BranchId, WarehouseId = first.WarehouseId,
            CurrencyId = first.CurrencyId, CustomerId = first.CustomerId, ProjectId = first.ProjectId,
            PersonnelId = first.PersonnelId, DocDate = DateTime.Now, Status = DocStatus.Taslak,
            Note = $"Kaynak: {string.Join(", ", sources.Select(s => s.FisNo))}"
        };
        Save(doc, lines, userId);
        try
        {
            // Satır bazlı bağlantı: hangi kaynak satırından ne kadar dönüştürüldü (kalan takibi + aşma engeli için)
            var saved = GetLines(doc.Id);   // Save LineNo'yu sırayla atar; GetLines LineNo'ya göre → ekleme sırasıyla eşleşir
            for (int i = 0; i < specs.Count && i < saved.Count; i++)
                AddLink(new DocumentLink { DocumentId = doc.Id, DocumentLineId = saved[i].Id, SourceDocumentId = specs[i].srcDoc, SourceLineId = specs[i].srcLine, Qty = specs[i].qty }, userId);
        }
        catch
        {
            HardDeleteDraft(doc.Id);   // link yazımı yarıda kalırsa taslağı + link'leri temizle (atomik etki, MSDTC'siz)
            throw;
        }
        return doc.Id;
    }

    /// <summary>Hiç işlenmemiş (taslak) bir evrağı satır+link'leriyle kalıcı siler — yarım dönüşüm telafisi.</summary>
    private static void HardDeleteDraft(int docId)
    {
        using var c = Database.OpenCompany();
        foreach (var sql in new[]
        {
            "DELETE FROM dbo.DocumentLinks WHERE DocumentId=@d;",
            "DELETE FROM dbo.DocumentLines WHERE DocumentId=@d;",
            "DELETE FROM dbo.Documents WHERE Id=@d AND Status=0;"
        })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@d", docId);
            try { cmd.ExecuteNonQuery(); } catch { /* telafi; en iyi çaba */ }
        }
    }

    /// <summary>Bir kaynak satırından hedef tipe (iptal olmayan) şimdiye dek dönüştürülmüş toplam miktar.</summary>
    private decimal ConvertedQty(int sourceLineId, DocType targetType)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT ISNULL(SUM(lk.Qty),0) FROM dbo.DocumentLinks lk
JOIN dbo.Documents t ON t.Id=lk.DocumentId
WHERE lk.SourceLineId=@l AND t.DocType=@t AND t.Status<>@iptal;";
        cmd.Parameters.AddWithValue("@l", sourceLineId);
        cmd.Parameters.AddWithValue("@t", (int)targetType);
        cmd.Parameters.AddWithValue("@iptal", (int)DocStatus.Iptal);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    /// <summary>Faturalanmamış irsaliyeler: bir faturaya bağlanmamış, onaylı irsaliyeler.</summary>
    public List<Document> GetUnbilledWaybills(DateTime? from = null, DateTime? to = null)
    {
        var list = new List<Document>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT {HeaderCols}, cu.Name, p.Code
FROM dbo.Documents d
LEFT JOIN dbo.Customers cu ON cu.Id=d.CustomerId
LEFT JOIN dbo.Projects p ON p.Id=d.ProjectId
WHERE d.DocType IN (@i1,@i2) AND d.Status=@onay
  AND (@from IS NULL OR d.DocDate >= @from) AND (@to IS NULL OR d.DocDate < @to)
  AND NOT EXISTS (
    SELECT 1 FROM dbo.DocumentLinks lk JOIN dbo.Documents f ON f.Id=lk.DocumentId
    WHERE lk.SourceDocumentId = d.Id AND f.DocType IN (@f1,@f2,@f3,@f4) AND f.Status <> @iptal)
ORDER BY d.DocDate;";
        cmd.Parameters.AddWithValue("@i1", (int)DocType.AlisIrsaliye);
        cmd.Parameters.AddWithValue("@i2", (int)DocType.SatisIrsaliye);
        cmd.Parameters.AddWithValue("@onay", (int)DocStatus.Onaylandi);
        cmd.Parameters.AddWithValue("@iptal", (int)DocStatus.Iptal);
        cmd.Parameters.AddWithValue("@f1", (int)DocType.AlisFatura);
        cmd.Parameters.AddWithValue("@f2", (int)DocType.SatisFatura);
        cmd.Parameters.AddWithValue("@f3", (int)DocType.AlisIadeFatura);
        cmd.Parameters.AddWithValue("@f4", (int)DocType.SatisIadeFatura);
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapHeader(r));
        return list;
    }

    /// <summary>Hedef tipe TAMAMEN dönüştürülmüş kaynak evrak id'leri (hiçbir satırında kalan miktar yok).
    /// Kısmen dönüştürülmüş kaynaklar listede KALIR (kalan miktar tekrar dönüştürülebilir).</summary>
    public HashSet<int> GetConvertedSourceIds(DocType targetType)
    {
        var set = new HashSet<int>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT s.Id FROM dbo.Documents s
WHERE EXISTS (SELECT 1 FROM dbo.DocumentLinks lk JOIN dbo.Documents t ON t.Id=lk.DocumentId
              WHERE lk.SourceDocumentId=s.Id AND t.DocType=@t AND t.Status<>@iptal)
AND NOT EXISTS (
  SELECT 1 FROM dbo.DocumentLines sl WHERE sl.DocumentId=s.Id AND sl.IsDeleted=0
  AND sl.Qty - ISNULL((SELECT SUM(lk2.Qty) FROM dbo.DocumentLinks lk2 JOIN dbo.Documents t2 ON t2.Id=lk2.DocumentId
                       WHERE lk2.SourceLineId=sl.Id AND t2.DocType=@t AND t2.Status<>@iptal),0) > 0.0001);";
        cmd.Parameters.AddWithValue("@t", (int)targetType);
        cmd.Parameters.AddWithValue("@iptal", (int)DocStatus.Iptal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    public void AddLink(DocumentLink link, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.DocumentLinks (DocumentId, DocumentLineId, SourceDocumentId, SourceLineId, Qty, CreatedBy)
VALUES (@d,@dl,@sd,@sl,@q,@by);";
        cmd.Parameters.AddWithValue("@d", link.DocumentId);
        cmd.Parameters.AddWithValue("@dl", (object?)link.DocumentLineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sd", link.SourceDocumentId);
        cmd.Parameters.AddWithValue("@sl", (object?)link.SourceLineId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@q", link.Qty);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private const string HeaderCols = @"
d.Id, d.DocType, d.FisNo, d.BelgeNo, d.DocDate, d.BelgeTarihi, d.BranchId, d.WarehouseId, d.TargetBranchId, d.TargetWarehouseId,
d.ProjectId, d.CustomerId, d.AddressId, d.PersonnelId, d.ExpenseTypeId, d.CurrencyId, d.UsdRate, d.EurRate, d.Status, d.Note,
d.SubTotal, d.DiscountTotal, d.TaxTotal, d.GrandTotal";

    private static Document MapHeader(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), DocType = (DocType)r.GetInt32(1), FisNo = r.GetString(2),
        BelgeNo = r.IsDBNull(3) ? null : r.GetString(3), DocDate = r.GetDateTime(4),
        BelgeTarihi = r.IsDBNull(5) ? null : r.GetDateTime(5), BranchId = r.GetInt32(6),
        WarehouseId = r.IsDBNull(7) ? null : r.GetInt32(7), TargetBranchId = r.IsDBNull(8) ? null : r.GetInt32(8),
        TargetWarehouseId = r.IsDBNull(9) ? null : r.GetInt32(9), ProjectId = r.IsDBNull(10) ? null : r.GetInt32(10),
        CustomerId = r.IsDBNull(11) ? null : r.GetInt32(11), AddressId = r.IsDBNull(12) ? null : r.GetInt32(12),
        PersonnelId = r.IsDBNull(13) ? null : r.GetInt32(13), ExpenseTypeId = r.IsDBNull(14) ? null : r.GetInt32(14),
        CurrencyId = r.GetInt32(15), UsdRate = r.IsDBNull(16) ? null : r.GetDecimal(16), EurRate = r.IsDBNull(17) ? null : r.GetDecimal(17),
        Status = (DocStatus)r.GetInt32(18), Note = r.IsDBNull(19) ? null : r.GetString(19),
        SubTotal = r.GetDecimal(20), DiscountTotal = r.GetDecimal(21), TaxTotal = r.GetDecimal(22), GrandTotal = r.GetDecimal(23),
        CustomerName = r.IsDBNull(24) ? null : r.GetString(24), ProjectCode = r.IsDBNull(25) ? null : r.GetString(25)
    };
}
