using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>V2 Mutfak ekranı (kanban): Bekleyen → Hazırlanıyor → Hazır → Tamamlandı. Tema-bağımsız (DynamicResource) + ekran içi tema değiştirme.
/// Kart = bir SİPARİŞ (OrderId); her satır kendi durumunu gösterir ve tek tek ilerletilebilir (KISMİ HAZIRLAMA). Kartın kolonu en geri aktif satıra göre belirlenir.</summary>
public partial class KdsWindowV2 : Window
{
    private readonly KdsService _kds = new();
    private readonly OrderService _orders = new();
    private readonly ZoneService _zoneService = new();
    private readonly ReyonService _reyonService = new();
    private readonly int _branchId;
    private DispatcherTimer? _timer;
    private int _filterReyonId;          // 0=Tümü, >0=reyon, -1=Gecikenler, -2=Paket(IsNonTable), -3=VIP
    private bool _rush;
    private int _lastQueued = -1;        // -1 = henüz okunmadı → ilk Load() temel okuma (açılışta ses çalmaz); yalnız ARTIŞTA yeni-sipariş sesi

    private const string ColWait = "#E9A23B", ColPrep = "#3B82F6", ColReady = "#17B26A", ColDone = "#6B7280";

    public KdsWindowV2()
    {
        InitializeComponent();

        // Lisans savunma derinliği (ikinci kat): Mutfak (KDS) modülü pakette yoksa pencereyi açma.
        if (!LicenseGuard.Has(MindAeonRest.Licensing.Feature.Kitchen))
        {
            Dialog.Warn("Mutfak (KDS) modülü lisansınızda yok.", "Lisans");
            Loaded += (_, _) => Close();
            return;
        }

        _branchId = Session.CurrentBranch?.Id ?? 0;
        ServiceText.Text = Session.CurrentBranch?.Name ?? "Servis";
        Loaded += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            Load();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) => { ClockText.Text = DateTime.Now.ToString("HH:mm"); Load(); };
            _timer.Start();
        };
        Closed += (_, _) => _timer?.Stop();
    }

    private Brush Res(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    /// <summary>Filtre çiplerini sayaçlarıyla birlikte kurar. allActive: tüm aktif (Tamamlandı hariç) satırlar.</summary>
    private void BuildTabs(List<KdsLine> allActive)
    {
        // Bir kartın (siparişin) çipe göre sayılması için OrderId bazında gruplayıp say.
        var byOrder = allActive.GroupBy(l => l.OrderId).Select(g => g.ToList()).ToList();

        var tabs = new List<KdsTab>
        {
            new(0, "Tümü", byOrder.Count)
        };

        // Reyon çipleri (sayaç = o reyonda aktif satırı olan kart sayısı)
        var zoneIds = _zoneService.GetAll().Where(z => z.BranchId == _branchId).Select(z => z.Id).ToHashSet();
        foreach (var ry in _reyonService.GetAll().Where(r => zoneIds.Contains(r.ZoneId)))
        {
            int cnt = byOrder.Count(o => o.Any(l => l.ReyonId == ry.Id));
            tabs.Add(new KdsTab(ry.Id, ry.Name, cnt));
        }

        // Kategori çipleri
        tabs.Add(new KdsTab(-2, "📦 Paket", byOrder.Count(o => o.Any(l => l.IsNonTable))));
        tabs.Add(new KdsTab(-3, "⭐ VIP", byOrder.Count(o => o.Any(l => l.IsVip))));
        tabs.Add(new KdsTab(-1, "⏱ Gecikenler", byOrder.Count(o => o.Any(l => l.IsCritical))));

        foreach (var t in tabs)
        {
            bool active = t.ReyonId == _filterReyonId;
            t.Bg = active ? Res("AccentBrush") : Res("SurfaceAltBrush");
            t.Fg = active ? Brushes.White : Res("TextMutedBrush");
        }
        TabHost.ItemsSource = tabs;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        // Filtre değişince kapsam değişir → yeni-sipariş sayacını temel al (filtre geçişi ses tetiklemesin).
        if ((sender as FrameworkElement)?.Tag is KdsTab t) { _filterReyonId = t.ReyonId; _lastQueued = -1; Load(); }
    }

    private void Load()
    {
        try
        {
            // Reyon filtresi DB tarafında; kategori/gecikme filtresi bellek tarafında.
            int dbReyon = _filterReyonId > 0 ? _filterReyonId : 0;
            var all = _kds.GetTickets(_branchId, dbReyon, includeDone: true);

            // Kart bazında filtre uygula: bir siparişin HERHANGİ satırı koşulu sağlıyorsa kartı tut.
            IEnumerable<IGrouping<int, KdsLine>> orderGroups = all.GroupBy(l => l.OrderId);
            orderGroups = _filterReyonId switch
            {
                -1 => orderGroups.Where(g => g.Any(l => l.IsCritical)),
                -2 => orderGroups.Where(g => g.Any(l => l.IsNonTable)),
                -3 => orderGroups.Where(g => g.Any(l => l.IsVip)),
                _ => orderGroups
            };
            var cards = orderGroups.Select(BuildCard).ToList();

            // Kartları kolonlara dağıt (agregat kolon aşamasına göre).
            FillColumn(Col0, cards.Where(c => c.Stage == OrderLineStatus.MutfagaGonderildi), Col0Avg);
            FillColumn(Col1, cards.Where(c => c.Stage == OrderLineStatus.Hazirlaniyor), Col1Avg);
            FillColumn(Col2, cards.Where(c => c.Stage == OrderLineStatus.Hazir), Col2Avg);
            FillColumn(Col3, cards.Where(c => c.Stage == OrderLineStatus.Teslim), Col3Avg);

            // Çip sayaçları için tamamlananlar hariç tüm aktif satırlar (filtreden bağımsız toplam görünüm).
            var allActive = all.Where(l => l.LineStatus is OrderLineStatus.MutfagaGonderildi
                or OrderLineStatus.Hazirlaniyor or OrderLineStatus.Hazir).ToList();
            BuildTabs(allActive);

            // YENİ SİPARİŞ SESİ (polling artış-algılama; SignalR yok) — mutfağa yeni düşen (MutfagaGonderildi) satır sayısı.
            // Sayım GetTickets'in reyon-filtreli verisinden gelir → reyon-özel KDS'te doğal olarak reyon bazlı.
            // İlk Load()'ta _lastQueued=-1 olduğundan ses ÇALMAZ (açılışta geçmiş siparişler için patlama olmaz); yalnız ARTIŞTA çalar.
            int queued = all.Count(l => l.LineStatus == OrderLineStatus.MutfagaGonderildi);
            if (_lastQueued >= 0 && queued > _lastQueued)
            {
                try { System.Media.SystemSounds.Beep.Play(); } catch { }   // çağrı(Exclamation) ve hazır(Asterisk)'tan FARKLI ses
            }
            _lastQueued = queued;

            // İstatistik (filtreden bağımsız, genel)
            StatActive.Text = allActive.Count.ToString();
            var late = allActive.Where(l => l.IsCritical).ToList();
            StatLate.Text = late.Count.ToString();
            StatPrep.Text = allActive.Count > 0 ? $"{(int)allActive.Average(l => l.ElapsedMinutes)} dk" : "0 dk";
            StatCooling.Text = all.Count(l => l.IsCooling).ToString();

            // Kritik şerit
            var crit = late.GroupBy(l => l.OrderId)
                .Select(g => $"{(string.IsNullOrWhiteSpace(g.First().TableCodes) ? g.First().OrderCode : "Masa " + g.First().TableCodes)} · {g.Max(x => x.ElapsedMinutes)}dk")
                .Take(6).ToList();
            if (crit.Count > 0)
            {
                AlertText.Text = $"⚠ {late.GroupBy(l => l.OrderId).Count()} KRİTİK GECİKME";
                AlertChips.ItemsSource = crit;
                AlertBar.Visibility = Visibility.Visible;
            }
            else AlertBar.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Mutfak"); }
    }

    /// <summary>Bir siparişin (OrderId) tüm aktif satırlarından kart üretir; kolonu en geri aktif satıra göre belirler.</summary>
    private KdsCard BuildCard(IGrouping<int, KdsLine> g)
    {
        var f = g.First();
        var lines = g.ToList();
        int elapsed = lines.Max(x => x.ElapsedMinutes);
        bool critical = lines.Any(x => x.IsCritical);

        // Agregat kolon aşaması: herhangi satır=Bekleyen → BEKLEYEN; değilse herhangi=Hazırlanıyor → HAZIRLANIYOR;
        // değilse herhangi=Hazır → HAZIR; aksi halde hepsi Teslim → TAMAMLANDI.
        OrderLineStatus stage =
            lines.Any(l => l.LineStatus == OrderLineStatus.MutfagaGonderildi) ? OrderLineStatus.MutfagaGonderildi :
            lines.Any(l => l.LineStatus == OrderLineStatus.Hazirlaniyor) ? OrderLineStatus.Hazirlaniyor :
            lines.Any(l => l.LineStatus == OrderLineStatus.Hazir) ? OrderLineStatus.Hazir :
            OrderLineStatus.Teslim;

        (string color, string btn, bool action) = stage switch
        {
            OrderLineStatus.MutfagaGonderildi => (ColWait, "Başlat", true),
            OrderLineStatus.Hazirlaniyor => (ColPrep, "Hazır", true),
            OrderLineStatus.Hazir => (ColReady, "Teslim Et", true),
            _ => (ColDone, "", false)
        };

        // Kart soğuyor uyarısı: herhangi satır hazır & ≥10dk alınmamış.
        var coolingLine = lines.FirstOrDefault(l => l.IsCooling);

        return new KdsCard
        {
            OrderId = g.Key,
            Stage = stage,
            TableTitle = string.IsNullOrWhiteSpace(f.TableCodes) ? f.OrderCode : $"Masa {f.TableCodes}",
            TypeLabel = f.TypeLabel,
            TypeBg = TypeColor(f),
            IsNew = elapsed <= 2 && stage == OrderLineStatus.MutfagaGonderildi,
            WaiterName = f.WaiterName ?? "",
            CoverText = f.CoverCount > 0 ? $"👤 {f.CoverCount} kişi" : "",
            ZoneText = f.ZoneName ?? "",
            TimeText = critical ? $"KRİTİK {elapsed}dk" : $"{elapsed} dk",
            TimeBg = critical ? "#33EF4444" : (Res("SurfaceAltBrush") is SolidColorBrush sb ? sb.Color.ToString() : "#22808080"),
            TimeFg = critical ? "#EF4444" : "#9AA0AB",
            HasAllergy = f.HasAllergy,
            AllergyNote = f.AllergyNote,
            HasSpicy = lines.Any(x => x.IsSpicy),
            IsVip = f.IsVip,
            Priority = f.Priority,
            HasNote = !string.IsNullOrWhiteSpace(f.OrderNote),
            NoteText = "📝 " + (f.OrderNote ?? ""),
            IsCooling = coolingLine is not null,
            CoolingText = coolingLine is not null ? $"🔔 Serviste {coolingLine.ElapsedText} — alın, soğuyor" : "",
            Lines = lines,
            ColumnColor = color,
            ButtonText = btn,
            ActionVisible = action
        };
    }

    private string TypeColor(KdsLine l) =>
        l.IsOnline ? "#8B5CF6" :       // ONLINE — mor
        l.IsKiosk ? "#0EA5E9" :        // KIOSK — açık mavi
        l.IsPackage ? "#F59E0B" :      // PAKET — amber
        l.IsTakeaway ? "#10B981" :     // GEL-AL — yeşil
        (Res("AccentBrush") is SolidColorBrush sb ? sb.Color.ToString() : "#3B82F6");   // MASA — accent

    private void FillColumn(ItemsControl host, IEnumerable<KdsCard> cards, TextBlock avgText)
    {
        var list = cards.OrderByDescending(c => c.Lines.Max(l => l.ElapsedMinutes)).ToList();
        host.ItemsSource = list;
        var allLines = list.SelectMany(c => c.Lines).ToList();
        avgText.Text = allLines.Count > 0 ? $"Ort. {(int)allLines.Average(l => l.ElapsedMinutes)} dk" : "";
    }

    /// <summary>Kart-altı toplu buton: o kolon aşamasındaki TÜM uygun satırları birlikte bir aşama ilerletir.</summary>
    private void Advance_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsCard card) return;
        // Yalnızca kartın agregat aşamasındaki satırları ilerlet (kısmen hazır satırlara dokunma).
        var stageLines = card.Lines.Where(l => l.LineStatus == card.Stage).ToList();
        try
        {
            foreach (var l in stageLines)
                _orders.SetLineStatus(l.LineId, NextStatus(l.LineStatus), Session.CurrentUserId);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Mutfak"); }
        Load();
    }

    /// <summary>Per-line: tek bir satırı bir aşama ilerletir (1→5→2→3).</summary>
    private void Line_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsLine line) return;
        if (line.LineStatus == OrderLineStatus.Teslim) return;   // tamamlanmış satır
        try
        {
            _orders.SetLineStatus(line.LineId, NextStatus(line.LineStatus), Session.CurrentUserId);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Mutfak"); }
        Load();
    }

    /// <summary>Aşama akışı: MutfagaGonderildi(1) → Hazirlaniyor(5) → Hazir(2) → Teslim(3).</summary>
    private static OrderLineStatus NextStatus(OrderLineStatus s) => s switch
    {
        OrderLineStatus.MutfagaGonderildi => OrderLineStatus.Hazirlaniyor,
        OrderLineStatus.Hazirlaniyor => OrderLineStatus.Hazir,
        OrderLineStatus.Hazir => OrderLineStatus.Teslim,
        _ => OrderLineStatus.Teslim
    };

    /// <summary>(i) butonu: sipariş detayını (kalemler/notlar/garson) dialog ile göster.</summary>
    private void Info_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsCard card) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{card.TableTitle}  ·  {card.TypeLabel}");
        if (!string.IsNullOrWhiteSpace(card.WaiterName)) sb.AppendLine($"Garson: {card.WaiterName}");
        if (!string.IsNullOrWhiteSpace(card.ZoneText)) sb.AppendLine($"Bölge: {card.ZoneText}");
        if (!string.IsNullOrWhiteSpace(card.CoverText)) sb.AppendLine(card.CoverText);
        if (card.IsVip) sb.AppendLine("⭐ VIP");
        if (card.Priority) sb.AppendLine("⏫ ÖNCELİK");
        if (card.HasAllergy) sb.AppendLine($"⚠ ALERJİ: {card.AllergyNote}");
        if (card.HasNote) sb.AppendLine(card.NoteText);
        sb.AppendLine();
        sb.AppendLine("— KALEMLER —");
        foreach (var l in card.Lines)
        {
            sb.AppendLine($"{l.QtyText} {l.ProductName}  [{l.StatusText}]");
            if (!string.IsNullOrWhiteSpace(l.ReyonName)) sb.AppendLine($"    Reyon: {l.ReyonName}");
            if (l.HasNote) sb.AppendLine($"    📝 {l.KitchenNote}");
            foreach (var ch in l.Children)
            {
                sb.AppendLine($"    • {ch.QtyText} {ch.ProductName}");
                if (ch.HasNote) sb.AppendLine($"        📝 {ch.KitchenNote}");
            }
        }
        Dialog.Info(sb.ToString().TrimEnd(), "Sipariş Detayı");
    }

    private void Rush_Click(object sender, RoutedEventArgs e)
    {
        _rush = !_rush;
        RushBtn.Background = _rush ? Res("DangerBrush") : Res("SurfaceAltBrush");
        RushBtn.Foreground = _rush ? Brushes.White : Res("TextBrush");
        // Rush modunda daha sık yenile
        if (_timer is not null) _timer.Interval = TimeSpan.FromSeconds(_rush ? 7 : 15);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var opts = ThemeManager.Options;
        int idx = opts.FindIndex(o => o.Value == ThemeManager.Current);
        var next = opts[(idx + 1) % opts.Count].Value;
        ThemeManager.ChangeAndSave(next);   // DynamicResource → anında uygulanır
        Load();                              // sekme renkleri + kart renkleri yenilensin
    }

    private void Back_Click(object sender, RoutedEventArgs e) => WaiterTerminalV2Window.LogoutToLogin();
}

