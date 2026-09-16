using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.SetupWizard;

/// <summary>
/// Restoran Kurulum Sihirbazı — WPF RestaurantWizardWindow'un web karşılığı.
/// Tek sayfa, adımlar ?step=N (0..9) ile. WPF'te olduğu gibi her adım kendi Save
/// çağrılarını POST anında yapar ve güncel DB durumunu gösterir (veri kaybı yok, yalnız ekle/oluştur).
/// Adımlar arası tek taşınan durum: seçili sektör (TempData'da JSON, Keep ile korunur).
/// </summary>
public class IndexModel : PageModel
{
    private static readonly CultureInfo Tr = new("tr-TR");

    // Adım tanımı (WPF _steps ile birebir aynı sıra/başlık)
    public static readonly (string Title, string Desc)[] Steps =
    {
        ("Sektör", "İşletme tipinizi seçin — kategori, ürün, lokasyon ve reyon önerileri buna göre hazır gelir. (Atlayabilirsiniz.)"),
        ("Şubeler", "Restoranın şubelerini tanımlayın. Tek şube de olabilir. Toplu ekleyebilir ya da atlayabilirsiniz."),
        ("Lokasyonlar", "Her şubede Salon, Bahçe, Teras, Bar gibi lokasyonlar. Yaygın olanları seçip topluca ekleyin."),
        ("Reyonlar (Mutfak/Bar)", "Her lokasyona mutfak/bar reyonu. 'Mutfak + Bar' otomatik oluşturulur; yazıcıyı sonra atarsınız."),
        ("Reyon Yönlendirme", "Kategori hangi reyona düşsün? Otomatik: yiyecekler → Mutfak, içecekler → Bar (her lokasyon)."),
        ("Masalar", "Her lokasyona kaç masa? Otomatik numaralı masaları topluca oluşturun."),
        ("Kategoriler", "Ürün kategorileri (İçecek, Ana Yemek, Tatlı…). Yaygın olanları seçip ekleyin."),
        ("Ürünler", "Kategori seçip 'Ad;Fiyat' satırlarıyla ürünleri topluca ekleyin (her satır bir ürün)."),
        ("Finans (Otomatik)", "Her şubeye 1 Nakit Kasa + KIOSK #1 tek tıkla oluşturulur. Bankalar/yemek çekleri topluca eklenir."),
        ("Bitti", "Kurulum özeti. Eksik kalanları RESTORAN TANIMLARI'ndan tamamlayabilirsiniz."),
    };

    public const int StepCount = 10;

    [BindProperty(SupportsGet = true)] public int Step { get; set; }

    public string? Info { get; private set; }   // başarı mesajı (alert-success)
    public string? Error { get; private set; }  // hata / uyarı (alert-error)

    // ---- Sektör durumu (TempData'da taşınır) ----
    public SectorPreset? Sector { get; private set; }

    // ---- Adımların gösterim verisi ----
    public List<CompanyBranch> Branches { get; private set; } = new();
    public List<Zone> Zones { get; private set; } = new();
    public List<Reyon> Reyons { get; private set; } = new();
    public List<MindAeonRest.Models.ReyonRoute> Routes { get; private set; } = new();
    public List<ProductCategory> Categories { get; private set; } = new();
    public int ProductCount { get; private set; }
    public int CashCount { get; private set; }
    public int KioskCount { get; private set; }

    // Nav'daki AdminOnly kuralıyla aynı: SysAdmin ya da Patron. (Aksi halde Patron menüde linki görür ama sayfada Forbid yer.)
    private static bool CanAccess => Session.IsTenantAdmin;

    private int CompanyId => Session.CurrentCompany?.Id ?? 0;

