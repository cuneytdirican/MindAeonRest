using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>X (anlık) / Z (gün sonu) raporu.</summary>
public class ReportService
{
    private readonly CashMovementService _cash = new();
    private readonly BankMovementService _bank = new();
    private readonly PosMovementService _pos = new();
    private readonly MealVoucherMovementService _meal = new();

    public XZReport GetDailyReport(int branchId, DateTime date)
    {
        var d = date.Date;
        int cutoff = _branch.GetDayStartMinutes(branchId);
        // Şube ADI SEÇİLEN branchId'den çözülür (oturum şubesinden DEĞİL) — yönetici başka şubenin Z/X raporunu
        // görüntülerken başlıkta yanlış (oturum) şube adı görünmesin.
        var rep = new XZReport { Date = d, BranchName = _branch.GetById(branchId)?.Name ?? Helpers.Session.CurrentBranch?.Name ?? "" };
        using var c = Database.OpenCompany();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT ISNULL(SUM(Total),0), COUNT(1), COUNT(DISTINCT OrderId) FROM dbo.ClFiches WHERE BranchId=@b AND IsDeleted=0 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, FicheDate) AS DATE)=@d;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@d", d);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { rep.SalesTotal = r.GetDecimal(0); rep.FisCount = r.GetInt32(1); rep.OrderCount = r.GetInt32(2); }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT pl.Kind, SUM(pl.Amount) FROM dbo.PaymentLines pl JOIN dbo.ClFiches cf ON cf.Id=pl.ClFicheId
