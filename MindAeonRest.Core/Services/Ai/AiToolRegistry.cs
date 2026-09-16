using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Services.Ai;

/// <summary>AI Hub'ın çağırabileceği DETERMİNİSTİK araçlar (mevcut servisleri kullanır, LLM olmadan da çalışır).
/// Hepsi SALT-OKUMA: veri değiştirmez, yalnız sorgular/öneri üretir.</summary>
public class AiToolRegistry
{
    public List<AiTool> Tools { get; }

    private int Branch => Session.CurrentBranch?.Id ?? 0;

    public AiToolRegistry()
    {
        Tools = new List<AiTool>
        {
            new() { Name = "top_products", Description = "Bugünün en çok satan ürünleri", Keywords = new[] { "en çok satan", "çok satılan", "top ürün", "popüler" }, Run = _ => TopProducts() },
            new() { Name = "daily_summary", Description = "Bugünün satış/ciro özeti", Keywords = new[] { "özet", "günsonu", "gün sonu", "ciro", "bugün ne", "durum" }, Run = _ => DailySummary() },
            new() { Name = "customer_balance", Description = "Bir carinin bakiyesi (borç/alacak)", Keywords = new[] { "bakiye", "borç", "borc", "alacak", "hesabı", "ne kadar borçlu" }, Run = CustomerBalance },
            new() { Name = "receivables", Description = "Toplam alacak ve en borçlu cariler", Keywords = new[] { "toplam alacak", "tahsil", "alacaklar", "kim borçlu", "borçlu cari" }, Run = _ => Receivables() },
            new() { Name = "stock_critical", Description = "Tükenmiş/negatif stok ürünleri", Keywords = new[] { "kritik stok", "biten stok", "tükenen", "stok bitti", "stoğu az", "eksi stok" }, Run = _ => StockCritical() },
            new() { Name = "stock_qty", Description = "Bir ürünün stok miktarı", Keywords = new[] { "stok", "kaç adet", "ne kadar var", "elimde" }, Run = StockQty },
            new() { Name = "cash_position", Description = "Kasa ve banka bakiyeleri (nakit pozisyon)", Keywords = new[] { "kasa", "banka", "nakit", "para durumu", "pozisyon" }, Run = _ => CashPosition() },
            new() { Name = "unbilled_waybills", Description = "Faturalanmamış irsaliyeler (7 gün uyarısı)", Keywords = new[] { "faturalanmamış", "irsaliye", "faturası gelmeyen" }, Run = _ => Unbilled() },
            new() { Name = "menu_engineering", Description = "Menü analizi: yıldız/köpek ürünler (katkı marjı + popülerlik)", Keywords = new[] { "menü analiz", "menu analiz", "yıldız ürün", "köpek ürün", "menü mühendis", "katkı marj", "hangi ürün kazandır", "kârlı ürün", "karli urun" }, Run = _ => MenuEngineering() },
            new() { Name = "product_margin", Description = "Bir ürünün maliyeti ve kâr marjı", Keywords = new[] { "marj", "kâr", "kar marj", "maliyeti ne", "kaç kazan", "ne kadar kâr", "ürün maliyet" }, Run = ProductMargin },
            new() { Name = "sales_forecast", Description = "Talep tahmini: ertesi gün / hafta ciro tahmini", Keywords = new[] { "tahmin", "yarın ne kadar", "gelecek hafta", "beklenen ciro", "forecast", "öngörü", "ongoru" }, Run = _ => SalesForecast() },
        };
    }

    // ---- araçlar ----
    private AiAnswer TopProducts()
    {
        var d = new DashboardService().Get(Branch, DateTime.Today);
        if (d.TopProducts.Count == 0) return new AiAnswer("Bugün için satış kaydı bulunamadı.");
        var t = new ReportTable { Title = "Bugünün En Çok Satanları", Columns = { new("Ürün", 220), new("Tutar", 120, true), new("Pay", 80) } };
        foreach (var p in d.TopProducts) t.Rows.Add(new object?[] { p.Name, p.Amount, p.PercentText });
        return new AiAnswer($"Bugün en çok satan {d.TopProducts.Count} ürün listede. Lideri: {d.TopProducts[0].Name}.", t);
    }

