using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>V2 Garson Terminali: masalar + menü/adisyon + sipariş teslim (mutfak) + meşrubatlar (bar) + yeni servis.
/// Tema-bağımsız (DynamicResource) + ekran içi tema değiştirme. KdsWindowV2/PaymentWindowV2 desenleri.</summary>
public partial class WaiterTerminalV2Window : Window
{
    private readonly ZoneService _zones = new();
    private readonly TableBoardService _board = new();
    private readonly OrderService _orders = new();
    private readonly PosCatalogService _catalog = new();
    private readonly KdsService _kds = new();
    private readonly ServiceRequestService _requests = new();
    private readonly CustomerService _customers = new();
    private readonly CampaignService _campaigns = new();

    private readonly int _branchId;
    private DispatcherTimer? _timer;

    private string _tab = "tables";        // tables | deliver | beverage | service
    private string _tableFilter = "all";   // all | zone:<id> | full | order | reserve | empty | late

    private Order? _order;                  // seçili masa adisyonu
    private int _zoneId;
    private List<PosProduct> _allProducts = new();
    private int _categoryFilter;            // 0 = Tümü
    private List<ProductCategory> _categories = new();
    private List<Campaign> _activeCampaigns = new();
    private readonly HashSet<int> _expandedTables = new();   // inline-expand durumu (TableId) — yenilemeler arası korunur

    // Sol canlı adisyon: kişiye (SeatText) göre gruplanmış satırlar
    private readonly ObservableCollection<LiveLineVM> _liveLines = new();
    private bool _liveGrouped;
    private int _lastAddedLineId;           // son eklenen satır → kısa highlight (IsNew)
    private readonly ObservableCollection<AdCampaignVM> _campaignVMs = new();
    private bool _campaignsLoaded;
    private bool _autoApplying;

    private const int LateMinutes = 90;

    public WaiterTerminalV2Window()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        CompanyText.Text = Session.CurrentCompany?.Name ?? "MindAeonRest";
        BranchText.Text = Session.CurrentBranch?.Name ?? "Şube";
        UserText.Text = Session.CurrentUser?.FullName ?? "";
        RoleText.Text = Session.CurrentUser?.RoleName ?? "";