    // ---------- GET ----------
    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Step = Math.Clamp(Step, 0, StepCount - 1);
        LoadSector();
        TempData.Keep("wiz_sector");
        PopulateMessages();
        LoadStepData();
        return Page();
    }

    // ---------- Sektör durumu ----------
    private void LoadSector()
    {
        if (TempData.Peek("wiz_sector") is string json && !string.IsNullOrWhiteSpace(json))
        {
            try { Sector = JsonSerializer.Deserialize<SectorPreset>(json); } catch { Sector = null; }
        }
    }

    private void SaveSector(SectorPreset? s)
    {
        if (s is null) TempData.Remove("wiz_sector");
        else TempData["wiz_sector"] = JsonSerializer.Serialize(s);
    }

    private void LoadStepData()
    {
        switch (Step)
        {
            case 1:
                Branches = new BranchService().GetByCompany(CompanyId);
                break;
            case 2:
                Branches = new BranchService().GetByCompany(CompanyId);
                Zones = new ZoneService().GetAll();
                break;
            case 3:
                Zones = new ZoneService().GetAll().Where(z => z.IsActive).ToList();
                Reyons = new ReyonService().GetAll();
                break;
            case 4:
                Routes = new ReyonRouteService().GetAll();
                break;
            case 5:
                Zones = new ZoneService().GetAll().Where(z => z.IsActive).ToList();
                break;
            case 6:
                Categories = new ProductCategoryService().GetAll();
                break;
            case 7:
                Categories = new ProductCategoryService().GetAll().Where(c => c.IsActive).ToList();
                ProductCount = new ProductService().GetAll().Count;
                break;
            case 8:
                Branches = new BranchService().GetByCompany(CompanyId);
                CashCount = new CashRegisterService().GetAll().Count;
                KioskCount = new KioskService().GetAll().Count;
                break;
            case 9:
                Branches = new BranchService().GetByCompany(CompanyId);
                Zones = new ZoneService().GetAll();
                Reyons = new ReyonService().GetAll();
                Routes = new ReyonRouteService().GetAll();
                Categories = new ProductCategoryService().GetAll();
                ProductCount = new ProductService().GetAll().Count;
                KioskCount = new KioskService().GetAll().Count;
                break;
        }
    }

    // ---------- yardımcılar ----------
    private static bool Dec(string? s, out decimal v) =>
        Num.TryParseDecimal(s, out v);

    private static int BulkTry(IEnumerable<Action> acts)
    {
        int ok = 0;
        foreach (var a in acts) { try { a(); ok++; } catch { } }
        return ok;
    }

    // ---------- 0. SEKTÖR ----------
    public IActionResult OnPostChooseSector(string name)
    {
        if (!CanAccess) return Forbid();
        var preset = Sectors.FirstOrDefault(s => s.Name == name);
        SaveSector(preset);
        return RedirectToPage(new { step = 0 });
    }

    public IActionResult OnPostClearSector()
    {
        if (!CanAccess) return Forbid();
        SaveSector(null);
        return RedirectToPage(new { step = 0 });
    }

    // ---------- 1. ŞUBELER ----------
    public IActionResult OnPostCreateBranches(int count, string? prefix)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        if (count <= 0) { TempData["wiz_err"] = "Geçerli bir adet girin."; return RedirectToPage(new { step = 1 }); }
        string px = string.IsNullOrWhiteSpace(prefix) ? "Şube" : prefix.Trim();
        int existing = new BranchService().GetByCompany(CompanyId).Count;
        int ok = BulkTry(Enumerable.Range(1, count).Select(i => (Action)(() =>
            new BranchService().Create(new CompanyBranch
            {
                CompanyId = CompanyId,
                Code = CodeHelper.Normalize($"{px}{existing + i}"),
                Name = $"{px} {existing + i}",
                IsActive = true
            }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} şube oluşturuldu.";
        return RedirectToPage(new { step = 1 });
    }

    // ---------- 2. LOKASYONLAR ----------
    public IActionResult OnPostAddZones(int branchId, string[]? zones, string? custom)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        if (branchId == 0) { TempData["wiz_err"] = "Şube seçin."; return RedirectToPage(new { step = 2 }); }
        var names = (zones ?? Array.Empty<string>()).ToList();
        if (!string.IsNullOrWhiteSpace(custom))
            names.AddRange(custom.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        if (names.Count == 0) { TempData["wiz_err"] = "En az bir lokasyon seçin/yazın."; return RedirectToPage(new { step = 2 }); }
        int ok = BulkTry(names.Select(nm => (Action)(() =>
            new ZoneService().Save(new Zone { Code = CodeHelper.Normalize($"{nm}{branchId}"), Name = nm, BranchId = branchId, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} lokasyon eklendi.";
        return RedirectToPage(new { step = 2 });
    }

    // ---------- 3. REYONLAR ----------
    public IActionResult OnPostAutoReyons(int zoneId)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        if (zoneId == 0) { TempData["wiz_err"] = "Lokasyon seçin."; return RedirectToPage(new { step = 3 }); }
        int ok = BulkTry(new[] { "Mutfak", "Bar" }.Select(nm => (Action)(() =>
            new ReyonService().Save(new Reyon { Code = CodeHelper.Normalize($"{nm}{zoneId}"), Name = nm, ZoneId = zoneId, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} reyon oluşturuldu. Yazıcılarını Reyonlar ekranından atayabilirsiniz.";
        return RedirectToPage(new { step = 3 });
    }

    public IActionResult OnPostAddReyons(int zoneId, string? custom)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        if (zoneId == 0 || string.IsNullOrWhiteSpace(custom)) { TempData["wiz_err"] = "Lokasyon + reyon adı girin."; return RedirectToPage(new { step = 3 }); }
        int ok = BulkTry(custom.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(nm => (Action)(() =>
            new ReyonService().Save(new Reyon { Code = CodeHelper.Normalize($"{nm}{zoneId}"), Name = nm, ZoneId = zoneId, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} reyon eklendi.";
        return RedirectToPage(new { step = 3 });
    }

    // ---------- 4. REYON YÖNLENDİRME ----------
    private static readonly string[] DrinkWords =
        { "içecek", "icecek", "bar", "alkol", "kahve", "çay", "cay", "meşrubat", "mesrubat", "kokteyl", "bira", "şarap", "sarap" };

    public IActionResult OnPostAutoRoutes()
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        var zones = new ZoneService().GetAll().Where(z => z.IsActive).ToList();
        var reyons = new ReyonService().GetAll();
        var cats = new ProductCategoryService().GetAll().Where(c => c.IsActive).ToList();
        var rr = new ReyonRouteService();
        int ok = 0;
        foreach (var z in zones)
        {
            var zr = reyons.Where(r => r.ZoneId == z.Id).ToList();
            var bar = zr.FirstOrDefault(r => r.Name.ToLower(Tr).Contains("bar"));
            var kitchen = zr.FirstOrDefault(r => r.Name.ToLower(Tr).Contains("mutfak")) ?? zr.FirstOrDefault();
            if (kitchen is null) continue;
            foreach (var c in cats)
            {
                bool drink = DrinkWords.Any(w => c.Name.ToLower(Tr).Contains(w));
                int reyonId = (drink && bar is not null) ? bar.Id : kitchen.Id;
                try { rr.Save(new ReyonRoute { SourceZoneId = z.Id, CategoryId = c.Id, ReyonId = reyonId }, Session.CurrentUserId); ok++; } catch { }
            }
            try { rr.Save(new ReyonRoute { SourceZoneId = z.Id, CategoryId = null, ProductId = null, ReyonId = kitchen.Id }, Session.CurrentUserId); } catch { }
        }
        TempData["wiz_ok"] = $"{ok} yönlendirme kuralı oluşturuldu. İnce ayarı 'Reyon Yönlendirme' ekranından yapabilirsiniz.";
        return RedirectToPage(new { step = 4 });
    }

    // ---------- 5. MASALAR ----------
    public IActionResult OnPostAddTables(int zoneId, int count, int seats)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        var zone = new ZoneService().GetAll().FirstOrDefault(z => z.Id == zoneId);
        if (zone is null) { TempData["wiz_err"] = "Lokasyon seçin."; return RedirectToPage(new { step = 5 }); }
        if (count <= 0) { TempData["wiz_err"] = "Geçerli masa adedi girin."; return RedirectToPage(new { step = 5 }); }
        if (seats <= 0) seats = 4;
        var ts = new TableService();
        var existing = ts.GetByZone(zoneId).ToList();
        int startNo = ts.NextTableNumberForBranch(zone.BranchId);
        var newTables = new List<Table>();
        for (int i = 0; i < count; i++)
        {
            int no = startNo + i;
            newTables.Add(new Table
            {
                ZoneId = zoneId, BranchId = zone.BranchId, Code = $"MASA{no}", Name = $"Masa {no}", Seats = seats,
                Shape = TableShape.Kare, X = 30 + (i % 6) * 110, Y = 30 + (i / 6) * 110, Width = 70, Height = 70, IsActive = true
            });
        }
        try
        {
            ts.SaveLayout(zoneId, zone.BranchId, existing.Concat(newTables), Array.Empty<int>(), Session.CurrentUserId);
            TempData["wiz_ok"] = $"{count} masa oluşturuldu.";
        }
        catch (Exception ex) { TempData["wiz_err"] = ex.Message; }
        return RedirectToPage(new { step = 5 });
    }

    // ---------- 6. KATEGORİLER ----------
    public IActionResult OnPostAddCategories(string[]? cats, string? custom)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        var names = (cats ?? Array.Empty<string>()).ToList();
        if (!string.IsNullOrWhiteSpace(custom))
            names.AddRange(custom.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        if (names.Count == 0) { TempData["wiz_err"] = "En az bir kategori seçin/yazın."; return RedirectToPage(new { step = 6 }); }
        int ok = BulkTry(names.Select(nm => (Action)(() =>
            new ProductCategoryService().Save(new ProductCategory { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} kategori eklendi.";
        return RedirectToPage(new { step = 6 });
    }

    // ---------- 7. ÜRÜNLER ----------
    private (int unitId, int? taxId, int cur) EnsureProductDefaults()
    {
        int cur = new CurrencyService().GetDefaultId() ?? 0;
        var units = new MainUnitService().GetAll();
        int unitId = units.FirstOrDefault(u => u.Name.ToLower(Tr).Contains("adet"))?.Id ?? units.FirstOrDefault()?.Id ?? 0;
        if (unitId == 0) unitId = new MainUnitService().Create("AD", "Adet", Session.CurrentUserId);
        var taxes = new TaxGroupService().GetAll();
        int? taxId = taxes.FirstOrDefault()?.Id;
        return (unitId, taxId, cur);
    }

    public IActionResult OnPostAddProducts(int catId, string? lines)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        if (catId == 0) { TempData["wiz_err"] = "Kategori seçin."; return RedirectToPage(new { step = 7 }); }
        var (unitId, taxId, cur) = EnsureProductDefaults();
        var ps = new ProductService();
        int ok = 0, idx = ps.GetAll().Count;
        foreach (var raw in (lines ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Ör")) continue;
            var parts = line.Split(';', ',');
            string name = parts[0].Trim();
            if (name.Length == 0) continue;
            decimal price = parts.Length > 1 && Dec(parts[1], out var pr) ? pr : 0;
            try
            {
                var p = new Product { Code = CodeHelper.Normalize($"URN{++idx}"), Name = name, MainUnitId = unitId, SalesTaxGroupId = taxId, TrackingType = TrackingType.Yok, IsActive = true };
                var units = new List<ProductUnit> { new() { UnitId = unitId, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 } };
                var prices = price > 0
                    ? new List<ProductPrice> { new() { UnitId = unitId, PriceType = PriceType.Satis, Price = price, CurrencyId = cur, IsTaxIncluded = true, IsActive = true } }
                    : new List<ProductPrice>();
                ps.Save(p, new[] { catId }, Array.Empty<ProductImage>(), Array.Empty<ProductDescription>(), units,
                    Array.Empty<ProductBarcode>(), prices, Array.Empty<ProductRecipe>(), Array.Empty<ProductBranchConsume>(), Session.CurrentUserId);
                ok++;
            }
            catch { }
        }
        TempData["wiz_ok"] = $"{ok} ürün eklendi.";
        return RedirectToPage(new { step = 7 });
    }

    // ---------- 8. FİNANS ----------
    public IActionResult OnPostAutoFinance()
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        int cur = new CurrencyService().GetDefaultId() ?? 0;
        var branches = new BranchService().GetByCompany(CompanyId);
        var crs = new CashRegisterService(); var ks = new KioskService();
        var existingCash = crs.GetAll();
        int okC = 0, okK = 0;
        foreach (var b in branches)
        {
            if (!existingCash.Any(x => x.BranchId == b.Id))
                try { crs.Save(new CashRegister { Code = CodeHelper.Normalize($"KASA{b.Id}"), Name = $"{b.Name} Nakit Kasa", BranchId = b.Id, CurrencyId = cur, Scope = FinanceScope.Sube, IsActive = true }, Session.CurrentUserId); okC++; } catch { }
            try { ks.EnsureForBranch(b.Id, 1, Session.CurrentUserId); okK++; } catch { }
        }
        TempData["wiz_ok"] = $"{okC} kasa + {okK} KIOSK hazırlandı.";
        return RedirectToPage(new { step = 8 });
    }

    public IActionResult OnPostAddBanks(string[]? banks)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        var names = (banks ?? Array.Empty<string>()).ToList();
        if (names.Count == 0) { TempData["wiz_err"] = "Banka seçin."; return RedirectToPage(new { step = 8 }); }
        int ok = BulkTry(names.Select(nm => (Action)(() =>
            new BankService().Save(new Bank { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} banka eklendi.";
        return RedirectToPage(new { step = 8 });
    }

    public IActionResult OnPostAddVouchers(string[]? vouchers)
    {
        if (!CanAccess) return Forbid();
        TempData.Keep("wiz_sector");
        var names = (vouchers ?? Array.Empty<string>()).ToList();
        if (names.Count == 0) { TempData["wiz_err"] = "Yemek çeki şirketi seçin."; return RedirectToPage(new { step = 8 }); }
        int ok = BulkTry(names.Select(nm => (Action)(() =>
            new MealVoucherService().Save(new MealVoucher { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
        TempData["wiz_ok"] = $"{ok} yemek çeki şirketi eklendi.";
        return RedirectToPage(new { step = 8 });
    }

    // ================== SABİT VERİLER (WPF ile birebir) ==================

    public static readonly string[] CommonZones = { "Salon", "Bahçe", "Teras", "Bar", "VIP", "Self-Servis" };

    public static readonly string[] CommonCats =
    {
        "Başlangıçlar", "Çorbalar", "Salatalar", "Ana Yemekler", "Izgaralar", "Pizzalar",
        "Burgerler", "Tatlılar", "Soğuk İçecekler", "Sıcak İçecekler", "Alkollü İçecekler"
    };

    public static readonly string[] TurkBanks =
    {
        "Ziraat Bankası","Türkiye İş Bankası","Garanti BBVA","Yapı Kredi","Akbank","Halkbank","VakıfBank",
        "QNB","DenizBank","TEB","ING","HSBC","Şekerbank","Fibabanka","Odeabank","Albaraka Türk",
        "Kuveyt Türk","Türkiye Finans","Ziraat Katılım","Vakıf Katılım","Enpara"
    };

    public static readonly string[] VoucherCos =
    {
        "Multinet Up","Sodexo (Pluxee)","Edenred (Ticket)","SetCard","Metropol","Smart","Paye","Winwin","Tokenflex"
    };

    /// <summary>Sektör şablonu — WPF SectorPreset ile aynı. JSON serialize edilebilir (Products list of tuple → SectorProduct).</summary>
    public class SectorPreset
    {
        public string Icon { get; set; } = "";
        public string Name { get; set; } = "";
        public string[] Zones { get; set; } = Array.Empty<string>();
        public string[] Categories { get; set; } = Array.Empty<string>();
        public Dictionary<string, List<SectorProduct>> Products { get; set; } = new();
    }

    public class SectorProduct
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public SectorProduct() { }
        public SectorProduct(string name, decimal price) { Name = name; Price = price; }
    }

    private static Dictionary<string, List<SectorProduct>> P(params (string cat, (string n, decimal p)[] items)[] groups)
        => groups.ToDictionary(g => g.cat, g => g.items.Select(x => new SectorProduct(x.n, x.p)).ToList());

    public static readonly List<SectorPreset> Sectors = new()
    {
        new() { Icon = "☕", Name = "Kafe", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Kahveler","Soğuk İçecekler","Tatlılar","Atıştırmalıklar","Sandviçler"},
            Products = P(
                ("Kahveler", new[]{("Espresso",40m),("Latte",55m),("Cappuccino",55m),("Türk Kahvesi",35m),("Filtre Kahve",45m)}),
                ("Soğuk İçecekler", new[]{("Limonata",45m),("Ice Latte",60m),("Milkshake",70m),("Soda",20m)}),
                ("Tatlılar", new[]{("Cheesecake",90m),("Brownie",80m),("San Sebastian",95m)}),
                ("Sandviçler", new[]{("Club Sandviç",120m),("Tost",70m)})) },
        new() { Icon = "🍽️", Name = "Restoran", Zones = new[]{"Salon","Bahçe","Teras"},
            Categories = new[]{"Çorbalar","Başlangıçlar","Salatalar","Ana Yemekler","Izgaralar","Tatlılar","İçecekler"},
            Products = P(
                ("Çorbalar", new[]{("Mercimek Çorbası",55m),("Ezogelin",55m)}),
                ("Ana Yemekler", new[]{("Karışık Izgara",280m),("Tavuk Sote",180m),("Mantı",160m)}),
                ("Izgaralar", new[]{("Adana Kebap",240m),("Köfte",200m),("Tavuk Şiş",190m)}),
                ("İçecekler", new[]{("Kola",45m),("Ayran",25m),("Su",10m)}),
                ("Tatlılar", new[]{("Künefe",120m),("Sütlaç",70m)})) },
        new() { Icon = "🍔", Name = "Fast Food", Zones = new[]{"Salon","Self-Servis"},
            Categories = new[]{"Burgerler","Menüler","Patates & Atıştırmalık","İçecekler","Tatlılar"},
            Products = P(
                ("Burgerler", new[]{("Cheeseburger",150m),("Double Burger",210m),("Tavuk Burger",140m)}),
                ("Menüler", new[]{("Burger Menü",210m),("Tavuk Menü",190m)}),
                ("Patates & Atıştırmalık", new[]{("Patates",60m),("Soğan Halkası",70m),("Nugget",90m)}),
                ("İçecekler", new[]{("Kola",45m),("Ayran",25m)})) },
        new() { Icon = "🍕", Name = "Pizza / İtalyan", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Pizzalar","Makarnalar","Salatalar","İçecekler","Tatlılar"},
            Products = P(
                ("Pizzalar", new[]{("Margherita",170m),("Karışık",210m),("Sucuklu",190m)}),
                ("Makarnalar", new[]{("Bolonez",160m),("Alfredo",170m)}),
                ("İçecekler", new[]{("Kola",45m),("Su",10m)})) },
        new() { Icon = "🍺", Name = "Bar / Beach Club", Zones = new[]{"Plaj","Havuz Başı","Bar","Teras"},
            Categories = new[]{"Biralar","Kokteyller","Alkollü İçecekler","Soğuk İçecekler","Atıştırmalıklar"},
            Products = P(
                ("Biralar", new[]{("Efes",80m),("Tuborg",80m),("Bomonti",90m)}),
                ("Kokteyller", new[]{("Mojito",180m),("Margarita",190m),("Negroni",200m)}),
                ("Alkollü İçecekler", new[]{("Rakı (tek)",150m),("Viski (tek)",200m),("Şarap (kadeh)",160m)}),
                ("Atıştırmalıklar", new[]{("Patates",60m),("Sosis Tabağı",140m),("Meze Tabağı",180m)})) },
        new() { Icon = "🥙", Name = "Kebap / Döner", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Dönerler","Kebaplar","Pideler","Lahmacun","Çorbalar","İçecekler","Tatlılar"},
            Products = P(
                ("Dönerler", new[]{("Et Döner Porsiyon",220m),("Tavuk Döner Porsiyon",170m),("Dürüm",110m)}),
                ("Kebaplar", new[]{("Adana",240m),("Urfa",240m),("Beyti",260m)}),
                ("Pideler", new[]{("Kıymalı Pide",140m),("Kaşarlı Pide",150m)}),
                ("Lahmacun", new[]{("Lahmacun",60m)}),
                ("İçecekler", new[]{("Ayran",25m),("Şalgam",30m),("Kola",45m)})) },
        new() { Icon = "🐟", Name = "Balık / Meyhane", Zones = new[]{"Salon","Teras"},
            Categories = new[]{"Soğuk Mezeler","Ara Sıcaklar","Balıklar","Salatalar","İçkiler"},
            Products = P(
                ("Soğuk Mezeler", new[]{("Haydari",70m),("Acılı Ezme",70m),("Patlıcan Salata",80m)}),
                ("Ara Sıcaklar", new[]{("Kalamar",180m),("Sigara Böreği",90m)}),
                ("Balıklar", new[]{("Levrek",320m),("Çupra",320m),("Hamsi Tava",180m)}),
                ("İçkiler", new[]{("Rakı (tek)",150m),("Şarap (kadeh)",160m)})) },
        new() { Icon = "🍰", Name = "Pastane / Fırın", Zones = new[]{"Salon"},
            Categories = new[]{"Pastalar","Kurabiyeler","Ekmek & Poğaça","İçecekler"},
            Products = P(
                ("Pastalar", new[]{("Yaş Pasta (dilim)",90m),("Profiterol",85m)}),
                ("Ekmek & Poğaça", new[]{("Poğaça",25m),("Açma",25m),("Simit",15m)}),
                ("İçecekler", new[]{("Çay",15m),("Türk Kahvesi",35m)})) },
    };

    // Sektör örnek ürünlerini "Ad;Fiyat" satır metnine çevirir (adım 7 ön-doldurma).
    public string ProductsTextForCategory(string categoryName)
    {
        if (Sector is null) return "";
        var match = Sector.Products.FirstOrDefault(kv => string.Equals(kv.Key, categoryName, StringComparison.OrdinalIgnoreCase));
        if (match.Value is { Count: > 0 } items)
            return string.Join("\n", items.Select(p => $"{p.Name};{p.Price.ToString("0.##", Tr)}"));
        return "";
    }

    // GET sonrası mesajları TempData'dan alma (view Info/Error için)
    private void PopulateMessages()
    {
        Info = TempData["wiz_ok"] as string;
        Error = TempData["wiz_err"] as string;
    }
}
