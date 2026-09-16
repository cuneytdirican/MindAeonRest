using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>ReportWindow için rapor tablosu üreticileri (ortak ReportTable çıktısı).</summary>
public static class ReportBuilders
{
    public static ReportTable CariEkstre(ReportFilter f)
    {
        if (f.CustomerId is not { } cid)
            throw new InvalidOperationException("Lütfen üstteki CARİ filtresinden bir cari seçin.");

        var cust = new CustomerService().GetById(cid);
        var from = f.From ?? DateTime.Today.AddMonths(-1);
        var to = f.To ?? DateTime.Today;
        var (opening, rows) = new CariHareketService().GetExtract(cid, from, to);

        var t = new ReportTable
        {
            Title = "Cari Hesap Ekstresi",
            Subtitle = $"{cust?.Code} · {cust?.Name}  ·  {from:dd.MM.yyyy} - {to:dd.MM.yyyy}",
            Columns =
            {
                new ReportColumn("Tarih", 130),
                new ReportColumn("Belge Tipi", 140),
                new ReportColumn("Belge No", 100),
                new ReportColumn("Açıklama", 220),
                new ReportColumn("Proje", 80),
                new ReportColumn("Borç", 100, true),
                new ReportColumn("Alacak", 100, true),
                new ReportColumn("Bakiye", 110, true),
                new ReportColumn("Döviz", 60),
            }
        };

        t.Rows.Add(new object?[] { "", "DEVİR", "", "Önceki dönem bakiyesi", "", null, null, opening, "" });
        decimal totDebt = 0, totCredit = 0;
        foreach (var h in rows)
        {
            totDebt += h.Debt; totCredit += h.Credit;
            t.Rows.Add(new object?[] { h.DateText, h.DocTypeText, h.DocNo, h.Description, h.ProjectCode, h.Debt, h.Credit, h.RunningBalance, h.CurrencyCode });
        }
        decimal final = opening + totDebt - totCredit;
        t.Footer = new object?[] { null, "GERÇEK BAKİYE", null, null, null, totDebt, totCredit, final, null };
        return t;
    }

    public static ReportTable BankaBakiye(ReportFilter f)
    {
        var list = new BankMovementService().GetBalances();
        var t = new ReportTable
        {
            Title = "Banka Hesap Bakiyeleri",
            Columns =
            {
                new ReportColumn("Kod", 90),
                new ReportColumn("Hesap", 240),
                new ReportColumn("Banka", 160),
                new ReportColumn("Döviz", 70),
                new ReportColumn("Bakiye", 140, true),
            }
        };
        foreach (var b in list)
            t.Rows.Add(new object?[] { b.Code, b.Name, b.BankName, b.CurrencyCode, b.Balance });
        var bankByCur = list.GroupBy(x => x.CurrencyCode).Select(g => $"{g.Key}: {g.Sum(x => x.Balance):N2}");
        t.Footer = new object?[] { null, $"{list.Count} hesap", null, "TOPLAM", string.Join("  ", bankByCur) };
        return t;
    }