        Loaded += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            NoteChipHost.ItemsSource = NotePresets.General;
            AllergyChipHost.ItemsSource = NotePresets.Allergy;
            BuildTabs();
            Load();
            UpdateCallBanner();   // açılışta banner temel durumu (ilk okuma sesi tetiklemez)
            // Garson çağrılarını hızlı göster: 5sn'de sayaç + banner tazelenir (adisyon açıkken tam Load atlanır)
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (_, _) => { ClockText.Text = DateTime.Now.ToString("HH:mm"); RefreshCounters(); if (_order is null) Load(); };
            _timer.Start();
        };
        Closed += (_, _) => _timer?.Stop();
    }

    private Brush Res(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    // ===== Üst sekmeler =====
    private void BuildTabs()
    {
        var (tables, deliver, beverage, service) = Counts();
        var tabs = new List<WtTab>
        {
            new("tables",   $"🍽 Masalar ({tables})"),
            new("deliver",  $"📦 Sipariş Teslim ({deliver})"),
            new("beverage", $"🥤 Meşrubatlar ({beverage})"),
            new("service",  $"🔔 Yeni Servis ({service})"),
        };
        foreach (var t in tabs)
        {
            bool active = t.Key == _tab;
            t.Bg = active ? Res("AccentBrush") : Res("SurfaceAltBrush");
            t.Fg = active ? Brushes.White : Res("TextMutedBrush");
        }
        TabHost.ItemsSource = tabs;
    }

    private (int Tables, int Deliver, int Beverage, int Service) Counts()
    {
        try
        {
            int tables = _orders.GetUnpaidTables(_branchId).Count;
            var tickets = _kds.GetTickets(_branchId, 0, includeDone: false);
            int deliver = tickets.Where(l => l.StationType == StationType.Mutfak && l.LineStatus == OrderLineStatus.Hazir).GroupBy(l => l.OrderId).Count();
            int beverage = tickets.Where(l => l.StationType == StationType.Bar && l.LineStatus == OrderLineStatus.Hazir).GroupBy(l => l.OrderId).Count();
            int service = _requests.GetActive(_branchId).Count;
            return (tables, deliver, beverage, service);
        }
        catch { return (0, 0, 0, 0); }
    }

    private void RefreshCounters() { try { BuildTabs(); UpdateCallBanner(); UpdateReadyNotify(); } catch { } }

    // ===== "ÜRÜN HAZIR" SESİ (mutfak+bar teslim bekleyen; polling artış-algılama; SignalR yok) =====
    private int _lastReadyCount = -1;   // -1 = henüz okunmadı → ilk okuma ses çalmaz; yalnız ARTIŞTA çalar

    /// <summary>Şubede teslim bekleyen (mutfak+bar, LineStatus==Hazir) sipariş sayısı bir önceki poll'e göre
    /// ARTMIŞSA (yeni ürün hazır oldu) çağrı/yeni-siparişten FARKLI bir ses çalar. Şube bazlı (WPF tek-terminal). Salt-okuma.</summary>
    private void UpdateReadyNotify()
    {
        int ready;
        try
        {
            var (_, deliver, beverage, _) = Counts();   // deliver=mutfak Hazir, beverage=bar Hazir (sipariş sayısı)
            ready = deliver + beverage;
        }
        catch { return; }   // sorgu hatasında mevcut durumu koru

        // Yalnız ARTIŞTA (yeni hazır) ses çal — ilk okumada (-1) ve azalışta (teslim edildikçe) çalma
        if (_lastReadyCount >= 0 && ready > _lastReadyCount)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }   // çağrı(Exclamation) ve yeni-sipariş(Beep)'ten FARKLI
        }
        _lastReadyCount = ready;
    }

    // ===== GARSON ÇAĞRISI BANNER (belirgin + yanıp sönen) + YENİ çağrıda ses =====
    private int _lastCallCount = -1;   // -1 = henüz okunmadı → ilk okuma ses çalmaz

    /// <summary>Aktif "Garson Çağrısı" (CagriGarson) sayısına göre üstteki yanıp sönen banner'ı günceller.
    /// Sayı bir önceki poll'e göre ARTMIŞSA (yeni çağrı) sistem sesi çalar. Salt-okuma; enumerasyona kapalı.</summary>
    private void UpdateCallBanner()
    {
        int count;
        try
        {
            count = _requests.GetActive(_branchId).Count(r => r.Type == ServiceRequestType.CagriGarson);
        }
        catch { return; }   // sorgu hatasında mevcut banner durumunu koru

        // Yalnız ARTIŞTA (yeni çağrı geldi) ses çal — ilk okumada (-1) ve azalışta çalma
        if (_lastCallCount >= 0 && count > _lastCallCount)
        {
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }
        _lastCallCount = count;

        if (count > 0)
        {
            CallBannerText.Text = count == 1 ? "1 masa garson çağırıyor" : $"{count} masa garson çağırıyor";
            CallBanner.Visibility = Visibility.Visible;
        }
        else CallBanner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Banner tıklanınca "Yeni Servis" sekmesine geç (bekleyen çağrılar burada tamamlanır).</summary>
    private void CallBanner_Click(object sender, MouseButtonEventArgs e)
    {
        CleanupEmptyOrder();   // adisyon panelinden çıkarken ürünsüz adisyonu temizle (Tab_Click deseni)
        _order = null;
        _tab = "service";
        BuildTabs();
        Load();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WtTab t) return;
        _tab = t.Key;
        CleanupEmptyOrder();           // sekme değişince ürünsüz açılmış adisyonu sil (masa "Dolu" kalmasın)
        _order = null;                 // sekme değişince adisyon panelinden çık
        BuildTabs();
        Load();
    }

    /// <summary>Açık ama hiç (ödenecek) ürünü olmayan adisyonu sil — masa boşa düşsün.
    /// Masaya girip ürün eklemeden çıkıldığında masanın "Dolu" kalmasını önler. (BackToTables ile aynı temizlik.)</summary>
    private void CleanupEmptyOrder()
    {
        if (_order is { } o) { try { _orders.DeleteIfEmpty(o.Id, Session.CurrentUserId); } catch { } }
    }

    /// <summary>Pencere her kapanışında (X / Çıkış / oturum kapatma / uygulama kapanışı) ürünsüz adisyonu temizle.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        CleanupEmptyOrder();
        base.OnClosing(e);
    }

    private void Load()
    {
        // adisyon paneli açıkken ana içerik gizlenir
        bool inMenu = _order is not null;
        MenuView.Visibility = inMenu ? Visibility.Visible : Visibility.Collapsed;
        TablesView.Visibility = (!inMenu && _tab == "tables") ? Visibility.Visible : Visibility.Collapsed;
        DeliverView.Visibility = (!inMenu && (_tab == "deliver" || _tab == "beverage")) ? Visibility.Visible : Visibility.Collapsed;
        ServiceView.Visibility = (!inMenu && _tab == "service") ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Collapsed;

        if (inMenu) return;
        try
        {
            switch (_tab)
            {
                case "tables": LoadTables(); break;
                case "deliver": LoadDeliver(StationType.Mutfak); break;
                case "beverage": LoadDeliver(StationType.Bar); break;
                case "service": LoadServices(); break;
            }
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Garson Terminali"); }
    }

    // ===== 1) MASALAR =====
    private void LoadTables()
    {
        var zones = _zones.GetAll().Where(z => z.BranchId == _branchId).ToList();

        // tüm zone board'ları
        var boards = new List<(Zone Zone, TableBoardItem Item)>();
        foreach (var z in zones)
            foreach (var it in _board.GetBoard(z.Id))
                boards.Add((z, it));

        var unpaid = _orders.GetUnpaidTables(_branchId).ToDictionary(u => u.OrderId);
        var tickets = _kds.GetTickets(_branchId, 0, includeDone: false);
        var ticketsByOrder = tickets.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        var activeReq = _requests.GetActive(_branchId);
        var callByTable = activeReq.Where(r => r.Type == ServiceRequestType.CagriGarson && r.TableId is int)
            .Select(r => r.TableId!.Value).ToHashSet();
        var reqByTable = activeReq.Where(r => r.TableId is int)
            .GroupBy(r => r.TableId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var cards = new List<TableCard>();
        foreach (var (zone, it) in boards)
        {
            var card = new TableCard(() => OpenTable(it, zone), () => PayTable(it.OrderId), Res)
            {
                TableId = it.TableId,
                ZoneId = zone.Id,
                ZoneText = zone.Name,
                TableName = it.Display,
                Status = it.Status,
            };
            card.StatusColor = it.StatusColor;
            card.StatusText = it.StatusText;
            card.StatusBg = it.Status == TableBoardStatus.Occupied ? "#E91E63" : it.Status == TableBoardStatus.Reserved ? "#E9A23B" : "#6B7280";
            card.BorderCol = it.Status == TableBoardStatus.Occupied ? "#E91E63" : it.Status == TableBoardStatus.Reserved ? "#E9A23B" : "#22808080";

            int elapsedMin = it.OpenedAt.HasValue ? Math.Max(0, (int)(DateTime.UtcNow - it.OpenedAt.Value).TotalMinutes) : 0;
            card.IsLate = it.Status == TableBoardStatus.Occupied && elapsedMin >= LateMinutes;
            card.ElapsedText = it.Status == TableBoardStatus.Occupied ? it.ElapsedText : (it.Status == TableBoardStatus.Reserved ? it.TimeText : "");

            if (it.Status == TableBoardStatus.Occupied && it.OrderId is int oid)
            {
                card.OrderId = oid;
                if (unpaid.TryGetValue(oid, out var u))
                {
                    card.CoverText = u.CoverCount > 0 ? $"👤 {u.CoverCount}" : "";
                    card.AmountText = u.Total > 0 ? $"₺{u.Total:N2}" : "";
                    card.Amount = u.Total;
                }

                // BEKLEYEN İŞLER (zamanlı) — yalnız DOLU masalar için
                var tasks = new List<TableTask>();
                if (ticketsByOrder.TryGetValue(oid, out var lines))
                {
                    card.OrderWaiting = lines.Any(l => l.LineStatus is OrderLineStatus.MutfagaGonderildi or OrderLineStatus.Hazirlaniyor);
                    card.BeverageWaiting = lines.Any(l => l.StationType == StationType.Bar && l.LineStatus == OrderLineStatus.Hazir);

                    // Meşrubat Bekliyor = Bar + Hazir (teslim edilmemiş)
                    var bev = lines.Where(l => l.StationType == StationType.Bar && l.LineStatus == OrderLineStatus.Hazir).ToList();
                    if (bev.Count > 0)
                        tasks.Add(new TableTask("#E91E63", $"Meşrubat Bekliyor ({bev.Sum(l => (int)l.Qty)})", $"{bev.Max(l => l.ElapsedMinutes)} dk"));

                    // Sipariş Bekliyor = Mutfak bekleyen (gönderildi/hazırlanıyor)
                    var prep = lines.Where(l => l.StationType == StationType.Mutfak && l.LineStatus is OrderLineStatus.MutfagaGonderildi or OrderLineStatus.Hazirlaniyor).ToList();
                    if (prep.Count > 0)
                        tasks.Add(new TableTask("#E9A23B", $"Sipariş Bekliyor ({prep.Sum(l => (int)l.Qty)})", $"{prep.Max(l => l.ElapsedMinutes)} dk"));
                }

                // Bu masanın açık servis istekleri (çağrı / servis aç / hesap)
                foreach (var req in reqByTable.TryGetValue(it.TableId, out var rs) ? rs : Enumerable.Empty<ServiceRequest>())
                {
                    string color = req.Type switch
                    {
                        ServiceRequestType.CagriGarson => "#EF4444",
                        ServiceRequestType.Hesap => "#EF4444",
                        _ => "#3B82F6"
                    };
                    tasks.Add(new TableTask(color, req.TypeText, req.ElapsedText));
                }
                card.Tasks = tasks;

                // Sipariş özeti + tüm satırlar + son sipariş saati + masa notu (yalnız DOLU masada ek sorgu)
                try
                {
                    var full = _orders.GetById(oid);
                    if (full is not null)
                    {
                        var vis = _orders.GetLines(oid)
                            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal).ToList();
                        if (vis.Count > 0)
                        {
                            var first = vis[0];
                            card.SummaryText = $"{first.Qty:0.##}x {first.ProductName}"
                                + (vis.Count > 1 ? $"  ·  +{vis.Count - 1} ürün" : "");
                            card.Lines = vis.Select(l => new TableLineRow(l)).ToList();

                            // Son sipariş saati: satırların en geç gönderim/oluşturma zamanı (UTC → yerel)
                            DateTime lastUtc = vis.Select(l => l.LastPrintedAt ?? l.CreatedAt).DefaultIfEmpty().Max();
                            if (lastUtc != default)
                                card.LastOrderText = $"Son sipariş: {lastUtc.ToLocalTime():HH:mm}";
                        }
                        if (!string.IsNullOrWhiteSpace(full.AllergyNote))
                        {
                            card.NoteText = $"⚠ {full.AllergyNote}";
                            card.NoteIsAllergy = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(full.Note))
                            card.NoteText = $"📝 {full.Note}";
                    }
                }
                catch { /* özet/not opsiyonel */ }
            }
            card.IsCalling = it.TableId > 0 && callByTable.Contains(it.TableId);
            // yenilemeler arasında açık kalan detayları koru
            if (card.HasLines && _expandedTables.Contains(it.TableId)) card.IsExpanded = true;
            card.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(TableCard.IsExpanded)) return;
                if (card.IsExpanded) _expandedTables.Add(card.TableId); else _expandedTables.Remove(card.TableId);
            };
            cards.Add(card);
        }

        // filtre çipleri (sayılı)
        BuildTableFilters(zones, cards);

        // uygula
        IEnumerable<TableCard> shown = cards;
        if (_tableFilter.StartsWith("zone:") && int.TryParse(_tableFilter.Substring(5), out var zid)) shown = cards.Where(c => c.ZoneId == zid);
        else shown = _tableFilter switch
        {
            "full" => cards.Where(c => c.Status == TableBoardStatus.Occupied),
            "order" => cards.Where(c => c.OrderWaiting || c.BeverageWaiting),
            "reserve" => cards.Where(c => c.Status == TableBoardStatus.Reserved),
            "empty" => cards.Where(c => c.Status == TableBoardStatus.Empty),
            "late" => cards.Where(c => c.IsLate),
            _ => cards
        };
        var list = shown.ToList();
        TableHost.ItemsSource = list;
        if (list.Count == 0) { EmptyHint.Text = "Bu filtre için masa yok."; EmptyHint.Visibility = Visibility.Visible; }
    }

    private void BuildTableFilters(List<Zone> zones, List<TableCard> cards)
    {
        var chips = new List<WtChip>
        {
            new("all", $"Tümü ({cards.Count})"),
        };
        foreach (var z in zones)
        {
            int n = cards.Count(c => c.ZoneId == z.Id);
            chips.Add(new($"zone:{z.Id}", $"{z.Name} ({n})"));
        }
        chips.Add(new("full", $"Dolu ({cards.Count(c => c.Status == TableBoardStatus.Occupied)})"));
        chips.Add(new("order", $"Sipariş Bekleyen ({cards.Count(c => c.OrderWaiting || c.BeverageWaiting)})"));
        chips.Add(new("reserve", $"Rezerve ({cards.Count(c => c.Status == TableBoardStatus.Reserved)})"));
        chips.Add(new("empty", $"Boş ({cards.Count(c => c.Status == TableBoardStatus.Empty)})"));
        chips.Add(new("late", $"Geciken ({cards.Count(c => c.IsLate)})"));
        foreach (var c in chips)
        {
            bool active = c.Key == _tableFilter;
            c.Bg = active ? Res("AccentBrush") : Res("SurfaceAltBrush");
            c.Fg = active ? Brushes.White : Res("TextMutedBrush");
        }
        TableFilterHost.ItemsSource = chips;
    }

    private void TableFilter_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is WtChip c) { _tableFilter = c.Key; LoadTables(); }
    }

    private void OpenTable(TableBoardItem item, Zone zone)
    {
        try
        {
            Order order;
            if (item.Status == TableBoardStatus.Occupied && item.OrderId is int oid)
            {
                if (_orders.GetById(oid) is not { } existing) { Dialog.Info("Bu masa artık açık değil.", "Masa"); Load(); return; }
                order = existing;
            }
            else
            {
                var pa = _customers.GetAll().FirstOrDefault(c => c.Code == "PA")
                    ?? throw new InvalidOperationException("Varsayılan 'PA' (perakende) cari kartı bulunamadı. Cari Kartlar'dan oluşturun.");
                order = _orders.CreateOrder(_branchId, zone.Id, pa.Id, new[] { item.TableId }, Session.CurrentUserId);
            }
            EnterOrder(order);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Masa"); }
    }

    /// <summary>Karttan doğrudan hesap al (PaymentWindowV2). Masalar listesinden çıkmadan ödeme alınır.</summary>
    private void PayTable(int? orderId)
    {
        if (orderId is not int oid) return;
        if (!Permissions.Payment) { Dialog.Warn("Ödeme alma yetkiniz yok."); return; }
        try
        {
            var order = _orders.GetById(oid);
            if (order is null) { Dialog.Info("Bu masa artık açık değil.", "Hesap"); Load(); return; }
            new PaymentWindowV2(order, Session.CurrentKioskId) { Owner = this }.ShowDialog();
            BuildTabs();
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hesap"); }
    }

    // ===== 2) MENÜ / ADİSYON PANELİ (split: sol canlı adisyon · orta menü) =====
    private void EnterOrder(Order order)
    {
        _order = _orders.GetById(order.Id) ?? order;
        _zoneId = _order.ZoneId;
        SelTableText.Text = string.IsNullOrWhiteSpace(_order.TableCodes) ? _order.Code : $"Masa {_order.TableCodes}";
        SelZoneText.Text = _order.ZoneName ?? "";
        _lastAddedLineId = 0;

        // Kişiye (SeatText) göre gruplanmış canlı adisyon (Varsayılan AdisyonWindow deseni) — tek sefer kur
        if (!_liveGrouped)
        {
            var view = (ICollectionView)CollectionViewSource.GetDefaultView(_liveLines);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LiveLineVM.SeatText)));
            LinesList.ItemsSource = view;
            _liveGrouped = true;
        }

        try
        {
            _categories = new List<ProductCategory> { new() { Id = 0, Name = "Tümü" } };
            _categories.AddRange(_catalog.GetCategories());
            _categoryFilter = 0;
            _allProducts = _catalog.GetProducts(_branchId);
            _activeCampaigns = _campaigns.GetActive(_branchId, DateTime.Now);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Katalog"); }

        BuildCategories();
        FilterProducts();
        LoadCampaigns();
        UpdateCoverText();
        UpdateFlagVisuals();
        ReloadOrderLines();
        PayBtn.IsEnabled = Permissions.Payment;
        Load();   // görünürlükleri ayarla (MenuView açılır)
    }

    private void BackToTables_Click(object sender, RoutedEventArgs e)
    {
        if (_order is { } o) { try { _orders.DeleteIfEmpty(o.Id, Session.CurrentUserId); } catch { } }
        _order = null;
        _tab = "tables";
        BuildTabs();
        Load();
    }

    // ----- Menü: kategoriler / kampanyalar / arama / ürün -----
    private void BuildCategories()
    {
        var chips = _categories.Select(c => new WtChip(c.Id.ToString(), c.Name)).ToList();
        foreach (var ch in chips)
        {
            bool active = ch.Key == _categoryFilter.ToString();
            ch.Bg = active ? Res("AccentBrush") : Res("SurfaceAltBrush");
            ch.Fg = active ? Brushes.White : Res("TextMutedBrush");
        }
        CategoryHost.ItemsSource = chips;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is WtChip c && int.TryParse(c.Key, out var id))
        { _categoryFilter = id; BuildCategories(); FilterProducts(); }
    }

    // Şu an geçerli kampanyalar panele yüklenir (kalıcı; Varsayılan AdisyonWindow deseni).
    private void LoadCampaigns()
    {
        _campaignVMs.Clear();
        foreach (var c in _activeCampaigns) _campaignVMs.Add(new AdCampaignVM(c));
        CampaignHost.ItemsSource = _campaignVMs;
        CampaignSection.Visibility = _campaignVMs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _campaignsLoaded = true;
        RefreshCampaigns();
    }

    // Her sipariş değişiminde: kriter sağlanıyor mu? canlı güncelle (+ zorunlu olanı otomatik uygula).
    private void RefreshCampaigns()
    {
        if (!_campaignsLoaded || _order is null) return;
        var applied = _orders.GetAppliedCampaignIds(_order.Id);

        // Uygulanan ama artık kriteri sağlamayan → uyar + kaldır
        var lost = _campaignVMs.Where(v => applied.Contains(v.Camp.Id) && !_orders.IsCampaignEligible(_order.Id, v.Camp)).ToList();
        if (lost.Count > 0)
        {
            Dialog.Warn($"'{string.Join(", ", lost.Select(v => v.Camp.Name))}' kampanya(sı) artık kriterleri karşılamıyor; kaldırıldı.", "Kampanya");
            try { _orders.RemoveCampaign(_order.Id, Session.CurrentUserId); } catch { }
            ReloadOrderLines();   // → bu metot tekrar çalışır
            return;
        }

        int eligible = 0;
        foreach (var v in _campaignVMs)
        {
            v.IsApplied = applied.Contains(v.Camp.Id);
            v.IsEligible = _orders.IsCampaignEligible(_order.Id, v.Camp);
            if (v.IsEligible && !v.IsApplied) eligible++;
            v.StatusText = v.IsApplied ? "✓ Uygulandı"
                : v.IsEligible ? (v.Camp.IsMandatory ? "🔒 OTOMATİK (zorunlu)" : "🔴 KULLANILABİLİR!")
                : "kriter sağlanmıyor";
        }

        // ZORUNLU kampanyalar: kriter sağlanıyorsa OTOMATİK uygula
        if (!_autoApplying)
        {
            var autoApply = _campaignVMs.Where(v => v.Camp.IsMandatory && v.IsEligible && !v.IsApplied).Select(v => v.Camp).ToList();
            if (autoApply.Count > 0)
            {
                _autoApplying = true;
                bool any = false;
                foreach (var camp in autoApply)
                    try { _orders.ApplyCampaign(_order.Id, camp.Id, _branchId, Session.CurrentUserId); any = true; } catch { }
                if (any) { ReloadOrderLines(); _autoApplying = false; return; }
                _autoApplying = false;
            }
        }

        CampaignHint.Text = _campaignVMs.Count == 0 ? "Şu an geçerli kampanya yok."
            : eligible > 0 ? $"🔴 {eligible} kampanya kullanılabilir!" : $"{_campaignVMs.Count} kampanya";
    }

    private void ApplyCampaign_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || (sender as FrameworkElement)?.Tag is not AdCampaignVM vm) return;
        try { _orders.ApplyCampaign(_order.Id, vm.Camp.Id, _branchId, Session.CurrentUserId); ReloadOrderLines(); }
        catch (Exception ex) { Dialog.Warn(ex.Message, "Kampanya"); }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_allProducts.Count > 0) FilterProducts(); }

    private void FilterProducts()
    {
        string q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<PosProduct> items = _allProducts;
        if (_categoryFilter != 0) items = items.Where(p => p.CategoryIds.Contains(_categoryFilter));
        if (q.Length > 0) items = items.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(q, StringComparison.OrdinalIgnoreCase));
        ProductHost.ItemsSource = items.ToList();
    }

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || (sender as FrameworkElement)?.Tag is not PosProduct p) return;
        if (p.IsSalesStopped) { Dialog.Info($"{p.Name} şu an satışa kapalı.", "Satışta değil"); return; }
        try
        {
            // AddLine eklenen ÜST satırı döndürür → solda anında görünür + kısa highlight
            var added = _orders.AddLine(_order.Id, _zoneId, p.Id, 1, p.Price, p.TaxRate, null, 0, Session.CurrentUserId);
            _lastAddedLineId = added.Id;
            ReloadOrderLines();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    // ----- Kişi (cover) -----
    private void UpdateCoverText() { CoverText.Text = $"{Math.Max(1, _order?.CoverCount ?? 1)} kişi"; }

    private void CoverMinus_Click(object sender, RoutedEventArgs e) => ChangeCover(-1);
    private void CoverPlus_Click(object sender, RoutedEventArgs e) => ChangeCover(+1);

    private void ChangeCover(int delta)
    {
        if (_order is null) return;
        int nv = Math.Max(1, _order.CoverCount + delta);
        try { _orders.SetCoverCount(_order.Id, nv, Session.CurrentUserId); _order.CoverCount = nv; UpdateCoverText(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Kişi"); }
    }

    // ----- Sol canlı adisyon -----
    private void ReloadOrderLines()
    {
        if (_order is null) return;
        _order = _orders.GetById(_order.Id) ?? _order;
        var lines = _orders.GetLines(_order.Id)
            .Where(l => l.LineStatus != OrderLineStatus.Iptal).ToList();

        _liveLines.Clear();
        foreach (var l in lines) _liveLines.Add(new LiveLineVM(l, l.Id == _lastAddedLineId));

        TotalText.Text = _order.TotalText;
        UpdateCoverText();
        RefreshCampaigns();
    }

    /// <summary>Sol listede seçili (kullanıcı tıkladığı) üst satır; menü bileşeni seçilemez (üstüne yönlendir).</summary>
    private LiveLineVM? Selected => LinesList.SelectedItem is LiveLineVM v && !v.IsComponent ? v : null;

    private void Treat_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("İkram (bedelsiz) için yetkiniz yok; kasiyer/yönetici onayı gerekir."); return; }
        if (Selected is not { } row) { NeedSelect(); return; }
        try { _orders.SetTreat(row.LineId, !row.IsTreat, Session.CurrentUserId); ReloadOrderLines(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "İkram"); }
    }

    private void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("İskonto için yetkiniz yok; kasiyer/yönetici onayı gerekir."); return; }
        if (Selected is not { } row) { NeedSelect(); return; }
        var w = new AmountPromptWindow("Satır İskontosu", $"{row.ProductName} — iskonto tutarı (₺)", row.DiscountAmount) { Owner = this };
        if (w.ShowDialog() != true) return;
        decimal max = row.Qty * row.UnitPrice;
        if (w.Amount > max) { Dialog.Warn($"İskonto satır tutarını ({max:N2}) aşamaz."); return; }
        try { _orders.SetLineDiscount(row.LineId, w.Amount, Session.CurrentUserId); ReloadOrderLines(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void CancelLine_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) { NeedSelect(); return; }
        bool returnToStock = true;
        if (row.Sent)
        {
            var r = Dialog.YesNoCancel(
                $"'{row.ProductName}' mutfağa gönderilmişti, iptal ediliyor.\n\nMalzemeler ne olsun?\n\n• EVET = STOĞA İADE\n• HAYIR = FİRE\n• VAZGEÇ = işlemi iptal et",
                "İptal — Fire mi, Stoğa İade mi?");
            if (r == DialogResultType.Cancel) return;
            returnToStock = r == DialogResultType.Yes;
        }
        else if (!Dialog.Confirm($"'{row.ProductName}' iptal edilsin mi?\n(Mutfağa gönderilmediği için stok etkilenmez, hesaba da girmez.)", "İptal")) return;

        // İptal sebebi (rapor için) — boş bırakılabilir (Varsayılan AdisyonWindow deseni)
        var rp = new ReasonPromptWindow("İptal Sebebi", $"'{row.ProductName}' iptal sebebi") { Owner = this };
        if (rp.ShowDialog() != true) return;

        try { _orders.CancelLine(row.LineId, Session.CurrentUserId, returnToStock, string.IsNullOrWhiteSpace(rp.Reason) ? null : rp.Reason); ReloadOrderLines(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "İptal"); }
    }

    /// <summary>Seçili satıra ürün açıklaması (mutfak notu) ekle/düzenle (Varsayılan AdisyonWindow satır notu deseni).</summary>
    private void Note_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) { NeedSelect(); return; }
        var w = new TextPromptWindow("Ürün Açıklaması", "Örn: az pişmiş, sossuz...", row.KitchenNote, NotePresets.General) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _orders.UpdateLineNote(row.LineId, string.IsNullOrWhiteSpace(w.Value) ? null : w.Value.Trim(), Session.CurrentUserId); ReloadOrderLines(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Açıklama"); }
    }

    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try { _orders.Reprint(_order.Id, Session.CurrentUserId); ReloadOrderLines(); Dialog.Info("Gönderilmiş satırların yazdırma sayısı artırıldı.", "Yeniden Yazdır"); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Yazdır"); }
    }

    private static void NeedSelect() => Dialog.Info("Önce soldan bir satır seçin.", "Uyarı");

    private void Receipt_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try { new HesapFisiWindow(_order) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hesap"); }
    }

    // ----- VIP / Öncelik / Alerji (Varsayılan AdisyonWindow mantığı) -----
    private void UpdateFlagVisuals()
    {
        if (_order is null) return;
        StyleSwitch(VipBtn, _order.IsVip);
        StyleSwitch(PriorityBtn, _order.Priority);
        AllergyBtn.Content = string.IsNullOrWhiteSpace(_order.AllergyNote) ? "⚠ Alerji" : "⚠ Alerji ✓";
    }

    private void Vip_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try { _order.IsVip = !_order.IsVip; _orders.SetOrderFlags(_order.Id, _order.IsVip, null, Session.CurrentUserId); UpdateFlagVisuals(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "VIP"); }
    }

    private void Priority_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try { _order.Priority = !_order.Priority; _orders.SetOrderFlags(_order.Id, null, _order.Priority, Session.CurrentUserId); UpdateFlagVisuals(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Öncelik"); }
    }

    private void Allergy_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        var w = new TextPromptWindow("Alerji Notu", "Örn: Fıstık alerjisi", _order.AllergyNote, NotePresets.Allergy) { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _order.AllergyNote = string.IsNullOrWhiteSpace(w.Value) ? null : w.Value.Trim(); _orders.SetAllergyNote(_order.Id, _order.AllergyNote, Session.CurrentUserId); UpdateFlagVisuals(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Alerji"); }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        try
        {
            var tickets = _orders.SendToKitchen(_order.Id, Session.CurrentUserId);
            ReloadOrderLines();
            int printed = 0, noPrinter = 0;
            foreach (var t in tickets)
            {
                if (t.HasPrinter) { if (ReceiptVisual.PrintKitchen(t)) printed++; }
                else noPrinter++;
            }
            string msg = "Bekleyen satırlar mutfağa/reyonlara gönderildi.";
            if (printed > 0) msg += $"\n🖨 {printed} reyon fişi yazıcıdan basıldı.";
            if (noPrinter > 0) msg += $"\n{noPrinter} reyon yazıcısız (yalnız ekran/KDS'te görünür).";
            Dialog.Info(msg, "Mutfağa Gönder");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        if (!Permissions.Payment) { Dialog.Warn("Ödeme alma yetkiniz yok."); return; }
        if (_orders.GetLines(_order.Id).All(l => l.LineStatus == OrderLineStatus.Iptal || l.SettledClFicheId != null || l.IsTreat))
        { Dialog.Info("Ödenecek açık satır yok.", "Ödeme"); return; }

        var w = new PaymentWindowV2(_order, Session.CurrentKioskId) { Owner = this };
        w.ShowDialog();
        _order = _orders.GetById(_order.Id);
        if (_order is null || _order.Status == OrderStatus.Kapandi) { _order = null; _tab = "tables"; BuildTabs(); Load(); }
        else ReloadOrderLines();
    }

    // ----- Masa Notu overlay -----
    private void TableNote_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        NoteBox.Text = _order.Note ?? "";
        AllergyBox.Text = _order.AllergyNote ?? "";
        StyleSwitch(VipSwitch, _order.IsVip);
        StyleSwitch(PrioritySwitch, _order.Priority);
        NoteOverlay.Visibility = Visibility.Visible;
    }

    private void NoteChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string s) NoteBox.Text = string.IsNullOrWhiteSpace(NoteBox.Text) ? s : NoteBox.Text.TrimEnd() + ", " + s;
    }

    private void AllergyChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string s) AllergyBox.Text = string.IsNullOrWhiteSpace(AllergyBox.Text) ? s : AllergyBox.Text.TrimEnd() + ", " + s;
    }

    private void VipSwitch_Click(object sender, RoutedEventArgs e) => StyleSwitch(VipSwitch, !SwitchOn(VipSwitch));
    private void PrioritySwitch_Click(object sender, RoutedEventArgs e) => StyleSwitch(PrioritySwitch, !SwitchOn(PrioritySwitch));

    private static bool SwitchOn(Button b) => b.Tag is true;

    private void StyleSwitch(Button b, bool on)
    {
        b.Tag = on;
        b.Background = on ? Res("AccentBrush") : Res("SurfaceAltBrush");
        b.Foreground = on ? Brushes.White : Res("TextBrush");
    }

    private void NoteCancel_Click(object sender, RoutedEventArgs e) => NoteOverlay.Visibility = Visibility.Collapsed;

    private void NoteSave_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { NoteOverlay.Visibility = Visibility.Collapsed; return; }
        try
        {
            string? note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
            string? allergy = string.IsNullOrWhiteSpace(AllergyBox.Text) ? null : AllergyBox.Text.Trim();
            bool vip = SwitchOn(VipSwitch), pri = SwitchOn(PrioritySwitch);
            _orders.SetOrderNote(_order.Id, note, Session.CurrentUserId);
            _orders.SetAllergyNote(_order.Id, allergy, Session.CurrentUserId);
            _orders.SetOrderFlags(_order.Id, vip, pri, Session.CurrentUserId);
            _order.Note = note; _order.AllergyNote = allergy; _order.IsVip = vip; _order.Priority = pri;
            UpdateFlagVisuals();   // başlık VIP/Öncelik/Alerji butonları senkron
            NoteOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Masa Notu"); }
    }

    // ===== 3/4) TESLİM / MEŞRUBAT =====
    private void LoadDeliver(StationType station)
    {
        var lines = _kds.GetTickets(_branchId, 0, includeDone: false)
            .Where(l => l.StationType == station && l.LineStatus == OrderLineStatus.Hazir).ToList();
        var cards = lines.GroupBy(l => l.OrderId).Select(g =>
        {
            var f = g.First();
            return new DeliverCard
            {
                OrderId = g.Key,
                TableTitle = string.IsNullOrWhiteSpace(f.TableCodes) ? f.OrderCode : $"Masa {f.TableCodes}",
                ZoneText = f.ZoneName ?? "",
                WaiterName = f.WaiterName ?? "",
                IsVip = f.IsVip,
                HasAllergy = f.HasAllergy,
                AllergyNote = f.AllergyNote,
                Lines = g.ToList()
            };
        }).ToList();
        DeliverHost.ItemsSource = cards;
        if (cards.Count == 0) { EmptyHint.Text = station == StationType.Bar ? "Teslim bekleyen meşrubat yok." : "Teslim bekleyen sipariş yok."; EmptyHint.Visibility = Visibility.Visible; }
    }

    private void Deliver_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeliverCard card) return;
        try
        {
            foreach (var l in card.Lines) _orders.SetLineStatus(l.LineId, OrderLineStatus.Teslim, Session.CurrentUserId);
            BuildTabs();
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Teslim"); }
    }

    // ===== 5) YENİ SERVİS =====
    private void LoadServices()
    {
        var reqs = _requests.GetActive(_branchId);
        var cards = reqs.Select(r => new ServiceCard(r)).ToList();
        ServiceHost.ItemsSource = cards;
        if (cards.Count == 0) { EmptyHint.Text = "Bekleyen servis isteği yok."; EmptyHint.Visibility = Visibility.Visible; }
    }

    private void ServiceComplete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ServiceCard card) return;
        try { _requests.Complete(card.Id, Session.CurrentUserId); BuildTabs(); Load(); UpdateCallBanner(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Servis"); }
    }

    // ===== MASA İŞLEMLERİ (Taşı / Böl / Birleştir / Kişi Aktar) =====
    private string _linePickMode = "";   // "split" (boş masa) | "movelines" (dolu masa)

    private void TableOps_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        TableOpsSubText.Text = string.IsNullOrWhiteSpace(_order.TableCodes) ? _order.Code : $"Masa {_order.TableCodes}";
        TableOpsOverlay.Visibility = Visibility.Visible;
    }

    private void TableOpsClose_Click(object sender, RoutedEventArgs e) => TableOpsOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Şubedeki tüm masaları board'dan toplar; istenirse boş/dolu filtreler. Dolu masada hedef adisyon id'si eklenir.</summary>
    private List<TablePickItem> BuildTablePicks(bool wantEmpty, int? excludeTableId)
    {
        var picks = new List<TablePickItem>();
        var unpaid = _orders.GetUnpaidTables(_branchId).ToDictionary(u => u.OrderId);
        var zones = _zones.GetAll().Where(z => z.BranchId == _branchId).ToList();
        foreach (var z in zones)
        {
            foreach (var it in _board.GetBoard(z.Id))
            {
                if (excludeTableId is int ex && it.TableId == ex) continue;
                bool occupied = it.Status == TableBoardStatus.Occupied && it.OrderId is int;
                if (wantEmpty)
                {
                    // BOŞ = dolu değil (rezerve dahil edilir; backend dolu masaya bölmeyi/taşımayı zaten reddeder)
                    if (occupied) continue;
                    picks.Add(new TablePickItem
                    {
                        TableId = it.TableId,
                        OrderId = null,
                        ZoneText = z.Name,
                        TableName = it.Display,
                        SubText = it.Status == TableBoardStatus.Reserved ? "Rezerve" : "Boş",
                        BorderCol = it.Status == TableBoardStatus.Reserved ? "#E9A23B" : "#22808080",
                    });
                }
                else
                {
                    if (!occupied) continue;
                    int oid = it.OrderId!.Value;
                    string amount = unpaid.TryGetValue(oid, out var u) && u.Total > 0 ? $"₺{u.Total:N2}" : "";
                    picks.Add(new TablePickItem
                    {
                        TableId = it.TableId,
                        OrderId = oid,
                        ZoneText = z.Name,
                        TableName = it.Display,
                        SubText = string.IsNullOrEmpty(amount) ? "Dolu" : amount,
                        BorderCol = "#E91E63",
                    });
                }
            }
        }
        return picks;
    }

    // ----- Taşı -----
    private void OpMove_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        TableOpsOverlay.Visibility = Visibility.Collapsed;
        var picks = BuildTablePicks(wantEmpty: true, excludeTableId: null);
        var dlg = new TablePickerWindow("➡ Taşı", "Adisyonun taşınacağı BOŞ masayı seçin.", picks, "Taşınacak boş masa yok.") { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } target) return;
        try
        {
            _orders.MoveOrder(_order.Id, target.TableId, Session.CurrentUserId);
            Dialog.Info($"Adisyon '{target.TableName}' masasına taşındı.", "Taşı");
            ReturnToTablesAfterOp();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Taşı"); }
    }

    // ----- Birleştir -----
    private void OpMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        TableOpsOverlay.Visibility = Visibility.Collapsed;
        var ownTableId = _board.GetBoard(_zoneId).FirstOrDefault(b => b.OrderId == _order.Id)?.TableId;
        var picks = BuildTablePicks(wantEmpty: false, excludeTableId: ownTableId);
        var dlg = new TablePickerWindow("🔗 Birleştir", "Bu adisyon, seçtiğiniz DOLU masanın açık adisyonuna aktarılıp kapanır.", picks, "Birleştirilecek dolu masa yok.") { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } target || target.OrderId is not int targetOid) return;
        if (!Dialog.Confirm($"Bu adisyon '{target.TableName}' masasıyla birleştirilsin mi?", "Birleştir")) return;
        try
        {
            _orders.MergeOrders(_order.Id, targetOid, Session.CurrentUserId);
            Dialog.Info($"Adisyon '{target.TableName}' masasıyla birleştirildi.", "Birleştir");
            ReturnToTablesAfterOp();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Birleştir"); }
    }

    // ----- Böl / Kişi Aktar: önce kalem/kişi seçimi -----
    private void OpSplit_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        TableOpsOverlay.Visibility = Visibility.Collapsed;
        OpenLinePick("split", "✂ Böl — Bölünecek kalemleri/kişiyi seçin");
    }

    private void OpMoveLines_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) return;
        TableOpsOverlay.Visibility = Visibility.Collapsed;
        OpenLinePick("movelines", "👤 Kişi Aktar — Aktarılacak kalemleri/kişiyi seçin");
    }

    private void OpenLinePick(string mode, string title)
    {
        if (_order is null) return;
        _linePickMode = mode;
        LinePickTitle.Text = title;

        // Yalnız üst (ParentLineId==null), iptal olmayan ve ÖDENMEMİŞ (SettledClFicheId==null) satırlar seçilebilir.
        // Ödenmiş satırlar backend'de zaten taşınmaz; UI'da da gösterilmez ki "taşıdım sandım" yanılgısı olmasın.
        var rows = _orders.GetLines(_order.Id)
            .Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal && l.SettledClFicheId == null)
            .Select(l => new LinePickVM(l)).ToList();
        if (rows.Count == 0) { Dialog.Info("Taşınacak (ödenmemiş) kalem yok.", "Masa İşlemleri"); return; }
        LinePickHost.ItemsSource = rows;

        // koltuk çipleri (Tümü + her koltuk)
        var seats = new List<SeatChip> { new(-1, "Tümü") };
        foreach (var g in rows.GroupBy(r => r.SeatNo).OrderBy(g => g.Key))
            seats.Add(new SeatChip(g.Key, g.First().SeatText));
        SeatChipHost.ItemsSource = seats;

        LinePickOverlay.Visibility = Visibility.Visible;
    }

    private List<LinePickVM> LinePickRows => (LinePickHost.ItemsSource as List<LinePickVM>) ?? new();

    private void SeatChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SeatChip chip) return;
        foreach (var r in LinePickRows)
            r.IsChecked = chip.SeatNo == -1 || r.SeatNo == chip.SeatNo;
        LinePickHost.Items.Refresh();
    }

    private void LinePickCancel_Click(object sender, RoutedEventArgs e) => LinePickOverlay.Visibility = Visibility.Collapsed;

    private void LinePickConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null) { LinePickOverlay.Visibility = Visibility.Collapsed; return; }
        var ids = LinePickRows.Where(r => r.IsChecked).Select(r => r.LineId).ToList();
        if (ids.Count == 0) { Dialog.Warn("En az bir kalem seçin.", "Masa İşlemleri"); return; }
        LinePickOverlay.Visibility = Visibility.Collapsed;

        if (_linePickMode == "split") DoSplit(ids);
        else DoMoveLines(ids);
    }

    private void DoSplit(List<int> ids)
    {
        if (_order is null) return;
        var picks = BuildTablePicks(wantEmpty: true, excludeTableId: null);
        var dlg = new TablePickerWindow("✂ Böl", "Seçili kalemlerin taşınacağı BOŞ masayı seçin.", picks, "Bölünecek boş masa yok.") { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } target) return;
        try
        {
            var newOrder = _orders.SplitOrder(_order.Id, ids, target.TableId, Session.CurrentUserId);
            string code = string.IsNullOrWhiteSpace(newOrder.TableCodes) ? newOrder.Code : $"Masa {newOrder.TableCodes}";
            Dialog.Info($"{ids.Count} kalem yeni adisyona bölündü: {code}", "Böl");
            ReloadOrderLines();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Böl"); }
    }

    private void DoMoveLines(List<int> ids)
    {
        if (_order is null) return;
        var ownTableId = _board.GetBoard(_zoneId).FirstOrDefault(b => b.OrderId == _order.Id)?.TableId;
        var picks = BuildTablePicks(wantEmpty: false, excludeTableId: ownTableId);
        var dlg = new TablePickerWindow("👤 Kişi Aktar", "Seçili kalemlerin aktarılacağı DOLU masayı seçin.", picks, "Aktarılacak dolu masa yok.") { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } target || target.OrderId is not int targetOid) return;
        try
        {
            int moved = _orders.MoveLines(_order.Id, ids, targetOid, Session.CurrentUserId);
            Dialog.Info($"{moved} kalem '{target.TableName}' masasına aktarıldı.", "Kişi Aktar");
            ReloadOrderLines();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Kişi Aktar"); }
    }

    /// <summary>Taşı/Birleştir sonrası: bu adisyon kaynaktan boşaldı → masalara dön.</summary>
    private void ReturnToTablesAfterOp()
    {
        _order = null;
        _tab = "tables";
        BuildTabs();
        Load();
    }

    // ===== Tema / Kapat =====
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var opts = ThemeManager.Options;
        int idx = opts.FindIndex(o => o.Value == ThemeManager.Current);
        ThemeManager.ChangeAndSave(opts[(idx + 1) % opts.Count].Value);
        BuildTabs();
        if (_order is null) Load();
        else { BuildCategories(); UpdateFlagVisuals(); }   // tema fırçaları yenilensin (canlı liste/kampanya DynamicResource)
    }

    private void Close_Click(object sender, RoutedEventArgs e) => LogoutToLogin();

    /// <summary>Çıkış → oturumu kapat, Login ekranını aç, diğer tüm pencereleri kapat.</summary>
    internal static void LogoutToLogin()
    {
        Session.Clear();
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        var all = new Window[Application.Current.Windows.Count];
        Application.Current.Windows.CopyTo(all, 0);
        foreach (var w in all) if (w != login) w.Close();
    }
}