    private AiAnswer DailySummary()
    {
        var d = new DashboardService().Get(Branch, DateTime.Today);
        string txt = $"Bugün: {d.SalesText} ciro, {d.AdisyonCount} adisyon (ort. {d.AvgText}). " +
                     $"Nakit {d.NakitText}, Kart {d.KartText}, Diğer {d.DigerText}. Kasa {d.KasaText}. " +
                     $"Toplam alacak {d.ReceivableText}, borç {d.PayableText}, bugünkü gider {d.TodayExpenseText}.";
        return new AiAnswer(txt);
    }

    private AiAnswer CustomerBalance(string q)
    {
        var all = new CariHareketService().GetBalances();
        var matches = all.Where(b => Contains(q, b.Name) || Contains(q, b.Code)).ToList();
        if (matches.Count == 0) return new AiAnswer("Belirttiğiniz cari bulunamadı veya hareketi yok. Cari adını net yazmayı deneyin.");
        var t = new ReportTable { Title = "Cari Bakiye", Columns = { new("Cari", 220), new("Döviz", 70), new("Borç", 110, true), new("Alacak", 110, true), new("Bakiye", 120, true) } };
        foreach (var b in matches) t.Rows.Add(new object?[] { b.Name, b.CurrencyCode, b.Debt, b.Credit, b.Balance });
        var first = matches[0];
        string yon = first.Balance > 0 ? "bize borçlu" : first.Balance < 0 ? "bizden alacaklı" : "bakiyesi sıfır";
        return new AiAnswer($"{first.Name}: {Math.Abs(first.Balance):N2} {first.CurrencyCode} {yon}.", t);
    }

    private AiAnswer Receivables()
    {
        int def = new CurrencyService().GetDefaultId() ?? 0;
        var pos = new CariHareketService().GetBalances().Where(b => b.CurrencyId == def && b.Balance > 0).OrderByDescending(b => b.Balance).ToList();
        if (pos.Count == 0) return new AiAnswer("Tahsil edilecek (bize borçlu) cari yok.");
        var t = new ReportTable { Title = "Bize Borçlu Cariler", Columns = { new("Cari", 240), new("Bakiye", 130, true) } };
        foreach (var b in pos.Take(20)) t.Rows.Add(new object?[] { b.Name, b.Balance });
        return new AiAnswer($"Toplam alacak {pos.Sum(b => b.Balance):N2}. En borçlu: {pos[0].Name} ({pos[0].Balance:N2}).", t);
    }

    private AiAnswer StockCritical()
    {
        var bals = new StockService().GetBalances(Branch);
        var crit = bals.Where(b => b.Qty <= 0).ToList();
        if (crit.Count == 0) return new AiAnswer("Tükenmiş/negatif stoklu ürün yok. 👍");
        var t = new ReportTable { Title = "Kritik (Tükenen) Stoklar", Columns = { new("Kod", 90), new("Ürün", 220), new("Depo", 150), new("Miktar", 110, true, "0.###") } };
        foreach (var b in crit) t.Rows.Add(new object?[] { b.Code, b.Name, b.WarehouseName, b.Qty });
        return new AiAnswer($"{crit.Count} ürün tükenmiş/negatif stokta.", t);
    }

    private AiAnswer StockQty(string q)
    {
        var bals = new StockService().GetBalances(Branch);
        var matches = bals.Where(b => Contains(q, b.Name) || Contains(q, b.Code)).ToList();
        if (matches.Count == 0) return new AiAnswer("Belirttiğiniz ürün için stok hareketi bulunamadı.");
        var t = new ReportTable { Title = "Stok Miktarı", Columns = { new("Ürün", 220), new("Depo", 150), new("Miktar", 110, true, "0.###"), new("Birim", 90) } };
        foreach (var b in matches) t.Rows.Add(new object?[] { b.Name, b.WarehouseName, b.Qty, b.MainUnit });
        return new AiAnswer($"{matches[0].Name}: {matches[0].Qty:0.##} {matches[0].MainUnit}.", t);
    }

    private AiAnswer CashPosition()
    {
        var kasa = new CashMovementService().GetBalances(Branch);
        var banka = new BankMovementService().GetBalances();
        var t = new ReportTable { Title = "Nakit Pozisyon", Columns = { new("Hesap", 240), new("Tür", 90), new("Döviz", 70), new("Bakiye", 130, true) } };
        foreach (var k in kasa) t.Rows.Add(new object?[] { k.Name, "Kasa", k.CurrencyCode, k.Balance });
        foreach (var b in banka) t.Rows.Add(new object?[] { b.Name, "Banka", b.CurrencyCode, b.Balance });
        decimal kt = kasa.Sum(x => x.Balance), bt = banka.Sum(x => x.Balance);
        return new AiAnswer($"Kasa toplam {kt:N2}, banka toplam {bt:N2}. (Dövizler ayrı tabloda.)", t);
    }

