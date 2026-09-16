using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Gerçek adım-adım kurulum sihirbazı: her adımda mevcut listeyi gösterir, TOPLU/akıllı oluşturma sunar, atlanabilir.</summary>
public partial class RestaurantWizardWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly int _companyId;
    private int _index;
    private SectorPreset? _sector;   // başta seçilen sektör → kategori/ürün/lokasyon önerileri

    private readonly (string Title, string Desc, Func<UIElement> Build)[] _steps;

    public RestaurantWizardWindow()
    {
        InitializeComponent();
        _companyId = Session.CurrentCompany?.Id ?? 0;
        _steps = new (string, string, Func<UIElement>)[]
        {
            ("Sektör", "İşletme tipinizi seçin — kategori, ürün, lokasyon ve reyon önerileri buna göre hazır gelir. (Atlayabilirsiniz.)", BuildSector),
            ("Şubeler", "Restoranın şubelerini tanımlayın. Tek şube de olabilir. Toplu ekleyebilir ya da atlayabilirsiniz.", BuildBranches),
            ("Lokasyonlar", "Her şubede Salon, Bahçe, Teras, Bar gibi lokasyonlar. Yaygın olanları seçip topluca ekleyin.", BuildZones),
            ("Reyonlar (Mutfak/Bar)", "Her lokasyona mutfak/bar reyonu. 'Mutfak + Bar' otomatik oluşturulur; yazıcıyı sonra atarsınız.", BuildReyons),
            ("Reyon Yönlendirme", "Kategori hangi reyona düşsün? 🤖 Otomatik: yiyecekler → Mutfak, içecekler → Bar (her lokasyon).", BuildRoutes),
            ("Masalar", "Her lokasyona kaç masa? Otomatik numaralı masaları topluca oluşturun.", BuildTables),
            ("Kategoriler", "Ürün kategorileri (İçecek, Ana Yemek, Tatlı…). Yaygın olanları seçip ekleyin.", BuildCategories),
            ("Ürünler", "Kategori seçip 'Ad;Fiyat' satırlarıyla ürünleri topluca ekleyin (her satır bir ürün).", BuildProducts),
            ("Finans (Otomatik)", "Her şubeye 1 Nakit Kasa + KIOSK #1 tek tıkla oluşturulur.", BuildFinance),
            ("Bitti 🎉", "Kurulum özeti. Eksik kalanları RESTORAN TANIMLARI'ndan tamamlayabilirsiniz.", BuildDone),
        };
        Loaded += (_, _) => ShowStep(0);
    }

    // ---------- Navigasyon ----------
    private void ShowStep(int i)
    {
        _index = Math.Clamp(i, 0, _steps.Length - 1);
        var s = _steps[_index];
        StepTitle.Text = $"{_index + 1}. {s.Title}";
        StepDesc.Text = s.Desc;
        StepHost.Content = s.Build();
        BackButton.Visibility = _index == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = _index == _steps.Length - 1 ? "Bitir ✓" : "İleri ▶";
        SkipButton.Visibility = _index == _steps.Length - 1 ? Visibility.Hidden : Visibility.Visible;
        ProgressBarMain.Value = (_index) * 100.0 / (_steps.Length - 1);
        BuildRail();
    }

    private void BuildRail()
    {
        var fg = (Brush)FindResource("TextBrush");
        var muted = (Brush)FindResource("TextMutedBrush");
        var accent = (Brush)FindResource("AccentBrush");
        var rail = new List<object>();
        for (int i = 0; i < _steps.Length; i++)
        {
            bool cur = i == _index; bool done = i < _index;
            rail.Add(new
            {
                _steps[i].Title,
                RailBadge = done ? "✓" : (i + 1).ToString(),
                RailBrush = cur ? accent : (Brush)FindResource("SurfaceAltBrush"),
                RailFg = cur ? Brushes.White : (done ? fg : muted)
            });
        }
        StepRail.ItemsSource = rail;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index == _steps.Length - 1) { Close(); return; }
        ShowStep(_index + 1);
    }
    private void Skip_Click(object sender, RoutedEventArgs e) => ShowStep(_index + 1);
    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_index - 1);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    // ---------- UI yardımcıları ----------
    private Brush B(string key) => (Brush)FindResource(key);
    private TextBlock H(string t) => new() { Text = t, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 0, 6) };
    private TextBlock Muted(string t) => new() { Text = t, FontSize = 12, Foreground = B("TextMutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private TextBox Tb(string text = "", double w = 70) => new() { Text = text, Width = w, Height = 36, Style = (Style)FindResource("InputTextBox"), VerticalContentAlignment = VerticalAlignment.Center };
    private Button Btn(string t, RoutedEventHandler h, bool primary = true)
    {
        var b = new Button { Content = t, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += h; return b;
    }
    private Border Card(params UIElement[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return new Border { Background = B("SurfaceBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = sp };
    }
    private UIElement ExistingList(IEnumerable<string> items, string emptyText)
    {
        var list = items.ToList();
        if (list.Count == 0) return new TextBlock { Text = emptyText, Foreground = B("TextMutedBrush"), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 0) };
        var wrap = new WrapPanel();
        foreach (var it in list)
            wrap.Children.Add(new Border { Background = B("SurfaceAltBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = it, Foreground = B("TextBrush"), FontSize = 12 } });
        return wrap;
    }
    private ComboBox Combo(IEnumerable<ComboItem> items, int? selected = null)
    {
        var c = new ComboBox { Style = (Style)FindResource("InputComboBox"), ItemContainerStyle = (Style)FindResource("InputComboBoxItem"), SelectedValuePath = "Value", DisplayMemberPath = "Text", Margin = new Thickness(0, 0, 0, 10) };
        c.ItemsSource = items.ToList();
        if (selected is int s) c.SelectedValue = s; else if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }
    private StackPanel Row(params UIElement[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var k in kids) { if (k is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(k); }
        return sp;
    }
    private static bool Dec(string s, out decimal v) => decimal.TryParse((s ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out v);
    private int BulkTry(IEnumerable<Action> acts)
    {
        int ok = 0;
        foreach (var a in acts) { try { a(); ok++; } catch { } }
        return ok;
    }

    // ---------- 1. ŞUBELER ----------
    private UIElement BuildBranches()
    {
        var root = new StackPanel();
        var branches = new BranchService().GetByCompany(_companyId);
        root.Children.Add(Card(H($"Mevcut şubeler ({branches.Count})"), ExistingList(branches.Select(b => b.Name), "Henüz şube yok.")));

        var countTb = Tb("1"); var prefixTb = Tb("Şube", 140);
        var createBtn = Btn("⚡ Topluca oluştur", (_, _) =>
        {
            if (!int.TryParse(countTb.Text.Trim(), out int n) || n <= 0) { Dialog.Warn("Geçerli bir adet girin."); return; }
            string prefix = string.IsNullOrWhiteSpace(prefixTb.Text) ? "Şube" : prefixTb.Text.Trim();
            int existing = branches.Count;
            int ok = BulkTry(Enumerable.Range(1, n).Select(i => (Action)(() =>
                new BranchService().Create(new CompanyBranch { CompanyId = _companyId, Code = CodeHelper.Normalize($"{prefix}{existing + i}"), Name = $"{prefix} {existing + i}", IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} şube oluşturuldu."); ShowStep(_index);
        });
        var detailBtn = Btn("+ Detaylı şube ekle", (_, _) =>
        {
            if (Session.CurrentCompany is null) return;
            new BranchEditWindow(_companyId, Session.CurrentCompany.Name) { Owner = this }.ShowDialog(); ShowStep(_index);
        }, primary: false);

        root.Children.Add(Card(H("Hızlı ekle"), Muted("Kaç şube istersiniz? Otomatik isimlendirilir (örn. 'Şube 1, Şube 2…')."),
            Row(new TextBlock { Text = "Adet:", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, countTb,
                new TextBlock { Text = "Önek:", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, prefixTb, createBtn),
            new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { detailBtn } }));
        return root;
    }

    // ---------- 2. LOKASYONLAR ----------
    private static readonly string[] CommonZones = { "Salon", "Bahçe", "Teras", "Bar", "VIP", "Self-Servis" };
    private UIElement BuildZones()
    {
        var root = new StackPanel();
        var branches = new BranchService().GetByCompany(_companyId);
        if (branches.Count == 0) { root.Children.Add(Card(Muted("Önce en az bir şube ekleyin (önceki adım)."))); return root; }
        var branchCombo = Combo(branches.Select(b => new ComboItem(b.Id, b.Name)));
        var zones = new ZoneService().GetAll();

        var existingHost = new StackPanel();
        void RefreshExisting() { existingHost.Children.Clear(); int bid = branchCombo.SelectedValue as int? ?? 0; existingHost.Children.Add(ExistingList(zones.Where(z => z.BranchId == bid).Select(z => z.Name), "Bu şubede lokasyon yok.")); }
        branchCombo.SelectionChanged += (_, _) => RefreshExisting();

        var zoneNames = _sector?.Zones is { Length: > 0 } sz ? sz : CommonZones;
        var checks = zoneNames.Select(n => new CheckBox { Content = n, IsChecked = _sector != null, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 16, 6), Style = (Style)FindResource("ThemedCheckBox") }).ToList();
        var wrapChecks = new WrapPanel(); foreach (var c in checks) wrapChecks.Children.Add(c);
        var customTb = Tb("", 180);
        var addBtn = Btn("⚡ Seçilenleri ekle", (_, _) =>
        {
            int bid = branchCombo.SelectedValue as int? ?? 0;
            if (bid == 0) { Dialog.Warn("Şube seçin."); return; }
            var names = checks.Where(c => c.IsChecked == true).Select(c => c.Content!.ToString()!).ToList();
            if (!string.IsNullOrWhiteSpace(customTb.Text)) names.AddRange(customTb.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            if (names.Count == 0) { Dialog.Warn("En az bir lokasyon seçin/yazın."); return; }
            int ok = BulkTry(names.Select(nm => (Action)(() =>
                new ZoneService().Save(new Zone { Code = CodeHelper.Normalize($"{nm}{bid}"), Name = nm, BranchId = bid, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} lokasyon eklendi."); ShowStep(_index);
        });

        root.Children.Add(Card(H("Şube"), branchCombo, H("Mevcut lokasyonlar"), existingHost));
        root.Children.Add(Card(H("Yaygın lokasyonlar"), wrapChecks, Row(new TextBlock { Text = "Diğer (virgülle):", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, customTb, addBtn)));
        RefreshExisting();
        return root;
    }

    // ---------- 3. REYONLAR ----------
    private UIElement BuildReyons()
    {
        var root = new StackPanel();
        var zones = new ZoneService().GetAll().Where(z => z.IsActive).ToList();
        if (zones.Count == 0) { root.Children.Add(Card(Muted("Önce lokasyon ekleyin (önceki adım)."))); return root; }
        var zoneCombo = Combo(zones.Select(z => new ComboItem(z.Id, $"{z.Name} ({z.BranchName})")));
        var reyons = new ReyonService().GetAll();

        var existingHost = new StackPanel();
        void RefreshExisting() { existingHost.Children.Clear(); int zid = zoneCombo.SelectedValue as int? ?? 0; existingHost.Children.Add(ExistingList(reyons.Where(r => r.ZoneId == zid).Select(r => r.Name + (string.IsNullOrWhiteSpace(r.PrinterName) ? "" : $" 🖨")), "Bu lokasyonda reyon yok.")); }
        zoneCombo.SelectionChanged += (_, _) => RefreshExisting();

        var autoBtn = Btn("🍳 Mutfak + 🍹 Bar otomatik oluştur", (_, _) =>
        {
            int zid = zoneCombo.SelectedValue as int? ?? 0;
            if (zid == 0) { Dialog.Warn("Lokasyon seçin."); return; }
            int ok = BulkTry(new[] { "Mutfak", "Bar" }.Select(nm => (Action)(() =>
                new ReyonService().Save(new Reyon { Code = CodeHelper.Normalize($"{nm}{zid}"), Name = nm, ZoneId = zid, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} reyon oluşturuldu. Yazıcılarını Reyonlar ekranından atayabilirsiniz."); ShowStep(_index);
        });
        var customTb = Tb("", 180);
        var addBtn = Btn("Ekle", (_, _) =>
        {
            int zid = zoneCombo.SelectedValue as int? ?? 0;
            if (zid == 0 || string.IsNullOrWhiteSpace(customTb.Text)) { Dialog.Warn("Lokasyon + reyon adı girin."); return; }
            int ok = BulkTry(customTb.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(nm => (Action)(() =>
                new ReyonService().Save(new Reyon { Code = CodeHelper.Normalize($"{nm}{zid}"), Name = nm, ZoneId = zid, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} reyon eklendi."); ShowStep(_index);
        }, primary: false);

        root.Children.Add(Card(H("Lokasyon"), zoneCombo, H("Mevcut reyonlar"), existingHost));
        root.Children.Add(Card(H("Otomatik / elle"), new StackPanel { Children = { autoBtn } }, Row(new TextBlock { Text = "Diğer (virgülle):", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, customTb, addBtn)));
        RefreshExisting();
        return root;
    }

    // ---------- 4. REYON YÖNLENDİRME ----------
    private static readonly string[] DrinkWords = { "içecek", "icecek", "bar", "alkol", "kahve", "çay", "cay", "meşrubat", "mesrubat", "kokteyl", "bira", "şarap", "sarap" };
    private UIElement BuildRoutes()
    {
        var root = new StackPanel();
        var routes = new ReyonRouteService().GetAll();
        root.Children.Add(Card(H($"Mevcut kural: {routes.Count}"),
            Muted("🤖 Otomatik kurulum: her lokasyonda, içecek/bar kategorileri o lokasyonun Bar reyonuna; diğer (yiyecek) kategoriler Mutfak reyonuna yönlendirilir. Bar yoksa Mutfak'a düşer.")));

        var autoBtn = Btn("🤖 Otomatik yönlendirmeyi kur", (_, _) =>
        {
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
            Dialog.Success($"{ok} yönlendirme kuralı oluşturuldu. İnce ayarı 'Reyon Yönlendirme' ekranından yapabilirsiniz."); ShowStep(_index);
        });
        root.Children.Add(Card(new StackPanel { Children = { autoBtn } }));
        return root;
    }

    // ---------- 5. MASALAR ----------
    private UIElement BuildTables()
    {
        var root = new StackPanel();
        var zones = new ZoneService().GetAll().Where(z => z.IsActive).ToList();
        if (zones.Count == 0) { root.Children.Add(Card(Muted("Önce lokasyon ekleyin."))); return root; }
        var zoneCombo = Combo(zones.Select(z => new ComboItem(z.Id, $"{z.Name} ({z.BranchName})")));
        var ts = new TableService();
        var countTb = Tb("8"); var seatTb = Tb("4");
        var infoHost = new StackPanel();
        void RefreshInfo() { infoHost.Children.Clear(); int zid = zoneCombo.SelectedValue as int? ?? 0; int c = zid == 0 ? 0 : ts.GetByZone(zid).Count; infoHost.Children.Add(new TextBlock { Text = $"Bu lokasyonda {c} masa var.", Foreground = B("TextMutedBrush") }); }
        zoneCombo.SelectionChanged += (_, _) => RefreshInfo();

        var addBtn = Btn("⚡ Masaları oluştur", (_, _) =>
        {
            int zid = zoneCombo.SelectedValue as int? ?? 0;
            var zone = zones.FirstOrDefault(z => z.Id == zid);
            if (zone is null) { Dialog.Warn("Lokasyon seçin."); return; }
            if (!int.TryParse(countTb.Text.Trim(), out int n) || n <= 0) { Dialog.Warn("Geçerli masa adedi girin."); return; }
            int seats = int.TryParse(seatTb.Text.Trim(), out int sv) && sv > 0 ? sv : 4;
            var existing = ts.GetByZone(zid).ToList();
            int startNo = ts.NextTableNumberForBranch(zone.BranchId);
            var newTables = new List<Table>();
            for (int i = 0; i < n; i++)
            {
                int no = startNo + i;
                newTables.Add(new Table
                {
                    ZoneId = zid, BranchId = zone.BranchId, Code = $"MASA{no}", Name = $"Masa {no}", Seats = seats,
                    Shape = TableShape.Kare, X = 30 + (i % 6) * 110, Y = 30 + (i / 6) * 110, Width = 70, Height = 70, IsActive = true
                });
            }
            try { ts.SaveLayout(zid, zone.BranchId, existing.Concat(newTables), Array.Empty<int>(), Session.CurrentUserId); Dialog.Success($"{n} masa oluşturuldu."); }
            catch (Exception ex) { Dialog.Error(ex.Message); }
            ShowStep(_index);
        });

        root.Children.Add(Card(H("Lokasyon"), zoneCombo, infoHost));
        root.Children.Add(Card(H("Toplu masa oluştur"),
            Row(new TextBlock { Text = "Adet:", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, countTb,
                new TextBlock { Text = "Kişilik:", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, seatTb, addBtn),
            Muted("Masalar MASA1, MASA2… diye otomatik numaralanır; planda düzenleyebilirsiniz (Masa Planı).")));
        RefreshInfo();
        return root;
    }

    // ---------- 6. KATEGORİLER ----------
    private static readonly string[] CommonCats = { "Başlangıçlar", "Çorbalar", "Salatalar", "Ana Yemekler", "Izgaralar", "Pizzalar", "Burgerler", "Tatlılar", "Soğuk İçecekler", "Sıcak İçecekler", "Alkollü İçecekler" };
    private UIElement BuildCategories()
    {
        var root = new StackPanel();
        var cats = new ProductCategoryService().GetAll();
        root.Children.Add(Card(H($"Mevcut kategoriler ({cats.Count})"), ExistingList(cats.Select(c => c.Name), "Henüz kategori yok.")));

        var catNames = _sector?.Categories is { Length: > 0 } sc ? sc : CommonCats;
        var checks = catNames.Select(n => new CheckBox { Content = n, IsChecked = _sector != null, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 16, 6), Style = (Style)FindResource("ThemedCheckBox") }).ToList();
        var wrap = new WrapPanel(); foreach (var c in checks) wrap.Children.Add(c);
        var customTb = Tb("", 220);
        var addBtn = Btn("⚡ Seçilenleri ekle", (_, _) =>
        {
            var names = checks.Where(c => c.IsChecked == true).Select(c => c.Content!.ToString()!).ToList();
            if (!string.IsNullOrWhiteSpace(customTb.Text)) names.AddRange(customTb.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            if (names.Count == 0) { Dialog.Warn("En az bir kategori seçin/yazın."); return; }
            int ok = BulkTry(names.Select(nm => (Action)(() => new ProductCategoryService().Save(new ProductCategory { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} kategori eklendi."); ShowStep(_index);
        });
        root.Children.Add(Card(H("Yaygın kategoriler"), wrap, Row(new TextBlock { Text = "Diğer (virgülle):", Foreground = B("TextBrush"), VerticalAlignment = VerticalAlignment.Center }, customTb, addBtn)));
        return root;
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
    private UIElement BuildProducts()
    {
        var root = new StackPanel();
        var cats = new ProductCategoryService().GetAll().Where(c => c.IsActive).ToList();
        if (cats.Count == 0) { root.Children.Add(Card(Muted("Önce kategori ekleyin (önceki adım)."))); return root; }
        var catCombo = Combo(cats.Select(c => new ComboItem(c.Id, c.Name)));
        int total = new ProductService().GetAll().Count;
        var linesTb = new TextBox
        {
            Style = (Style)FindResource("InputTextBox"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 180, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = "Ör:\nKola;45\nAyran;25\nKöfte;160"
        };
        // Sektör seçiliyse: kategori seçilince o kategorinin örnek ürünlerini otomatik doldur
        void Prefill()
        {
            if (_sector is null || catCombo.SelectedItem is not ComboItem ci) return;
            var match = _sector.Products.FirstOrDefault(kv => string.Equals(kv.Key, ci.Text, StringComparison.OrdinalIgnoreCase));
            if (match.Value is { Length: > 0 } items)
                linesTb.Text = string.Join("\n", items.Select(p => $"{p.name};{p.price:0.##}"));
        }
        catCombo.SelectionChanged += (_, _) => Prefill();
        Prefill();
        var addBtn = Btn("⚡ Ürünleri ekle", (_, _) =>
        {
            int catId = catCombo.SelectedValue as int? ?? 0;
            if (catId == 0) { Dialog.Warn("Kategori seçin."); return; }
            var (unitId, taxId, cur) = EnsureProductDefaults();
            var ps = new ProductService();
            int ok = 0, idx = ps.GetAll().Count;
            foreach (var raw in linesTb.Text.Split('\n'))
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
                    var prices = price > 0 ? new List<ProductPrice> { new() { UnitId = unitId, PriceType = PriceType.Satis, Price = price, CurrencyId = cur, IsTaxIncluded = true, IsActive = true } } : new List<ProductPrice>();
                    ps.Save(p, new[] { catId }, Array.Empty<ProductImage>(), Array.Empty<ProductDescription>(), units, Array.Empty<ProductBarcode>(), prices, Array.Empty<ProductRecipe>(), Array.Empty<ProductBranchConsume>(), Session.CurrentUserId);
                    ok++;
                }
                catch { }
            }
            Dialog.Success($"{ok} ürün eklendi."); ShowStep(_index);
        });
        root.Children.Add(Card(H($"Kategori seç (toplam {total} ürün var)"), catCombo,
            H("Ürünler — her satır 'Ad;Fiyat'"), linesTb, new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { addBtn } }));
        return root;
    }

    // ---------- 8. FİNANS (OTOMATİK) ----------
    private UIElement BuildFinance()
    {
        var root = new StackPanel();
        var branches = new BranchService().GetByCompany(_companyId);
        int cash = new CashRegisterService().GetAll().Count;
        int kiosks = new KioskService().GetAll().Count;
        root.Children.Add(Card(H("Durum"), Muted($"{branches.Count} şube · {cash} kasa · {kiosks} KIOSK tanımlı.")));

        var autoBtn = Btn("⚡ Her şubeye Nakit Kasa + KIOSK #1 oluştur", (_, _) =>
        {
            int cur = new CurrencyService().GetDefaultId() ?? 0;
            var crs = new CashRegisterService(); var ks = new KioskService();
            var existingCash = crs.GetAll();
            int okC = 0, okK = 0;
            foreach (var b in branches)
            {
                if (!existingCash.Any(x => x.BranchId == b.Id))
                    try { crs.Save(new CashRegister { Code = CodeHelper.Normalize($"KASA{b.Id}"), Name = $"{b.Name} Nakit Kasa", BranchId = b.Id, CurrencyId = cur, Scope = FinanceScope.Sube, IsActive = true }, Session.CurrentUserId); okC++; } catch { }
                try { ks.EnsureForBranch(b.Id, 1, Session.CurrentUserId); okK++; } catch { }
            }
            Dialog.Success($"{okC} kasa + {okK} KIOSK hazırlandı."); ShowStep(_index);
        });
        root.Children.Add(Card(new StackPanel { Children = { autoBtn } },
            Muted("POS ve hesap detaylarını ihtiyaç oldukça FİNANS menüsünden ekleyebilirsiniz.")));

        // Türkiye bankaları — işaretle, topluca ekle
        var existBanks = new BankService().GetAll().Select(b => b.Name.ToLower(Tr)).ToHashSet();
        var bankChecks = TurkBanks.Where(n => !existBanks.Contains(n.ToLower(Tr)))
            .Select(n => new CheckBox { Content = n, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 14, 6), Style = (Style)FindResource("ThemedCheckBox") }).ToList();
        var bankWrap = new WrapPanel(); foreach (var c in bankChecks) bankWrap.Children.Add(c);
        var bankBtn = Btn("⚡ Seçili bankaları ekle", (_, _) =>
        {
            var names = bankChecks.Where(c => c.IsChecked == true).Select(c => c.Content!.ToString()!).ToList();
            if (names.Count == 0) { Dialog.Warn("Banka seçin."); return; }
            int ok = BulkTry(names.Select(nm => (Action)(() => new BankService().Save(new Bank { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} banka eklendi."); ShowStep(_index);
        });
        root.Children.Add(Card(H("🏦 Türkiye Bankaları"), Muted("Çalıştığınız bankaları işaretleyin."), bankWrap, new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { bankBtn } }));

        // Yemek çeki şirketleri
        var existVo = new MealVoucherService().GetAll().Select(v => v.Name.ToLower(Tr)).ToHashSet();
        var voChecks = VoucherCos.Where(n => !existVo.Contains(n.ToLower(Tr)))
            .Select(n => new CheckBox { Content = n, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 14, 6), Style = (Style)FindResource("ThemedCheckBox") }).ToList();
        var voWrap = new WrapPanel(); foreach (var c in voChecks) voWrap.Children.Add(c);
        var voBtn = Btn("⚡ Seçili yemek çeklerini ekle", (_, _) =>
        {
            var names = voChecks.Where(c => c.IsChecked == true).Select(c => c.Content!.ToString()!).ToList();
            if (names.Count == 0) { Dialog.Warn("Yemek çeki şirketi seçin."); return; }
            int ok = BulkTry(names.Select(nm => (Action)(() => new MealVoucherService().Save(new MealVoucher { Code = CodeHelper.Normalize(nm), Name = nm, IsActive = true }, Session.CurrentUserId))));
            Dialog.Success($"{ok} yemek çeki şirketi eklendi."); ShowStep(_index);
        });
        root.Children.Add(Card(H("🎫 Yemek Çeki / Kart Şirketleri"), Muted("Kabul ettiğiniz yemek çeki firmalarını işaretleyin."), voWrap, new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { voBtn } }));
        return root;
    }

    // ---------- 9. BİTTİ ----------
    private UIElement BuildDone()
    {
        var root = new StackPanel();
        int br = new BranchService().GetByCompany(_companyId).Count;
        int zo = new ZoneService().GetAll().Count;
        int re = new ReyonService().GetAll().Count;
        int ro = new ReyonRouteService().GetAll().Count;
        int ca = new ProductCategoryService().GetAll().Count;
        int pr = new ProductService().GetAll().Count;
        int ks = new KioskService().GetAll().Count;
        string Line(string n, int c) => $"   {(c > 0 ? "✓" : "○")}  {n}: {c}";
        root.Children.Add(Card(
            H("🎉 Kurulum özeti"),
            new TextBlock
            {
                Foreground = B("TextBrush"), FontSize = 13.5, LineHeight = 24,
                Text = string.Join("\n", new[] {
                    Line("Şube", br), Line("Lokasyon", zo), Line("Reyon", re), Line("Yönlendirme kuralı", ro),
                    Line("Kategori", ca), Line("Ürün", pr), Line("KIOSK", ks)
                })
            },
            Muted("Eksik kalanları sol menüdeki RESTORAN TANIMLARI ya da ilgili ekranlardan tamamlayabilirsiniz. 'Bitir' ile kapatın.")));
        return root;
    }

    // ---------- 0. SEKTÖR ----------
    private UIElement BuildSector()
    {
        var root = new StackPanel();
        if (_sector is not null)
            root.Children.Add(Card(H($"Seçili sektör: {_sector.Icon} {_sector.Name}"),
                Muted($"Öneriler hazır: {_sector.Categories.Length} kategori, {_sector.Zones.Length} lokasyon + örnek ürünler. Sonraki adımlarda işaretli/dolu gelir."),
                new StackPanel { Children = { Btn("Seçimi temizle", (_, _) => { _sector = null; ShowStep(_index); }, primary: false) } }));

        var grid = new WrapPanel();
        foreach (var s in Sectors)
        {
            var preset = s;
            var inner = new StackPanel { Width = 200 };
            inner.Children.Add(new TextBlock { Text = preset.Icon, FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center });
            inner.Children.Add(new TextBlock { Text = preset.Name, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = B("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextAlignment = TextAlignment.Center });
            inner.Children.Add(new TextBlock { Text = string.Join(" · ", preset.Categories.Take(4)), FontSize = 10.5, Foreground = B("TextMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            bool sel = _sector?.Name == preset.Name;
            var card = new Button
            {
                Content = inner, Width = 220, Height = 150, Margin = new Thickness(0, 0, 12, 12), Cursor = Cursors.Hand,
                Background = sel ? B("AccentBrush") : B("SurfaceBrush"),
                BorderBrush = sel ? B("AccentBrush") : B("BorderBrush"), BorderThickness = new Thickness(sel ? 2 : 1, sel ? 2 : 1, sel ? 2 : 1, sel ? 2 : 1)
            };
            card.Click += (_, _) => { _sector = preset; ShowStep(_index); };
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        return root;
    }

    public class SectorPreset
    {
        public string Icon = ""; public string Name = "";
        public string[] Zones = Array.Empty<string>();
        public string[] Categories = Array.Empty<string>();
        public Dictionary<string, (string name, decimal price)[]> Products = new();
    }

    private static readonly List<SectorPreset> Sectors = new()
    {
        new() { Icon = "☕", Name = "Kafe", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Kahveler","Soğuk İçecekler","Tatlılar","Atıştırmalıklar","Sandviçler"},
            Products = new() {
                ["Kahveler"]=new[]{("Espresso",40m),("Latte",55m),("Cappuccino",55m),("Türk Kahvesi",35m),("Filtre Kahve",45m)},
                ["Soğuk İçecekler"]=new[]{("Limonata",45m),("Ice Latte",60m),("Milkshake",70m),("Soda",20m)},
                ["Tatlılar"]=new[]{("Cheesecake",90m),("Brownie",80m),("San Sebastian",95m)},
                ["Sandviçler"]=new[]{("Club Sandviç",120m),("Tost",70m)} } },
        new() { Icon = "🍽️", Name = "Restoran", Zones = new[]{"Salon","Bahçe","Teras"},
            Categories = new[]{"Çorbalar","Başlangıçlar","Salatalar","Ana Yemekler","Izgaralar","Tatlılar","İçecekler"},
            Products = new() {
                ["Çorbalar"]=new[]{("Mercimek Çorbası",55m),("Ezogelin",55m)},
                ["Ana Yemekler"]=new[]{("Karışık Izgara",280m),("Tavuk Sote",180m),("Mantı",160m)},
                ["Izgaralar"]=new[]{("Adana Kebap",240m),("Köfte",200m),("Tavuk Şiş",190m)},
                ["İçecekler"]=new[]{("Kola",45m),("Ayran",25m),("Su",10m)},
                ["Tatlılar"]=new[]{("Künefe",120m),("Sütlaç",70m)} } },
        new() { Icon = "🍔", Name = "Fast Food", Zones = new[]{"Salon","Self-Servis"},
            Categories = new[]{"Burgerler","Menüler","Patates & Atıştırmalık","İçecekler","Tatlılar"},
            Products = new() {
                ["Burgerler"]=new[]{("Cheeseburger",150m),("Double Burger",210m),("Tavuk Burger",140m)},
                ["Menüler"]=new[]{("Burger Menü",210m),("Tavuk Menü",190m)},
                ["Patates & Atıştırmalık"]=new[]{("Patates",60m),("Soğan Halkası",70m),("Nugget",90m)},
                ["İçecekler"]=new[]{("Kola",45m),("Ayran",25m)} } },
        new() { Icon = "🍕", Name = "Pizza / İtalyan", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Pizzalar","Makarnalar","Salatalar","İçecekler","Tatlılar"},
            Products = new() {
                ["Pizzalar"]=new[]{("Margherita",170m),("Karışık",210m),("Sucuklu",190m)},
                ["Makarnalar"]=new[]{("Bolonez",160m),("Alfredo",170m)},
                ["İçecekler"]=new[]{("Kola",45m),("Su",10m)} } },
        new() { Icon = "🍺", Name = "Bar / Beach Club", Zones = new[]{"Plaj","Havuz Başı","Bar","Teras"},
            Categories = new[]{"Biralar","Kokteyller","Alkollü İçecekler","Soğuk İçecekler","Atıştırmalıklar"},
            Products = new() {
                ["Biralar"]=new[]{("Efes",80m),("Tuborg",80m),("Bomonti",90m)},
                ["Kokteyller"]=new[]{("Mojito",180m),("Margarita",190m),("Negroni",200m)},
                ["Alkollü İçecekler"]=new[]{("Rakı (tek)",150m),("Viski (tek)",200m),("Şarap (kadeh)",160m)},
                ["Atıştırmalıklar"]=new[]{("Patates",60m),("Sosis Tabağı",140m),("Meze Tabağı",180m)} } },
        new() { Icon = "🥙", Name = "Kebap / Döner", Zones = new[]{"Salon","Bahçe"},
            Categories = new[]{"Dönerler","Kebaplar","Pideler","Lahmacun","Çorbalar","İçecekler","Tatlılar"},
            Products = new() {
                ["Dönerler"]=new[]{("Et Döner Porsiyon",220m),("Tavuk Döner Porsiyon",170m),("Dürüm",110m)},
                ["Kebaplar"]=new[]{("Adana",240m),("Urfa",240m),("Beyti",260m)},
                ["Pideler"]=new[]{("Kıymalı Pide",140m),("Kaşarlı Pide",150m)},
                ["Lahmacun"]=new[]{("Lahmacun",60m)},
                ["İçecekler"]=new[]{("Ayran",25m),("Şalgam",30m),("Kola",45m)} } },
        new() { Icon = "🐟", Name = "Balık / Meyhane", Zones = new[]{"Salon","Teras"},
            Categories = new[]{"Soğuk Mezeler","Ara Sıcaklar","Balıklar","Salatalar","İçkiler"},
            Products = new() {
                ["Soğuk Mezeler"]=new[]{("Haydari",70m),("Acılı Ezme",70m),("Patlıcan Salata",80m)},
                ["Ara Sıcaklar"]=new[]{("Kalamar",180m),("Sigara Böreği",90m)},
                ["Balıklar"]=new[]{("Levrek",320m),("Çupra",320m),("Hamsi Tava",180m)},
                ["İçkiler"]=new[]{("Rakı (tek)",150m),("Şarap (kadeh)",160m)} } },
        new() { Icon = "🍰", Name = "Pastane / Fırın", Zones = new[]{"Salon"},
            Categories = new[]{"Pastalar","Kurabiyeler","Ekmek & Poğaça","İçecekler"},
            Products = new() {
                ["Pastalar"]=new[]{("Yaş Pasta (dilim)",90m),("Profiterol",85m)},
                ["Ekmek & Poğaça"]=new[]{("Poğaça",25m),("Açma",25m),("Simit",15m)},
                ["İçecekler"]=new[]{("Çay",15m),("Türk Kahvesi",35m)} } },
    };

    // Türkiye bankaları (FİNANS adımında işaretlenip toplu eklenir)
    private static readonly string[] TurkBanks = {
        "Ziraat Bankası","Türkiye İş Bankası","Garanti BBVA","Yapı Kredi","Akbank","Halkbank","VakıfBank",
        "QNB","DenizBank","TEB","ING","HSBC","Şekerbank","Fibabanka","Odeabank","Albaraka Türk",
        "Kuveyt Türk","Türkiye Finans","Ziraat Katılım","Vakıf Katılım","Enpara"
    };
    // Yemek çeki / yemek kartı veren şirketler
    private static readonly string[] VoucherCos = {
        "Multinet Up","Sodexo (Pluxee)","Edenred (Ticket)","SetCard","Metropol","Smart","Paye","Winwin","Tokenflex"
    };
}