WHERE cf.BranchId=@b AND cf.IsDeleted=0 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, cf.FicheDate) AS DATE)=@d GROUP BY pl.Kind;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d); cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rep.Payments.Add(new ReportPay { Kind = (PaymentKind)r.GetInt32(0), Amount = r.GetDecimal(1) });
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT cl.TaxRate, SUM(cl.Matrah), SUM(cl.TaxAmount) FROM dbo.ClFicheLines cl JOIN dbo.ClFiches cf ON cf.Id=cl.ClFicheId
WHERE cf.BranchId=@b AND cf.IsDeleted=0 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, cf.FicheDate) AS DATE)=@d GROUP BY cl.TaxRate ORDER BY cl.TaxRate;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d); cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rep.Kdv.Add(new ReportKdv { Rate = r.GetDecimal(0), Matrah = r.GetDecimal(1), Tax = r.GetDecimal(2) });
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT ISNULL(SUM(-Qty),0) FROM dbo.StockMovements WHERE BranchId=@b AND MovementType=7 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, MoveDate) AS DATE)=@d;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d); cmd.Parameters.AddWithValue("@cutoff", cutoff);
            rep.FireQty = (decimal)cmd.ExecuteScalar()!;
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ISNULL(SUM(l.Qty*l.UnitPrice - l.DiscountAmount),0) FROM dbo.OrderLines l JOIN dbo.Orders o ON o.Id=l.OrderId
WHERE o.BranchId=@b AND l.IsTreat=1 AND l.IsDeleted=0 AND CAST(DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, o.ClosedAt) AS DATE)=@d;";
            cmd.Parameters.AddWithValue("@b", branchId); cmd.Parameters.AddWithValue("@d", d); cmd.Parameters.AddWithValue("@cutoff", cutoff);
            rep.TreatTotal = (decimal)cmd.ExecuteScalar()!;
        }

        rep.CashBalances = _cash.GetBalances(branchId);
        rep.BankBalances = _bank.GetBalances();
        rep.PosBalances = _pos.GetBalances();
        rep.MealVoucherBalances = _meal.GetBalanceList(branchId);
        return rep;
    }

    /// <summary>Z raporu snapshot kaydeder (gün sonu kapanış işareti).</summary>
    public void SaveZ(XZReport rep, int branchId, int? userId)
    {
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try { SaveZ(c, tx, rep, branchId, userId); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Mevcut bağlantı+transaction altında Z snapshot kaydeder (atomik gün sonu devri için).</summary>
    public void SaveZ(Microsoft.Data.SqlClient.SqlConnection c, Microsoft.Data.SqlClient.SqlTransaction tx, XZReport rep, int branchId, int? userId)
    {
        // Aynı gün/şube için Z zaten alınmışsa tekrar kayıt atma (mükerrer Z geçmişi/tutarı bozar).
        using (var chk = c.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(1) FROM dbo.ZReports WHERE BranchId=@b AND ReportDate=@d;";
            chk.Parameters.AddWithValue("@b", branchId);
            chk.Parameters.AddWithValue("@d", rep.Date.Date);
            if ((int)chk.ExecuteScalar()! > 0)
                throw new InvalidOperationException("Bu gün için Z raporu zaten alınmış.");
        }
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO dbo.ZReports (BranchId, ReportDate, SalesTotal, FisCount, TakenAt, CreatedBy, KioskNo) VALUES (@b,@d,@s,@f,SYSUTCDATETIME(),@by,@kno);";
        cmd.Parameters.AddWithValue("@kno", Helpers.Session.CurrentKioskNo);
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@d", rep.Date.Date);
        cmd.Parameters.AddWithValue("@s", rep.SalesTotal);
        cmd.Parameters.AddWithValue("@f", rep.FisCount);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Z (gün sonu) alırken kasaları ŞUBE kasasına devreder: Nakit SEÇİLEN tutar (kalan kasada kalır),
    /// Kart tüm POS bakiyeleri TAM. Sonra Z snapshot kaydeder. (KIOSK/ikincil kasalar → şube merkez kasası)</summary>
    public void TakeZWithTransfer(XZReport rep, int branchId, decimal cashTransfer, int? targetCashId, int? targetPosId, int? userId)
    {
        // Mükerrer Z guard'ı ÖNCE (transfer yapıp sonra Z'nin reddedilmesini önle)
        using (var gc = Database.OpenCompany())
        using (var chk = gc.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(1) FROM dbo.ZReports WHERE BranchId=@b AND ReportDate=@d;";
            chk.Parameters.AddWithValue("@b", branchId);
            chk.Parameters.AddWithValue("@d", rep.Date.Date);
            if ((int)chk.ExecuteScalar()! > 0) throw new InvalidOperationException("Bu gün için Z raporu zaten alınmış.");
        }

        var cashSvc = new CashMovementService();
        var posSvc = new PosMovementService();

        // Devredilecek nakit kaynaklarını ve kart bakiyelerini ÖNCE hesapla (transfer/Z TEK transaction altında atomik çalışsın)
        var cashTransfers = new List<(int From, int To, decimal Amount)>();
        if (targetCashId is int tc && cashTransfer > 0.005m)
        {
            var kioskCashIds = new CashRegisterService().GetAll().Where(k => k.BranchId == branchId && k.Scope == FinanceScope.Kiosk && k.Id != tc).Select(k => k.Id).ToHashSet();
            var sources = cashSvc.GetBalances(branchId).Where(b => kioskCashIds.Contains(b.CashRegisterId) && b.Balance > 0).OrderByDescending(b => b.Balance).ToList();
            decimal avail = sources.Sum(b => b.Balance);
            if (cashTransfer > avail + 0.005m)
                throw new InvalidOperationException($"Devredilecek nakit ({cashTransfer:N2}), KIOSK kasalarındaki devredilebilir nakitten ({avail:N2}) fazla olamaz.");
            decimal rem = cashTransfer;
            foreach (var b in sources)
            {
                if (rem <= 0.005m) break;
                decimal amt = Math.Min(b.Balance, rem);
                cashTransfers.Add((b.CashRegisterId, tc, amt));
                rem -= amt;
            }
        }

        var posTransfers = new List<(int From, int To, decimal Amount)>();
        if (targetPosId is int tp)
        {
            var kioskPosIds = new PosService().GetAll().Where(p => p.BranchId == branchId && p.Scope == FinanceScope.Kiosk && p.Id != tp).Select(p => p.Id).ToHashSet();
            foreach (var b in posSvc.GetBalances().Where(b => kioskPosIds.Contains(b.PosId) && b.Balance > 0).ToList())
                posTransfers.Add((b.PosId, tp, b.Balance));
        }

        // Nakit/kart devri + Z snapshot TEK bağlantı+transaction altında: yarıda hata olursa hepsi geri alınır
        // (kalıcı devir + Z yok / tekrar çalışınca çift devir senaryosu engellenir).
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            foreach (var t in cashTransfers)
                cashSvc.Transfer(c, tx, t.From, t.To, t.Amount, "Z gün sonu nakit devri (KIOSK→Şube)", userId);
            foreach (var t in posTransfers)
                posSvc.Transfer(c, tx, t.From, t.To, t.Amount, "Z gün sonu kart devri (KIOSK→Şube)", userId);
            SaveZ(c, tx, rep, branchId, userId);
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private readonly BranchService _branch = new();

    private const string LocalDateExpr = "DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()), {0})";

    /// <summary>İş-günü kesimli yerel ifade: UTC kolon önce yerele çevrilir, sonra @cutoff (DayStartMinutes) geri çekilir.
    /// Gün ATAMASI (tarih filtresi / GROUP BY gün) yapan yerlerde LocalDateExpr yerine bunu kullan; @cutoff komuta bağlanmalı.
    /// cutoff=0 iken sonuç LocalDateExpr ile birebir aynıdır (yerel takvim günü).</summary>
    private const string BusinessDayExpr = "DATEADD(MINUTE, DATEDIFF(MINUTE, SYSUTCDATETIME(), SYSDATETIME()) - @cutoff, {0})";

    /// <summary>Nullable şube için iş-günü kesimini çöz: branchId verilmişse onun kesimi, yoksa oturum şubesi, o da yoksa 0.</summary>
    private int ResolveCutoff(int? branchId)
        => branchId is int b ? _branch.GetDayStartMinutes(b)
           : (Helpers.Session.CurrentBranch?.DayStartMinutes ?? 0);

    /// <summary>Detaylı fiş/adisyon raporu (kapanmış fişler): şube, lokasyon, garson, kasiyer, ödeme tipleri, indirim, ikram.</summary>
    public List<FicheReportRow> GetFicheReport(int branchId, DateTime from, DateTime to)
    {
        var rows = new List<FicheReportRow>();
        var users = new UserService().GetAll().ToDictionary(u => u.Id, u => u.FullName);
        string branchName = new BranchService().GetByCompany(Helpers.Session.CurrentCompany?.Id ?? 0).FirstOrDefault(b => b.Id == branchId)?.Name ?? (Helpers.Session.CurrentBranch?.Name ?? "");
        int cutoff = _branch.GetDayStartMinutes(branchId);
        // Gün ATAMASI (tarih filtresi + raporda görünen iş-günü tarihi) iş-günü kesimine göre.
        string fdLocal = string.Format(BusinessDayExpr, "f.FicheDate");

        // Ödemeler fiş bazında (tip + tutar)
        var pays = new Dictionary<int, List<string>>();
        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT p.ClFicheId, p.Kind, SUM(p.Amount) AS Amt
FROM dbo.PaymentLines p JOIN dbo.ClFiches f ON f.Id=p.ClFicheId
WHERE f.BranchId=@b AND f.IsDeleted=0 AND CAST({fdLocal} AS DATE) BETWEEN @from AND @to
GROUP BY p.ClFicheId, p.Kind;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int fid = r.GetInt32(0); var kind = (PaymentKind)r.GetInt32(1); decimal amt = r.GetDecimal(2);
                if (!pays.TryGetValue(fid, out var l)) { l = new(); pays[fid] = l; }
                l.Add($"{kind.ToTr()} {amt:N0}");
            }
        }

        using (var c = Database.OpenCompany())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
SELECT f.Id, f.Code, f.Total, cu.Name AS CustomerName, f.CreatedBy AS CashierId, {fdLocal} AS LocalDate,
       o.Code AS OrderCode, z.Name AS ZoneName, o.CreatedBy AS WaiterId, o.DiscountAmount AS OrderDiscount,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes,
       (SELECT ISNULL(SUM(l.DiscountAmount),0) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0 AND l.LineStatus<>4) AS LineDiscount,
       (SELECT ISNULL(SUM(CASE WHEN l.IsTreat=1 THEN l.Qty*l.UnitPrice ELSE 0 END),0) FROM dbo.OrderLines l WHERE l.OrderId=o.Id AND l.IsDeleted=0 AND l.LineStatus<>4) AS TreatTotal
FROM dbo.ClFiches f
JOIN dbo.Orders o ON o.Id=f.OrderId
LEFT JOIN dbo.Zones z ON z.Id=o.ZoneId
LEFT JOIN dbo.Customers cu ON cu.Id=f.CustomerId
WHERE f.BranchId=@b AND f.IsDeleted=0 AND CAST({fdLocal} AS DATE) BETWEEN @from AND @to
ORDER BY f.FicheDate DESC;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int fid = r.GetInt32(0);
                int? wId = r["WaiterId"] as int?; int? cId = r["CashierId"] as int?;
                rows.Add(new FicheReportRow
                {
                    FicheCode = r.GetString(1),
                    Total = r.GetDecimal(2),
                    CustomerName = r["CustomerName"] as string ?? "",
                    Date = r.GetDateTime(5),
                    OrderCode = r["OrderCode"] as string ?? "",
                    ZoneName = r["ZoneName"] as string ?? "",
                    TableCodes = r["TableCodes"] as string ?? "",
                    Discount = (r["OrderDiscount"] as decimal? ?? 0) + (r["LineDiscount"] as decimal? ?? 0),
                    Treat = r["TreatTotal"] as decimal? ?? 0,
                    BranchName = branchName,
                    WaiterName = wId is int w && users.TryGetValue(w, out var wn) ? wn : "",
                    CashierName = cId is int ci && users.TryGetValue(ci, out var cn) ? cn : "",
                    Payments = pays.TryGetValue(fid, out var pl) ? string.Join(" + ", pl) : "",
                });
            }
        }
        return rows;
    }

    /// <summary>İptal raporu: iptal edilen satırlar (sebep + iptal eden + Fire/Stoğa iade). Hesaba girmez.</summary>
    public List<CancelReportRow> GetCancellationReport(int branchId, DateTime from, DateTime to)
    {
        var rows = new List<CancelReportRow>();
        var users = new UserService().GetAll().ToDictionary(u => u.Id, u => u.FullName);
        int cutoff = _branch.GetDayStartMinutes(branchId);
        // İptal satırının HANGİ GÜNE ait olduğu (görünen tarih + filtre) iş-günü kesimine göre.
        string cdLocal = string.Format(BusinessDayExpr, "ISNULL(l.CancelledAt, l.UpdatedAt)");
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT {cdLocal} AS LocalDate, o.Code AS OrderCode, p.Name AS ProductName, l.Qty, l.UnitPrice, l.DiscountAmount,
       l.CancelReason, l.CancelledBy, l.ReturnedToStock,
       (SELECT STRING_AGG(t.Code, ', ') FROM dbo.OrderTables ot JOIN dbo.Tables t ON t.Id=ot.TableId WHERE ot.OrderId=o.Id) AS TableCodes
FROM dbo.OrderLines l
JOIN dbo.Orders o ON o.Id=l.OrderId
LEFT JOIN dbo.Products p ON p.Id=l.ProductId
WHERE o.BranchId=@b AND l.LineStatus=4 AND l.ParentLineId IS NULL
  AND CAST({cdLocal} AS DATE) BETWEEN @from AND @to
ORDER BY ISNULL(l.CancelledAt, l.UpdatedAt) DESC;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", to.Date);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int? by = r["CancelledBy"] as int?;
            bool? rts = r["ReturnedToStock"] as bool?;
            decimal qty = Convert.ToDecimal(r["Qty"]); decimal up = Convert.ToDecimal(r["UnitPrice"]);
            rows.Add(new CancelReportRow
            {
                Date = r.GetDateTime(0),
                OrderCode = r["OrderCode"] as string ?? "",
                ProductName = r["ProductName"] as string ?? "",
                Qty = qty,
                Amount = qty * up - (r["DiscountAmount"] as decimal? ?? 0),
                Reason = string.IsNullOrWhiteSpace(r["CancelReason"] as string) ? "Belirtilmedi" : (string)r["CancelReason"],
                CancelledByName = by is int b2 && users.TryGetValue(b2, out var bn) ? bn : "",
                Disposition = rts == false ? "Fire" : rts == true ? "Stoğa İade" : "-",
                TableCodes = r["TableCodes"] as string ?? "",
            });
        }
        return rows;
    }

    /// <summary>Belirtilen şube/KIOSK için en son Z'nin alınma zamanı (UTC) — "son Z'den beri" kapsamı için.</summary>
    public DateTime? GetLastZTakenAt(int? branchId, int? kioskNo)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MAX(TakenAt) FROM dbo.ZReports WHERE (@b IS NULL OR BranchId=@b) AND (@kno IS NULL OR KioskNo=@kno);";
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kno", (object?)kioskNo ?? DBNull.Value);
        var o = cmd.ExecuteScalar();
        return o is DateTime dt ? dt : null;
    }

    /// <summary>KIOSK dashboard: tipe göre tahsil edilen para. sinceUtc verilirse o andan beri; aksi halde tarih aralığı (yerel).</summary>
    public KioskMoneySnapshot GetKioskMoney(int? branchId, int? kioskNo, DateTime from, DateTime to, DateTime? sinceUtc = null)
    {
        var snap = new KioskMoneySnapshot { LastZAt = GetLastZTakenAt(branchId, kioskNo) };
        int cutoff = ResolveCutoff(branchId);
        // Tarih aralığı modunda ödeme HANGİ GÜNE düşüyor → iş-günü kesimine göre. (sinceUtc modu ham UTC karşılaştırması, kesim gerekmez.)
        string payLocal = string.Format(BusinessDayExpr, "p.PayDate");
        string whereTime = sinceUtc is null ? $"CAST({payLocal} AS DATE) BETWEEN @from AND @to" : "p.PayDate > @since";
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT p.Kind, SUM(p.Amount) AS Amt, COUNT(DISTINCT f.Id) AS Fis
FROM dbo.PaymentLines p
JOIN dbo.ClFiches f ON f.Id = p.ClFicheId
WHERE f.IsDeleted=0 AND (@b IS NULL OR f.BranchId=@b) AND (@kno IS NULL OR f.KioskNo=@kno) AND {whereTime}
GROUP BY p.Kind;";
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kno", (object?)kioskNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", to.Date);
        cmd.Parameters.AddWithValue("@since", (object?)sinceUtc ?? DBNull.Value);
        if (sinceUtc is null) cmd.Parameters.AddWithValue("@cutoff", cutoff);
        var fisSet = new HashSet<int>();
        int fisTotal = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kind = (PaymentKind)r.GetInt32(0);
            decimal amt = r.IsDBNull(1) ? 0 : r.GetDecimal(1);
            int fis = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            fisTotal = Math.Max(fisTotal, fis);
            if (kind == PaymentKind.Nakit) snap.Nakit += amt;
            else if (kind.IsCardType()) snap.KrediKarti += amt;
            else if (kind == PaymentKind.YemekCeki) snap.YemekCeki += amt;
            else if (kind == PaymentKind.HavaleEft) snap.Havale += amt;
            else if (kind == PaymentKind.HediyeCeki) snap.HediyeCeki += amt;
            else if (kind == PaymentKind.PuanSadakat) snap.PuanSadakat += amt;
            else if (kind == PaymentKind.AcikHesap) snap.AcikHesap += amt;
        }
        snap.FisCount = fisTotal;
        return snap;
    }

    /// <summary>Masa anlık durumu (şu an): Boş / Dolu (açık adisyon) / Rezerve. Şube + lokasyon filtreli.</summary>
    public List<TableLiveRow> GetTableLiveStatus(int? branchId, int? zoneId)
    {
        var rows = new List<TableLiveRow>();
        var users = new UserService().GetAll().ToDictionary(u => u.Id, u => u.FullName);
        var branches = new BranchService().GetByCompany(Helpers.Session.CurrentCompany?.Id ?? 0).ToDictionary(b => b.Id, b => b.Name);
        int cutoff = ResolveCutoff(branchId);
        // OpenedLocal: adisyonun GERÇEK açılış saati (görünen değer) → kesim uygulanmaz, ham yerel zaman.
        string opened = string.Format(LocalDateExpr, "oo.OpenedAt");
        // Rezervasyon "bugün mü" GÜN ataması → iş-günü kesimine göre (her iki taraf da kesimli; cutoff=0'da birebir aynı).
        string resStart = string.Format(BusinessDayExpr, "rv.StartTime");
        string todayBiz = "DATEADD(MINUTE, -@cutoff, SYSDATETIME())";
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT t.Code, t.Name AS TableName, z.Name AS ZoneName, t.BranchId, t.Seats,
       oo.OrderCode, {opened} AS OpenedLocal, oo.CoverCount, oo.WaiterId,
       (SELECT ISNULL(SUM(CASE WHEN l.IsTreat=1 OR l.ParentLineId IS NOT NULL THEN 0 ELSE l.Qty*l.UnitPrice - l.DiscountAmount END),0)
        FROM dbo.OrderLines l WHERE l.OrderId=oo.OrderId AND l.IsDeleted=0 AND l.LineStatus<>4) AS Total,
       (SELECT COUNT(1) FROM dbo.ReservationTables rt JOIN dbo.Reservations rv ON rv.Id=rt.ReservationId
          WHERE rt.TableId=t.Id AND rv.IsDeleted=0 AND rv.Status NOT IN (4,5)
            AND CAST({resStart} AS DATE)=CAST({todayBiz} AS DATE)) AS HasRes
FROM dbo.Tables t
JOIN dbo.Zones z ON z.Id=t.ZoneId
OUTER APPLY (
    SELECT TOP 1 o.Id AS OrderId, o.Code AS OrderCode, o.OpenedAt, o.CoverCount, o.CreatedBy AS WaiterId
    FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
    WHERE ot.TableId=t.Id AND o.Status=0 AND o.IsDeleted=0
    ORDER BY o.OpenedAt DESC
) oo
WHERE t.IsDeleted=0 AND t.IsActive=1 AND (@b IS NULL OR t.BranchId=@b) AND (@z IS NULL OR t.ZoneId=@z)
ORDER BY z.Name, t.Code;";
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@z", (object?)zoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            bool dolu = !r.IsDBNull(5);
            bool res = !r.IsDBNull(10) && r.GetInt32(10) > 0;
            int? wId = r["WaiterId"] as int?;
            rows.Add(new TableLiveRow
            {
                TableCode = r.GetString(0),
                TableName = r["TableName"] as string ?? "",
                ZoneName = r["ZoneName"] as string ?? "",
                BranchName = branches.TryGetValue(r.GetInt32(3), out var bn) ? bn : "",
                Seats = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                State = dolu ? "Dolu" : res ? "Rezerve" : "Boş",
                OrderCode = dolu ? r.GetString(5) : null,
                OpenedAt = dolu && !r.IsDBNull(6) ? r.GetDateTime(6) : null,
                Covers = dolu && !r.IsDBNull(7) ? r.GetInt32(7) : 0,
                Total = dolu && !r.IsDBNull(9) ? r.GetDecimal(9) : 0,
                WaiterName = dolu && wId is int w && users.TryGetValue(w, out var wn) ? wn : null,
                Info = res && !dolu ? "Bugün rezervasyon var" : null
            });
        }
        return rows;
    }

    /// <summary>Masa geçmiş performansı (tarih aralığı): adisyon/devir, ciro, kişi, ort. fiş, son kullanım. Şube + lokasyon filtreli.</summary>
    public List<TableHistoryRow> GetTableHistory(int? branchId, int? zoneId, DateTime from, DateTime to)
    {
        var rows = new List<TableHistoryRow>();
        var branches = new BranchService().GetByCompany(Helpers.Session.CurrentCompany?.Id ?? 0).ToDictionary(b => b.Id, b => b.Name);
        int cutoff = ResolveCutoff(branchId);
        // Tarih aralığı filtreleri (hangi GÜNE ait) → iş-günü kesimine göre.
        string closed = string.Format(BusinessDayExpr, "o.ClosedAt");
        string openedO = string.Format(BusinessDayExpr, "o.OpenedAt");
        // LastUsed: masanın GERÇEK son kullanım zamanı (görünen değer) → ham yerel, kesim uygulanmaz.
        string closedLocal = string.Format(LocalDateExpr, "o.ClosedAt");
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT t.Code, t.Name AS TableName, z.Name AS ZoneName, t.BranchId, t.Seats,
  (SELECT COUNT(1) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
     WHERE ot.TableId=t.Id AND o.Status=1 AND o.IsDeleted=0 AND CAST({closed} AS DATE) BETWEEN @from AND @to) AS Adisyon,
  (SELECT ISNULL(SUM(f.Total),0) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
     JOIN dbo.ClFiches f ON f.OrderId=o.Id AND f.IsDeleted=0
     WHERE ot.TableId=t.Id AND o.Status=1 AND o.IsDeleted=0 AND CAST({closed} AS DATE) BETWEEN @from AND @to) AS Revenue,
  (SELECT ISNULL(SUM(o.CoverCount),0) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
     WHERE ot.TableId=t.Id AND o.Status=1 AND o.IsDeleted=0 AND CAST({closed} AS DATE) BETWEEN @from AND @to) AS Covers,
  (SELECT COUNT(1) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
     WHERE ot.TableId=t.Id AND o.Status=2 AND CAST({openedO} AS DATE) BETWEEN @from AND @to) AS Cancels,
  (SELECT MAX({closedLocal}) FROM dbo.OrderTables ot JOIN dbo.Orders o ON o.Id=ot.OrderId
     WHERE ot.TableId=t.Id AND o.Status=1 AND o.IsDeleted=0 AND CAST({closed} AS DATE) BETWEEN @from AND @to) AS LastUsed
FROM dbo.Tables t
JOIN dbo.Zones z ON z.Id=t.ZoneId
WHERE t.IsDeleted=0 AND (@b IS NULL OR t.BranchId=@b) AND (@z IS NULL OR t.ZoneId=@z)
ORDER BY Revenue DESC, z.Name, t.Code;";
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@z", (object?)zoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", to.Date);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new TableHistoryRow
            {
                TableCode = r.GetString(0),
                TableName = r["TableName"] as string ?? "",
                ZoneName = r["ZoneName"] as string ?? "",
                BranchName = branches.TryGetValue(r.GetInt32(3), out var bn) ? bn : "",
                Seats = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                AdisyonCount = r.GetInt32(5),
                Revenue = r.GetDecimal(6),
                Covers = r.GetInt32(7),
                CancelCount = r.GetInt32(8),
                LastUsed = r.IsDBNull(9) ? null : r.GetDateTime(9)
            });
        return rows;
    }

    /// <summary>Z (gün sonu) raporları listesi — tarih aralığı + (opsiyonel) şube + (opsiyonel) KIOSKNO filtreli.</summary>
    public List<ZReportRow> GetZReportList(int? branchId, int? kioskNo, DateTime from, DateTime to)
    {
        var rows = new List<ZReportRow>();
        var users = new UserService().GetAll().ToDictionary(u => u.Id, u => u.FullName);
        var branches = new BranchService().GetByCompany(Helpers.Session.CurrentCompany?.Id ?? 0).ToDictionary(b => b.Id, b => b.Name);
        string takenLocal = string.Format(LocalDateExpr, "TakenAt");
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT Id, ReportDate, {takenLocal} AS LocalTaken, BranchId, KioskNo, SalesTotal, FisCount, CreatedBy
FROM dbo.ZReports
WHERE ReportDate BETWEEN @from AND @to
  AND (@b IS NULL OR BranchId=@b)
  AND (@kno IS NULL OR KioskNo=@kno)
ORDER BY ReportDate DESC, BranchId, KioskNo;";
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", to.Date);
        cmd.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kno", (object?)kioskNo ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int bid = r.GetInt32(3);
            int? by = r["CreatedBy"] as int?;
            rows.Add(new ZReportRow
            {
                Id = r.GetInt32(0),
                BranchId = bid,
                ReportDate = r.GetDateTime(1),
                TakenAt = r.GetDateTime(2),
                BranchName = branches.TryGetValue(bid, out var bn) ? bn : $"Şube {bid}",
                KioskNo = r.IsDBNull(4) ? 1 : r.GetInt32(4),
                SalesTotal = r.GetDecimal(5),
                FisCount = r.GetInt32(6),
                TakenByName = by is int b2 && users.TryGetValue(b2, out var un) ? un : "",
            });
        }
        return rows;
    }
}