    private AiAnswer Unbilled()
    {
        var t = ReportBuilders.FaturalanmamisIrsaliye(new ReportFilter { From = DateTime.Today.AddMonths(-2), To = DateTime.Today });
        return new AiAnswer($"{t.Rows.Count} faturalanmamış irsaliye var (7 günü geçenler kırmızı).", t);
    }

    // ---- FAZ 3a: menü mühendisliği + tahmin araçları (salt-okuma) ----
    private AiAnswer MenuEngineering()
    {
        var to = DateTime.Today; var from = to.AddDays(-29);
        var rep = new MenuEngineeringService().GetMenuEngineering(from, to, Branch);
        if (rep.Items.Count == 0) return new AiAnswer("Son 30 günde satış kaydı bulunamadı (menü analizi için veri yok).");
        var t = new ReportTable { Title = "Menü Analizi (son 30 gün)", Columns = { new("Ürün", 200), new("Adet", 80, true, "0.##"), new("Katkı Marjı", 120, true), new("Marj %", 80, true, "N1"), new("Sınıf", 90) } };
        foreach (var i in rep.Items.Take(15))
            t.Rows.Add(new object?[] { i.Name, i.Qty, i.ContributionMargin, i.MarginPercent, i.ClassLabel });
        var stars = rep.Items.Where(x => x.MenuClass == MindAeonRest.Models.MenuClass.Star).Select(x => x.Name).Take(3).ToList();
        var dogs = rep.Items.Where(x => x.MenuClass == MindAeonRest.Models.MenuClass.Dog).Select(x => x.Name).Take(3).ToList();
        string txt = $"Son 30 gün: {rep.StarCount} yıldız, {rep.PlowhorseCount} inek, {rep.PuzzleCount} bilmece, {rep.DogCount} köpek ürün. Toplam katkı marjı ₺{rep.TotalMargin:N2}.";
        if (stars.Count > 0) txt += " Yıldızlar: " + string.Join(", ", stars) + ".";
        if (dogs.Count > 0) txt += " Gözden geçir (köpek): " + string.Join(", ", dogs) + ".";
        return new AiAnswer(txt, t);
    }

    private AiAnswer ProductMargin(string q)
    {
        var prods = new ProductService().GetAll();
        var match = prods.FirstOrDefault(p => Contains(q, p.Name) || Contains(q, p.Code));
        if (match is null) return new AiAnswer("Belirttiğiniz ürün bulunamadı. Ürün adını net yazmayı deneyin.");
        var prices = new ProductService().GetGeneralSalePrices();
        decimal salePrice = prices.TryGetValue(match.Id, out var pp) ? pp.Price : 0m;
        var m = new CostService().GetMarginInfo(match.Id, salePrice);
        var t = new ReportTable { Title = "Ürün Marjı", Columns = { new("Ürün", 220), new("Maliyet", 110, true), new("Satış", 110, true), new("Marj", 110, true), new("Marj %", 90, true, "N1") } };
        t.Rows.Add(new object?[] { match.Name, m.Cost, m.SalePrice, m.MarginAmount, m.MarginPercent });
        string note = salePrice == 0 ? " (Genel satış fiyatı tanımlı değil.)" : "";
        return new AiAnswer($"{match.Name}: maliyet ₺{m.Cost:N2}, satış ₺{m.SalePrice:N2}, marj ₺{m.MarginAmount:N2} (%{m.MarginPercent:N1}).{note}", t);
    }

    private AiAnswer SalesForecast()
    {
        var fc = new MenuEngineeringService().GetForecast(Branch);
        var t = new ReportTable { Title = "Ciro Tahmini (önümüzdeki 7 gün)", Columns = { new("Gün", 120), new("Tarih", 110), new("Tahmini Ciro", 130, true) } };
        foreach (var p in fc.Next7Days) t.Rows.Add(new object?[] { p.DayName, p.Date.ToString("dd.MM"), p.Revenue });
        return new AiAnswer($"Ertesi gün tahmini ciro yaklaşık {fc.NextDayText}; önümüzdeki 7 gün toplamı ~{fc.Next7Text} (son {fc.LookbackDays} güne göre, ort. günlük ₺{fc.AvgDailyRevenue:N0}).", t);
    }

    private static bool Contains(string haystack, string? needle)
        => !string.IsNullOrWhiteSpace(needle) && needle.Length >= 2 && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