    public static ReportTable StokMiktar(ReportFilter f)
    {
        var list = new StockService().GetBalances(f.BranchId, f.WarehouseId);
        if (!string.IsNullOrWhiteSpace(f.Search))
            list = list.Where(x => (x.Code + " " + x.Name).Contains(f.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var t = new ReportTable
        {
            Title = "Stok Miktar Raporu",
            Subtitle = "Ürün + depo bazlı bakiye (birim çevrimli)",
            Columns =
            {
                new ReportColumn("Kod", 90), new ReportColumn("Ürün", 240), new ReportColumn("Depo", 150),
                new ReportColumn("Miktar", 110, true, "0.###"), new ReportColumn("Birim", 90),
                new ReportColumn("Alt Birim Miktar", 130, true, "0.###"), new ReportColumn("Alt Birim", 90),
            }
        };
        foreach (var b in list)
            t.Rows.Add(new object?[] { b.Code, b.Name, b.WarehouseName, b.Qty, b.MainUnit, b.AltQty, b.AltUnit });
        t.Footer = new object?[] { null, $"{list.Count} kalem", null, null, null, null, null };
        return t;
    }

    public static ReportTable StokHareket(ReportFilter f)
    {
        var list = new StockService().GetMovements(f.From, f.To, f.WarehouseId, f.BranchId);
        if (!string.IsNullOrWhiteSpace(f.Search))
            list = list.Where(x => (x.ProductName ?? "").Contains(f.Search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var t = new ReportTable
        {
            Title = "Stok Hareket Raporu",
            Columns =
            {
                new ReportColumn("Tarih", 140), new ReportColumn("Ürün", 240), new ReportColumn("Depo", 150),
                new ReportColumn("Hareket", 120), new ReportColumn("Miktar", 110, true, "0.###"),
            }
        };
        decimal giris = 0, cikis = 0;
        foreach (var m in list)
        {
            if (m.Qty >= 0) giris += m.Qty; else cikis += m.Qty;
            t.Rows.Add(new object?[] { m.DateText, m.ProductName, m.WarehouseName, m.TypeText, m.Qty });
        }
        t.Footer = new object?[] { null, $"{list.Count} hareket", "Net:", null, giris + cikis };
        return t;
    }

    public static ReportTable Giderler(ReportFilter f)
    {
        var list = new ExpenseService().GetList(f.From, f.To);
        var t = new ReportTable
        {
            Title = "Gider Raporu",
            Columns =
            {
                new ReportColumn("Kod", 90), new ReportColumn("Tarih", 140), new ReportColumn("Gider Tipi", 180),
                new ReportColumn("Personel", 160), new ReportColumn("Ödeme", 80), new ReportColumn("Tutar", 120, true),
            }
        };
        decimal tot = 0;
        foreach (var x in list)
        {
            tot += x.Amount;
            t.Rows.Add(new object?[] { x.Code, x.DateText, x.ExpenseTypeName, x.PersonnelName, x.PayText, x.Amount });
        }
        t.Footer = new object?[] { null, null, $"{list.Count} gider", null, "TOPLAM", tot };
        return t;
    }

    public static ReportTable FaturalanmamisIrsaliye(ReportFilter f)
    {
        var list = new DocumentService().GetUnbilledWaybills(f.From, f.To);
        var t = new ReportTable
        {
            Title = "Faturalanmamış İrsaliyeler",
            Subtitle = "Bir faturaya bağlanmamış onaylı irsaliyeler (Türkiye: 7 gün içinde faturalanmalı)",
            Columns =
            {
                new ReportColumn("Fiş No", 100), new ReportColumn("Tarih", 120), new ReportColumn("Tür", 110),
                new ReportColumn("Cari", 180), new ReportColumn("Tutar (₺)", 110, true),
                new ReportColumn("USD", 100, true), new ReportColumn("EUR", 100, true),
                new ReportColumn("Geçen Gün", 80, true, "0"), new ReportColumn("Durum", 130),
            }
        };
        decimal tot = 0, totUsd = 0, totEur = 0;
        foreach (var d in list)
        {
            int days = (DateTime.Today - d.DocDate.Date).Days;
            decimal? vUsd = d.UsdRate is > 0 ? Math.Round(d.GrandTotal / d.UsdRate.Value, 2) : null;
            decimal? vEur = d.EurRate is > 0 ? Math.Round(d.GrandTotal / d.EurRate.Value, 2) : null;
            tot += d.GrandTotal; totUsd += vUsd ?? 0; totEur += vEur ?? 0;
            t.Rows.Add(new object?[] { d.FisNo, d.DateText, d.DocTypeText, d.CustomerName, d.GrandTotal, vUsd, vEur, days, days > 7 ? "⚠ 7 GÜN GEÇTİ" : "Süresinde" });
        }
        t.Footer = new object?[] { null, null, null, $"{list.Count} irsaliye", tot, totUsd, totEur, null, null };
        return t;
    }

    public static ReportTable PosBakiye(ReportFilter f)
    {
        var list = new PosMovementService().GetBalances();
        var t = new ReportTable
        {
            Title = "POS / Kredi Kartı Bakiyeleri",
            Columns =
            {
                new ReportColumn("Kod", 90), new ReportColumn("POS", 240), new ReportColumn("Banka", 160),
                new ReportColumn("Döviz", 70), new ReportColumn("Bakiye", 140, true),
            }
        };
        foreach (var b in list) t.Rows.Add(new object?[] { b.Code, b.Name, b.BankName, b.CurrencyCode, b.Balance });
        var posByCur = list.GroupBy(x => x.CurrencyCode).Select(g => $"{g.Key}: {g.Sum(x => x.Balance):N2}");
        t.Footer = new object?[] { null, $"{list.Count} POS", null, "TOPLAM", string.Join("  ", posByCur) };
        return t;
    }

    public static ReportTable CekSenetPortfoy(ReportFilter f)
    {
        var list = new CheckNoteService().GetAll();
        var t = new ReportTable
        {
            Title = "Çek / Senet Portföyü",
            Columns =
            {
                new ReportColumn("Tür", 70), new ReportColumn("Yön", 80), new ReportColumn("Cari", 220),
                new ReportColumn("Seri/No", 110), new ReportColumn("Vade", 100), new ReportColumn("Tutar", 120, true),
                new ReportColumn("Durum", 120),
            }
        };
        decimal tot = 0;
        foreach (var n in list)
        {
            tot += n.Amount;
            t.Rows.Add(new object?[] { n.KindText, n.DirectionText, n.CustomerName, n.Serial, n.DueText, n.Amount, n.StatusText });
        }
        t.Footer = new object?[] { null, null, $"{list.Count} adet", null, null, tot, null };
        return t;
    }

    public static ReportTable CariBakiye(ReportFilter f)
    {
        var list = new CariHareketService().GetBalances(f.From, f.To);
        var t = new ReportTable
        {
            Title = "Cari Bakiye Listesi",
            Subtitle = (f.From is { } a ? $"{a:dd.MM.yyyy}" : "tüm") + " - " + (f.To is { } b ? $"{b:dd.MM.yyyy}" : "tüm"),
            Columns =
            {
                new ReportColumn("Kod", 90),
                new ReportColumn("Cari", 240),
                new ReportColumn("Tip", 140),
                new ReportColumn("Döviz", 70),
                new ReportColumn("Borç", 120, true),
                new ReportColumn("Alacak", 120, true),
                new ReportColumn("Bakiye", 130, true),
            }
        };
        foreach (var c in list)
            t.Rows.Add(new object?[] { c.Code, c.Name, c.TypeText, c.CurrencyCode, c.Debt, c.Credit, c.Balance });
        // Döviz bazında toplam (farklı dövizler karıştırılmaz)
        var byCur = list.GroupBy(x => x.CurrencyCode).Select(g => $"{g.Key}: {g.Sum(x => x.Balance):N2}");
        t.Footer = new object?[] { null, $"{list.Count} satır", null, string.Join("  ·  ", byCur), null, null, null };
        return t;
    }
}