public class KdsTab(int reyonId, string label, int count = 0)
{
    public int ReyonId { get; } = reyonId;
    public string Label { get; } = label;
    public int Count { get; } = count;
    public string CountText => Count.ToString();
    public Brush? Bg { get; set; }
    public Brush? Fg { get; set; }
}

public class KdsCard
{
    public int OrderId { get; set; }
    public OrderLineStatus Stage { get; set; }
    public string TableTitle { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string TypeBg { get; set; } = "#3B82F6";
    public bool IsNew { get; set; }
    public string WaiterName { get; set; } = "";
    public string CoverText { get; set; } = "";
    public string ZoneText { get; set; } = "";
    public string TimeText { get; set; } = "";
    public string TimeBg { get; set; } = "#22808080";
    public string TimeFg { get; set; } = "#9AA0AB";
    public bool HasAllergy { get; set; }
    public string? AllergyNote { get; set; }
    public bool HasSpicy { get; set; }
    public bool IsVip { get; set; }
    public bool Priority { get; set; }
    public bool HasNote { get; set; }
    public string NoteText { get; set; } = "";
    public bool IsCooling { get; set; }
    public string CoolingText { get; set; } = "";
    public List<KdsLine> Lines { get; set; } = new();
    public string ColumnColor { get; set; } = "#6B7280";
    public string ButtonText { get; set; } = "";
    public bool ActionVisible { get; set; }
}
