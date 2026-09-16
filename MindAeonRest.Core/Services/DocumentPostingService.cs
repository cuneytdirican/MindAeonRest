using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Evrakların defterlere işlenmesi/geri alınması. Faz 6: faturalar (cari + stok).
/// Fatura → cari borç/alacak (Genel Toplam) + satır bazlı stok giriş/çıkış. İptal → tüm kayıtlar geri alınır.</summary>
public class DocumentPostingService
{
    // Geriye dönük adlar (faturalar)
    public void PostInvoice(int documentId, int? userId) => Post(documentId, userId);
    public void CancelInvoice(int documentId, int? userId) => Cancel(documentId, userId);

    /// <summary>Evrağı onaylar/işler: tipine göre cari hareket (fatura) + stok hareketi (irsaliye/fatura);
    /// sipariş defter çalıştırmaz, yalnızca onaylanır. Durum → Onaylandı.</summary>
    public void Post(int documentId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var d = ReadDoc(c, tx, documentId);
            if (d is null) throw new InvalidOperationException("Evrak bulunamadı.");
            if (d.Status != DocStatus.Taslak) throw new InvalidOperationException("Evrak zaten işlenmiş veya iptal. Önce iptal edin.");

            // ---- Cari ----
            if (d.DocType.RunsCari())
            {
                if (d.CustomerId is not int cust) throw new InvalidOperationException("Fatura için cari zorunlu.");
                bool borc = d.DocType is DocType.SatisFatura or DocType.AlisIadeFatura; // müşteri borç / tedarikçi borç(iade)
                var cariType = d.DocType switch
                {
                    DocType.SatisFatura => CariDocType.SatisFaturasi,
                    DocType.AlisFatura => CariDocType.AlisFaturasi,
                    DocType.SatisIadeFatura => CariDocType.SatisIadeFaturasi,
                    _ => CariDocType.AlisIadeFaturasi
                };
                CariHareketService.PostTx(c, tx, new CariHareket
                {
                    BranchId = d.BranchId, CustomerId = cust, TxnDate = d.DocDate,
                    DocType = cariType, DocRefType = "Fatura", DocRefId = d.Id, DocNo = d.FisNo,
                    Description = $"{d.DocType.ToTr()} {d.FisNo}", CurrencyId = d.CurrencyId,
                    Debt = borc ? d.GrandTotal : 0, Credit = borc ? 0 : d.GrandTotal, ProjectId = d.ProjectId
                }, userId);
            }

            // ---- Stok ----
            // #7b: Fatura, bağlı kaynak irsaliye ONAYLANMADAN işlenirse hem fatura hem sonradan irsaliye stok
            // işler → çift stok. HasIrsaliyeSource yalnız ONAYLI irsaliyeyi "stok taşımış" sayar; taslak kaynak
            // varsa net hata verip engelle (stok işlemeyi irsaliyeye bırak).
            if (d.DocType.RunsCari() && HasPendingIrsaliyeSource(c, tx, documentId))
                throw new InvalidOperationException("Önce kaynak irsaliyeyi onaylayın; sonra faturayı işleyin.");
            // Fatura bir irsaliyeden oluştuysa stok zaten irsaliyede hareket etti → tekrar etme.
            bool stockAlreadyByWaybill = d.DocType.RunsCari() && HasIrsaliyeSource(c, tx, documentId);
            int sign = d.DocType.StockSign();
            if (d.DocType.RunsStock() && sign != 0 && !stockAlreadyByWaybill)
            {
                if (d.WarehouseId is not int wh) throw new InvalidOperationException("Depo seçili olmalı.");
                var mvType = d.DocType == DocType.Fire ? StockMovementType.Fire : sign > 0 ? StockMovementType.Giris : StockMovementType.Cikis;
                foreach (var (lineId, productId, qty) in ReadLines(c, tx, documentId))
                    InsertStock(c, tx, documentId, lineId, productId, wh, sign * qty, mvType, d.BranchId, userId);

                // FAZ 1a Maliyet Motoru: alış evrağı (irsaliye/fatura) stok girişinden SONRA ağırlıklı-ortalama maliyeti güncelle.
                // Aynı tx: maliyet doğruluğu için (hata olursa posting de geri alınır). Faturadan gelen stok irsaliyede
                // hareket etmişse (stockAlreadyByWaybill) bu blok zaten çalışmaz → çift güncelleme olmaz.
                if (d.DocType is DocType.AlisIrsaliye or DocType.AlisFatura)
                    new CostService().UpdateAvgCostOnPurchase(c, tx, documentId, wh);
            }

            // BULGU 1: İrsaliyeden üretilen alış FATURASI Post edilince stok tekrar hareket etmez
            // (stockAlreadyByWaybill) → yukarıdaki blok atlanır. Fakat faturanın NİHAİ satır fiyatı irsaliye
            // fiyatından farklıysa maliyet güncellenmeli. YALNIZ maliyet (stok/cari değil); fiyat aynıysa
            // fark 0 → değişiklik yok (mevcut davranış korunur).
            if (d.DocType == DocType.AlisFatura && stockAlreadyByWaybill && d.WarehouseId is int whInv)
                new CostService().UpdateAvgCostOnInvoiceOverWaybill(c, tx, documentId, whInv);

            SetStatus(c, tx, documentId, DocStatus.Onaylandi, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Evrağı iptal eder: cari + stok kayıtlarını geri alır, durumu İptal yapar.</summary>
    public void Cancel(int documentId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var d = ReadDoc(c, tx, documentId);
            if (d is null) throw new InvalidOperationException("Evrak bulunamadı.");
            if (d.Status is DocStatus.Taslak or DocStatus.Iptal)
                throw new InvalidOperationException("Taslak ya da iptal edilmiş evrak için iptal yapılamaz.");

            // #7a: Bu evraktan dönüştürülmüş (ör. irsaliyeden üretilmiş) İŞLENMİŞ downstream evrak varsa,
            // kaynağı iptal etmek stok/cari tutarsızlığı yaratır → önce downstream iptal edilmeli.
            if (HasProcessedDownstream(c, tx, documentId))
                throw new InvalidOperationException("Bu evraktan üretilmiş işlenmiş fatura/evrak var; önce onu iptal edin.");

            // Cari + stok kayıtları geri alınır. Transferde StockMovements'da hem kaynak çıkış hem hedef giriş
            // DocumentId ile silinir (iki bacak da geri alınır); teslim sayaçları sıfırlanır.
            Exec(c, tx, "DELETE FROM dbo.CariHareketler WHERE DocRefType='Fatura' AND DocRefId=@d;", documentId);
            Exec(c, tx, "DELETE FROM dbo.StockMovements WHERE DocumentId=@d;", documentId);
            if (d.DocType == DocType.DepoTransfer)
                Exec(c, tx, "UPDATE dbo.DocumentLines SET ReceivedQty=0 WHERE DocumentId=@d;", documentId);
            SetStatus(c, tx, documentId, DocStatus.Iptal, userId);

            // BULGU 8: Alış evrağı iptalinde AvgCost/LastCost kirli kalmasın. Stok hareketleri silindi ama
            // maliyet Products üzerinde duruyordu → etkilenen ürünlerin AvgCost'unu kalan (onaylı, fiyatlı)
            // alışlardan yeniden hesapla. Status artık İptal olduğu için bu evrak dışlanır. Yalnız maliyet.
            if (d.DocType is DocType.AlisIrsaliye or DocType.AlisFatura)
            {
                var cost = new CostService();
                foreach (var pid in ReadLines(c, tx, documentId).Select(l => l.ProductId).Distinct())
                    cost.RecomputeAvgCost(c, tx, pid);
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Depo transfer: kaynak depodan çıkış (mal yolda). Teslim alımı ReceiveTransfer ile.</summary>
    public void PostTransfer(int documentId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var d = ReadDoc(c, tx, documentId);
            if (d is null || d.DocType != DocType.DepoTransfer) throw new InvalidOperationException("Depo transfer evrağı değil.");
            if (d.Status != DocStatus.Taslak) throw new InvalidOperationException("Evrak zaten işlenmiş/iptal.");
            if (d.WarehouseId is not int src) throw new InvalidOperationException("Kaynak depo seçilmeli.");
            if (d.TargetWarehouseId is null) throw new InvalidOperationException("Hedef depo seçilmeli.");
            foreach (var (lineId, productId, qty) in ReadLines(c, tx, documentId))
                InsertStock(c, tx, documentId, lineId, productId, src, -qty, StockMovementType.Transfer, d.BranchId, userId);
            SetStatus(c, tx, documentId, DocStatus.Onaylandi, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Transfer teslim alımı: satır bazlı teslim alınan miktar kadar hedef depoya giriş.</summary>
    public void ReceiveTransfer(int documentId, IDictionary<int, decimal> lineReceipts, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var d = ReadDoc(c, tx, documentId);
            if (d is null || d.DocType != DocType.DepoTransfer) throw new InvalidOperationException("Depo transfer evrağı değil.");
            if (d.Status != DocStatus.Onaylandi && d.Status != DocStatus.KismenKapandi) throw new InvalidOperationException("Önce transferi işleyin (kaynak çıkış).");
            if (d.TargetWarehouseId is not int tgt) throw new InvalidOperationException("Hedef depo yok.");

            bool allFull = true;
            foreach (var (lineId, productId, qty) in ReadLines(c, tx, documentId))
            {
                decimal already = LineReceived(c, tx, lineId);
                decimal recv = lineReceipts.TryGetValue(lineId, out var v) ? v : 0;
                if (recv < 0) throw new InvalidOperationException("Teslim miktarı negatif olamaz.");
                if (already + recv > qty) throw new InvalidOperationException($"Teslim, sevk miktarını aşamaz (sevk {qty:0.##}, önceki {already:0.##}).");
                if (recv > 0)
                {
                    InsertStock(c, tx, documentId, lineId, productId, tgt, recv, StockMovementType.Transfer, d.BranchId, userId);
                    UpdateReceived(c, tx, lineId, already + recv);
                    already += recv;
                }
                if (already < qty) allFull = false;
            }
            SetStatus(c, tx, documentId, allFull ? DocStatus.Kapandi : DocStatus.KismenKapandi, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Sayım kesinleştirme: sayılan miktar ile sistemdeki miktar farkı kadar stok düzeltme.</summary>
    public void PostCount(int documentId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            var d = ReadDoc(c, tx, documentId);
            if (d is null || d.DocType != DocType.Sayim) throw new InvalidOperationException("Sayım evrağı değil.");
            if (d.Status != DocStatus.Taslak) throw new InvalidOperationException("Sayım zaten kesinleştirilmiş/iptal.");
            if (d.WarehouseId is not int wh) throw new InvalidOperationException("Depo seçilmeli.");
            foreach (var (lineId, productId, counted) in ReadLines(c, tx, documentId))
            {
                decimal sys = SystemStock(c, tx, productId, wh);
                decimal diff = counted - sys;   // + fazla, - eksik
                if (diff != 0)
                    InsertStock(c, tx, documentId, lineId, productId, wh, diff, StockMovementType.Sayim, d.BranchId, userId);
            }
            SetStatus(c, tx, documentId, DocStatus.Onaylandi, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private static decimal LineReceived(SqlConnection c, SqlTransaction tx, int lineId)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT ReceivedQty FROM dbo.DocumentLines WHERE Id=@l;";
        cmd.Parameters.AddWithValue("@l", lineId);
        return (decimal)(cmd.ExecuteScalar() ?? 0m);
    }

    private static void UpdateReceived(SqlConnection c, SqlTransaction tx, int lineId, decimal recv)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE dbo.DocumentLines SET ReceivedQty=@r WHERE Id=@l;";
        cmd.Parameters.AddWithValue("@r", recv);
        cmd.Parameters.AddWithValue("@l", lineId);
        cmd.ExecuteNonQuery();
    }

    private static decimal SystemStock(SqlConnection c, SqlTransaction tx, int productId, int wh)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT ISNULL(SUM(Qty),0) FROM dbo.StockMovements WHERE ProductId=@p AND WarehouseId=@w;";
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@w", wh);
        return (decimal)cmd.ExecuteScalar()!;
    }

    private static bool HasIrsaliyeSource(SqlConnection c, SqlTransaction tx, int docId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        // Yalnız ONAYLI irsaliye stoğu hareket ettirmiş sayılır; iptal/taslak irsaliye stok atlamasına yol açmamalı
        cmd.CommandText = @"
SELECT COUNT(1) FROM dbo.DocumentLinks lk JOIN dbo.Documents s ON s.Id=lk.SourceDocumentId
WHERE lk.DocumentId=@d AND s.DocType IN (@i1,@i2) AND s.Status=@onay;";
        cmd.Parameters.AddWithValue("@d", docId);
        cmd.Parameters.AddWithValue("@i1", (int)DocType.AlisIrsaliye);
        cmd.Parameters.AddWithValue("@i2", (int)DocType.SatisIrsaliye);
        cmd.Parameters.AddWithValue("@onay", (int)DocStatus.Onaylandi);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>#7b: Bu faturaya bağlı kaynak irsaliyelerden HENÜZ ONAYLANMAMIŞ (Taslak) olan var mı?
    /// Varsa fatura stok işlerse, irsaliye sonradan onaylanınca çift stok oluşur → engellenir.</summary>
    private static bool HasPendingIrsaliyeSource(SqlConnection c, SqlTransaction tx, int docId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT COUNT(1) FROM dbo.DocumentLinks lk JOIN dbo.Documents s ON s.Id=lk.SourceDocumentId
WHERE lk.DocumentId=@d AND s.DocType IN (@i1,@i2) AND s.Status=@taslak;";
        cmd.Parameters.AddWithValue("@d", docId);
        cmd.Parameters.AddWithValue("@i1", (int)DocType.AlisIrsaliye);
        cmd.Parameters.AddWithValue("@i2", (int)DocType.SatisIrsaliye);
        cmd.Parameters.AddWithValue("@taslak", (int)DocStatus.Taslak);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    /// <summary>#7a: Bu evrak KAYNAK olarak dönüştürülmüş, Taslak/İptal DIŞINDA (işlenmiş) bir downstream
    /// evrağa bağlı mı? (ör. irsaliyeden üretilmiş onaylı fatura). Varsa kaynağın iptali engellenir.</summary>
    private static bool HasProcessedDownstream(SqlConnection c, SqlTransaction tx, int docId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT COUNT(1) FROM dbo.DocumentLinks lk JOIN dbo.Documents t ON t.Id=lk.DocumentId
WHERE lk.SourceDocumentId=@d AND t.Status NOT IN (@taslak,@iptal);";
        cmd.Parameters.AddWithValue("@d", docId);
        cmd.Parameters.AddWithValue("@taslak", (int)DocStatus.Taslak);
        cmd.Parameters.AddWithValue("@iptal", (int)DocStatus.Iptal);
        return (int)cmd.ExecuteScalar()! > 0;
    }

    // ---- helpers ----
    private static Document? ReadDoc(SqlConnection c, SqlTransaction tx, int id)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT DocType, FisNo, DocDate, BranchId, WarehouseId, ProjectId, CustomerId, CurrencyId, GrandTotal, Status, TargetWarehouseId FROM dbo.Documents WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Document
        {
            Id = id, DocType = (DocType)r.GetInt32(0), FisNo = r.GetString(1), DocDate = r.GetDateTime(2),
            BranchId = r.GetInt32(3), WarehouseId = r.IsDBNull(4) ? null : r.GetInt32(4),
            ProjectId = r.IsDBNull(5) ? null : r.GetInt32(5), CustomerId = r.IsDBNull(6) ? null : r.GetInt32(6),
            CurrencyId = r.GetInt32(7), GrandTotal = r.GetDecimal(8), Status = (DocStatus)r.GetInt32(9),
            TargetWarehouseId = r.IsDBNull(10) ? null : r.GetInt32(10)
        };
    }

    private static IEnumerable<(int LineId, int ProductId, decimal Qty)> ReadLines(SqlConnection c, SqlTransaction tx, int docId)
    {
        var list = new List<(int, int, decimal)>();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id, ProductId, Qty FROM dbo.DocumentLines WHERE DocumentId=@d;";
        cmd.Parameters.AddWithValue("@d", docId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2)));
        return list;
    }

    private static void InsertStock(SqlConnection c, SqlTransaction tx, int docId, int lineId, int productId, int wh,
        decimal qty, StockMovementType type, int branchId, int? userId)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        // MoveDate UTC yazılır (SYSUTCDATETIME) → sipariş akışıyla ve okuyucuların UTC→yerel dönüşümüyle tutarlı.
        // İş-tarihi (DocDate) Document kaydında durur; MoveDate yalnız hareket damgasıdır.
        cmd.CommandText = @"
INSERT INTO dbo.StockMovements (DocumentId, DocumentLineId, ProductId, WarehouseId, Qty, MovementType, BranchId, MoveDate, CreatedBy)
VALUES (@doc,@line,@p,@w,@qty,@t,@b,SYSUTCDATETIME(),@by);";
        cmd.Parameters.AddWithValue("@doc", docId);
        cmd.Parameters.AddWithValue("@line", lineId);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.Parameters.AddWithValue("@w", wh);
        cmd.Parameters.AddWithValue("@qty", qty);
        cmd.Parameters.AddWithValue("@t", (int)type);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SetStatus(SqlConnection c, SqlTransaction tx, int id, DocStatus status, int? userId)
        => Exec(c, tx, $"UPDATE dbo.Documents SET Status={(int)status}, UpdatedBy=@by2, UpdatedAt=SYSUTCDATETIME() WHERE Id=@d;", id, userId);

    private static void Exec(SqlConnection c, SqlTransaction tx, string sql, int docId, int? userId = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@d", docId);
        if (sql.Contains("@by2")) cmd.Parameters.AddWithValue("@by2", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