// ===== VM'ler =====
public class WtTab(string key, string label)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public Brush? Bg { get; set; }
    public Brush? Fg { get; set; }
}

public class WtChip(string key, string label)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public Brush? Bg { get; set; }
    public Brush? Fg { get; set; }
}

public class TableCard : System.ComponentModel.INotifyPropertyChanged
{
    private readonly Func<string, Brush> _res;
    /// <param name="open">"Ürün Ekle" / kart tıklaması → Menü-Adisyon paneli.</param>
    /// <param name="pay">"Hesap Al" → ödeme penceresi.</param>
    public TableCard(Action open, Action pay, Func<string, Brush> res)
    {
        _res = res;
        OpenCmd = new RelayCommand(open);
        PayCmd = new RelayCommand(pay);
        ExpandCmd = new RelayCommand(ToggleExpand);
    }

    public int TableId { get; set; }
    public int ZoneId { get; set; }
    public int? OrderId { get; set; }
    public string ZoneText { get; set; } = "";
    public string TableName { get; set; } = "";
    public TableBoardStatus Status { get; set; }
    public string StatusColor { get; set; } = "#6B7280";
    public string StatusText { get; set; } = "";
    public string StatusBg { get; set; } = "#6B7280";
    public string BorderCol { get; set; } = "#22808080";

    public string CoverText { get; set; } = "";
    public string ElapsedText { get; set; } = "";
    public string AmountText { get; set; } = "";
    public decimal Amount { get; set; }
    public string NoteText { get; set; } = "";
    public bool NoteIsAllergy { get; set; }
    public string SummaryText { get; set; } = "";
    public string LastOrderText { get; set; } = "";
    public List<TableTask> Tasks { get; set; } = new();
    public List<TableLineRow> Lines { get; set; } = new();

    public bool IsLate { get; set; }
    public bool BeverageWaiting { get; set; }
    public bool OrderWaiting { get; set; }
    public bool IsCalling { get; set; }

    public bool HasCover => !string.IsNullOrWhiteSpace(CoverText);
    public bool HasElapsed => !string.IsNullOrWhiteSpace(ElapsedText);
    public bool HasAmount => !string.IsNullOrWhiteSpace(AmountText);
    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);
    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);
    public bool HasTasks => Tasks.Count > 0;
    public bool HasLines => Lines.Count > 0;
    public bool HasLastOrder => !string.IsNullOrWhiteSpace(LastOrderText);
    public bool IsOccupied => Status == TableBoardStatus.Occupied;
    public bool CanPay => IsOccupied && Amount > 0;
    /// <summary>Not rengi: alerji ise kırmızı, normal not ise soluk.</summary>
    public string NoteColor => NoteIsAllergy ? "#EF4444" : "#9AA0AB";

    // ----- Inline expand (Tümünü Gör) -----
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set { _expanded = value; OnCh(nameof(IsExpanded)); OnCh(nameof(ExpandVisible)); OnCh(nameof(ExpandLabel)); } }
    /// <summary>Genişletilmiş liste yalnız satır varsa ve açıkken görünür.</summary>
    public bool ExpandVisible => _expanded && HasLines;
    public string ExpandLabel => _expanded ? "Gizle ▴" : "Tümünü Gör ›";
    /// <summary>Kartta detay genişletilebilir mi (dolu + satır var).</summary>
    public bool CanExpand => HasLines;
    private void ToggleExpand() { if (HasLines) IsExpanded = !IsExpanded; }

    public System.Windows.Input.ICommand OpenCmd { get; }
    public System.Windows.Input.ICommand PayCmd { get; }
    public System.Windows.Input.ICommand ExpandCmd { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnCh(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

/// <summary>Masa kartındaki "Bekleyen İşler" satırı: renkli nokta + metin + süre.</summary>
public class TableTask(string color, string text, string time)
{
    public string Color { get; } = color;
    public string Text { get; } = text;
    public string Time { get; } = time;
}

/// <summary>Masa kartı inline genişletildiğinde gösterilen sipariş satırı (qty × ürün, durum, tutar).</summary>
public class TableLineRow
{
    public string QtyText { get; }
    public string Name { get; }
    public string StatusText { get; }
    public string StatusBg { get; }
    public string LineTotalText { get; }
    public bool IsTreat { get; }

    public TableLineRow(OrderLine l)
    {
        QtyText = $"{l.Qty:0.##}x";
        Name = l.ProductName ?? "";
        IsTreat = l.IsTreat;
        StatusText = l.LineStatus.ToTr();
        StatusBg = l.LineStatus switch
        {
            OrderLineStatus.Teslim => "#10B981",
            OrderLineStatus.Hazir => "#17B26A",
            OrderLineStatus.Hazirlaniyor => "#3B82F6",
            OrderLineStatus.MutfagaGonderildi => "#E9A23B",
            _ => "#6B7280"
        };
        LineTotalText = l.IsTreat ? "İKRAM" : $"₺{l.LineTotal:N2}";
    }
}

/// <summary>Sol canlı adisyon satırı (Varsayılan AdisyonWindow kırılımlarıyla): kişiye göre gruplanır,
/// menü bileşeni alt satırı (IsComponent) girintili "↳" + soluk + fiyat gizli, durum rozeti, İKRAM, KitchenNote.</summary>
public class LiveLineVM
{
    public int LineId { get; }
    public int SeatNo { get; }
    public string SeatText { get; }      // gruplama anahtarı
    public decimal Qty { get; }
    public string QtyText { get; }
    public string ProductName { get; }
    public string? KitchenNote { get; }
    public bool IsComponent { get; }
    public bool IsTreat { get; }
    public bool Sent { get; }
    public bool IsNew { get; }           // son eklenen → kısa highlight
    public decimal UnitPrice { get; }
    public decimal DiscountAmount { get; }
    public string StatusText { get; }
    public string StatusBg { get; }
    public string LineTotalText { get; }

    public LiveLineVM(OrderLine l, bool isNew)
    {
        LineId = l.Id;
        SeatNo = l.SeatNo;
        SeatText = l.SeatText;
        Qty = l.Qty;
        QtyText = $"{l.Qty:0.##}";
        ProductName = l.ProductName ?? "";
        KitchenNote = l.KitchenNote;
        IsComponent = l.IsComponent;
        IsTreat = l.IsTreat;
        IsNew = isNew && !l.IsComponent;
        UnitPrice = l.UnitPrice;
        DiscountAmount = l.DiscountAmount;
        Sent = l.LineStatus is OrderLineStatus.MutfagaGonderildi or OrderLineStatus.Hazir or OrderLineStatus.Teslim or OrderLineStatus.Hazirlaniyor;
        StatusText = l.LineStatus.ToTr();
        StatusBg = l.LineStatus switch
        {
            OrderLineStatus.Teslim => "#10B981",
            OrderLineStatus.Hazir => "#17B26A",
            OrderLineStatus.Hazirlaniyor => "#3B82F6",
            OrderLineStatus.MutfagaGonderildi => "#E9A23B",
            _ => "#6B7280"
        };
        LineTotalText = l.IsComponent ? "" : (l.IsTreat ? "İKRAM" : $"₺{l.LineTotal:N2}");
    }
}

public class DeliverCard
{
    public int OrderId { get; set; }
    public string TableTitle { get; set; } = "";
    public string ZoneText { get; set; } = "";
    public string WaiterName { get; set; } = "";
    public bool IsVip { get; set; }
    public bool HasAllergy { get; set; }
    public string? AllergyNote { get; set; }
    public List<KdsLine> Lines { get; set; } = new();
}

/// <summary>Masa İşlemleri kalem/kişi seçimi satırı (üst satır = ParentLineId null). Checkbox iki yönlü.</summary>
public class LinePickVM : System.ComponentModel.INotifyPropertyChanged
{
    public int LineId { get; }
    public int SeatNo { get; }
    public string SeatText { get; }
    public string ProductName { get; }
    public string QtyText { get; }

    private bool _checked;
    public bool IsChecked { get => _checked; set { _checked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); } }

    public LinePickVM(OrderLine l)
    {
        LineId = l.Id;
        SeatNo = l.SeatNo;
        SeatText = l.SeatText;
        ProductName = l.ProductName ?? "";
        QtyText = $"{l.Qty:0.##}x";
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Kalem seçim overlay'inde koltuk hızlı seçim çipi. SeatNo == -1 → Tümü.</summary>
public class SeatChip(int seatNo, string label)
{
    public int SeatNo { get; } = seatNo;
    public string Label { get; } = label;
}

public class ServiceCard
{
    public int Id { get; }
    public string Icon { get; }
    public string TypeText { get; }
    public string TableText { get; }
    public string ElapsedText { get; }
    public string NoteText { get; }
    public bool HasNote { get; }

    public ServiceCard(ServiceRequest r)
    {
        Id = r.Id;
        Icon = r.Type switch
        {
            ServiceRequestType.CagriGarson => "🔔",
            ServiceRequestType.MasaTemizlik => "🧹",
            ServiceRequestType.Hesap => "🧾",
            _ => "❔"
        };
        TypeText = r.TypeText;
        TableText = (string.IsNullOrWhiteSpace(r.TableCode) ? "Masa —" : $"Masa {r.TableCode}")
            + (string.IsNullOrWhiteSpace(r.ZoneName) ? "" : $" · {r.ZoneName}");
        ElapsedText = r.ElapsedText;
        NoteText = r.Note ?? "";
        HasNote = !string.IsNullOrWhiteSpace(r.Note);
    }
}
