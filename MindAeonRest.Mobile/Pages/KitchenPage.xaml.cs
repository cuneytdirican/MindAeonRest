using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

// ===== KDS V2 görünüm modelleri (WPF KdsWindowV2 / PWA renderKds ile paritede) =====

/// <summary>Üst sekme/aşama (BEKLEYEN/HAZIRLANIYOR/HAZIR/TAMAMLANDI) — sayaçlı.</summary>
public partial class KdsStageVm : ObservableObject
{
    public int Index { get; init; }              // 0..3
    public string Label { get; init; } = "";
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _active;

    public string Display => $"{Label} ({Count})";
    partial void OnCountChanged(int v) => OnPropertyChanged(nameof(Display));

    public Color BgColor => Active ? KdsColor.Accent : KdsColor.SurfaceAlt;
    public Color FgColor => Active ? Colors.White : KdsColor.Text;
    partial void OnActiveChanged(bool v) => RefreshColors();
    /// <summary>Tema değişince hesaplanmış renkleri tazele.</summary>
    public void RefreshColors() { OnPropertyChanged(nameof(BgColor)); OnPropertyChanged(nameof(FgColor)); }
}

/// <summary>Üst filtre çipi (Tümü/Mutfak/Bar/Paket/VIP/Gecikenler) — sayaçlı.</summary>
public partial class KdsFilterVm : ObservableObject
{
    public string Key { get; init; } = "";       // all|kitchen|bar|package|vip|late
    public string Label { get; init; } = "";
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _active;

    public string Display => $"{Label} ({Count})";
    partial void OnCountChanged(int v) => OnPropertyChanged(nameof(Display));

    public Color BgColor => Active ? KdsColor.Accent : KdsColor.SurfaceAlt;
    public Color FgColor => Active ? Colors.White : KdsColor.Text;
    partial void OnActiveChanged(bool v) => RefreshColors();
    /// <summary>Tema değişince hesaplanmış renkleri tazele.</summary>
    public void RefreshColors() { OnPropertyChanged(nameof(BgColor)); OnPropertyChanged(nameof(FgColor)); }
}

/// <summary>Bir kart satırı (üst kalem) — adet+ürün+reyon+mutfak notu + durum noktası.</summary>
public record KdsRowVm(int LineId, string Text, string? Reyon, string? Note, string StatusColorHex)
{
    public Color DotColor => KdsColor.Parse(StatusColorHex);
    public bool HasReyon => !string.IsNullOrWhiteSpace(Reyon);
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>Alt kalem (children).</summary>
public record KdsChildRowVm(string Text, string? Note)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

/// <summary>KDS kartı — bir sipariş satırı (children'larıyla). Aksiyon aşamaya göre değişir.</summary>
public partial class KdsCardVm : ObservableObject
{
    public int LineId { get; init; }
    public int OrderId { get; init; }
    public int Stage { get; init; }              // 0 bekleyen,1 hazırlanıyor,2 hazır,3 tamamlandı

    public string TitleText { get; init; } = "";     // masa / sipariş kodu
    public string TypeLabel { get; init; } = "";     // MASA/GEL-AL/PAKET/ONLINE/KIOSK
    public Color TypeColor { get; init; } = KdsColor.Accent;

    public string ElapsedText { get; init; } = "";
    public bool IsCritical { get; init; }
    public Color ElapsedColor => IsCritical ? KdsColor.Danger : KdsColor.Muted;

    public string MetaText { get; init; } = "";      // garson · kişi · bölge

    public bool IsVip { get; init; }
    public bool Priority { get; init; }
    public bool IsSpicy { get; init; }
    public bool HasAllergy { get; init; }
    public string AllergyText { get; init; } = "";

    public bool IsCooling { get; init; }
    public string CoolingText { get; init; } = "";

    public List<KdsRowVm> Rows { get; init; } = new();
    public List<KdsChildRowVm> Children { get; init; } = new();
    public bool HasChildren => Children.Count > 0;

    public string ActionText { get; init; } = "";
    public Color ActionColor { get; init; } = KdsColor.Accent;
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionText);

    public Color AccentColor => IsCritical ? KdsColor.Danger
        : IsVip ? KdsColor.Purple
        : Stage switch { 2 => KdsColor.Success, 1 => KdsColor.Info, 3 => KdsColor.Muted, _ => KdsColor.Warning };

    public bool ShowMeta => !string.IsNullOrWhiteSpace(MetaText);
    public bool ShowBadges => IsVip || Priority || IsSpicy || HasAllergy;
}

/// <summary>Tema renklerini koddan okur (DynamicResource olmayan hesaplanmış değerler için).</summary>
internal static class KdsColor
{
    public static Color Get(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Gray;
    public static Color Parse(string? hex) => string.IsNullOrWhiteSpace(hex) ? Muted : Color.FromArgb(hex);

    public static Color Accent => Get("Accent");
    public static Color SurfaceAlt => Get("SurfaceAlt");
    public static Color Text => Get("TextColor");
    public static Color Muted => Get("TextMuted");
    public static Color Danger => Get("Danger");
    public static Color Success => Get("Success");
    public static Color Warning => Get("Warning");
    public static Color Purple => Get("Purple");
    public static Color Info => Color.FromArgb("#3B82F6");   // hazırlanıyor (PWA/WPF ile aynı)
}

public partial class KitchenPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly HubService _hub;
    private IDispatcherTimer? _timer;

    private List<KdsLineDto> _all = new();
    private readonly ObservableCollection<KdsStageVm> _stages = new();
    private readonly ObservableCollection<KdsFilterVm> _filters = new();
    private int _activeStage;                 // 0 bekleyen,1 hazırlanıyor,2 hazır,3 tamamlandı
    private string _activeFilter = "all";

    // Satır durumu (LineStatus) → aşama (sekme). 0/1(Mutfakta)→Bekleyen, 5→Hazırlanıyor, 2→Hazır, 3→Tamamlandı
    private static int StageForStatus(int status) => status switch { 5 => 1, 2 => 2, 3 => 3, _ => 0 };

    public KitchenPage(AppState state, ApiClient api, HubService hub)
    {
        InitializeComponent();
        _state = state; _api = api; _hub = hub;
        SubTitle.Text = $"{_state.Login?.BranchName}  ·  {_state.Login?.FullName}";

        for (int i = 0; i < 4; i++)
            _stages.Add(new KdsStageVm { Index = i, Label = StageLabel(i), Active = i == 0 });
        StagesView.ItemsSource = _stages;

        void AddF(string k, string l) => _filters.Add(new KdsFilterVm { Key = k, Label = l, Active = k == "all" });
        AddF("all", "Tümü");
        AddF("kitchen", "🍳 Mutfak");
        AddF("bar", "🥤 Bar");
        AddF("package", "📦 Paket");
        AddF("vip", "⭐ VIP");
        AddF("late", "⏰ Gecikenler");
        FiltersView.ItemsSource = _filters;
    }

    private static string StageLabel(int i) => i switch
    {
        1 => "HAZIRLANIYOR", 2 => "HAZIR", 3 => "TAMAMLANDI", _ => "BEKLEYEN"
    };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.NewTicket += OnNewTicket;
        _hub.TableChanged += OnNewTicket;
        _hub.TicketReady += OnTicketReady;
        MauiTheme.Changed += OnThemeChanged;
        await _hub.EnsureConnectedAsync();
        UpdateLiveDot();
        await Load();

        // Süre/renk canlılığı + SignalR kaçaklarına karşı yedek tazeleme
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(15);
        _timer.Tick += async (_, _) => { UpdateLiveDot(); await Load(); };
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.NewTicket -= OnNewTicket;
        _hub.TableChanged -= OnNewTicket;
        _hub.TicketReady -= OnTicketReady;
        MauiTheme.Changed -= OnThemeChanged;
        _timer?.Stop();
    }

    private async void OnNewTicket() => await Load();
    private async void OnTicketReady(int lineId) => await Load();

    private void OnThemeChanged()
    {
        // Hesaplanmış renkler (çip/sekme/kart accent) DynamicResource değil → elle tazele
        foreach (var s in _stages) s.RefreshColors();
        foreach (var f in _filters) f.RefreshColors();
        Render();
    }

    private void UpdateLiveDot() =>
        LiveDot.TextColor = _hub.IsConnected ? Color.FromArgb("#8CF0B6") : Color.FromArgb("#FF8A8A");

    private async Task Load()
    {
        try
        {
            Refresher.IsRefreshing = true;
            // includeDone=true → Tamamlandı sütunu için son teslimler de gelir
            _all = await _api.KitchenTicketsAsync(includeDone: true);
            UpdateStats();
            UpdateCounts();
            Render();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
        finally { Refresher.IsRefreshing = false; }
    }

    // ===== İstatistik başlığı =====
    private void UpdateStats()
    {
        // Aktif = bekleyen + hazırlanıyor + hazır (teslim hariç)
        var active = _all.Where(l => l.LineStatus is 1 or 5 or 2).ToList();
        int aktif = active.Count;
        int geciken = active.Count(l => l.IsCritical);
        int soguyor = _all.Count(l => l.IsCooling);

        // Ort. hazırlık: hazır/teslim olmuş kalemlerin gönderimden bu yana ort. dakikası (kaba ölçü)
        var prepared = _all.Where(l => l.LineStatus is 2 or 3 && l.ElapsedMinutes > 0).ToList();
        int ort = prepared.Count > 0 ? (int)Math.Round(prepared.Average(l => l.ElapsedMinutes)) : 0;

        StatActive.Text = aktif.ToString();
        StatLate.Text = geciken.ToString();
        StatAvg.Text = ort.ToString();
        StatCooling.Text = soguyor.ToString();
    }

    // ===== Sekme/filtre sayaçları =====
    private void UpdateCounts()
    {
        foreach (var s in _stages)
            s.Count = FilteredByChip(_all).Count(l => StageForStatus(l.LineStatus) == s.Index);

        var stageRows = _all.Where(l => StageForStatus(l.LineStatus) == _activeStage).ToList();
        foreach (var f in _filters)
            f.Count = ApplyChip(stageRows, f.Key).Count();
    }

    private IEnumerable<KdsLineDto> FilteredByChip(IEnumerable<KdsLineDto> src) => ApplyChip(src, _activeFilter);

    private static IEnumerable<KdsLineDto> ApplyChip(IEnumerable<KdsLineDto> src, string key) => key switch
    {
        "kitchen" => src.Where(l => l.StationType == 0),
        "bar" => src.Where(l => l.StationType == 1),
        "package" => src.Where(l => l.IsNonTable),
        "vip" => src.Where(l => l.IsVip),
        "late" => src.Where(l => l.IsCritical),
        _ => src,
    };

    // ===== Render =====
    private void Render()
    {
        var rows = FilteredByChip(_all)
            .Where(l => StageForStatus(l.LineStatus) == _activeStage)
            .OrderByDescending(l => l.IsCritical)
            .ThenByDescending(l => l.IsVip || l.Priority)
            .ThenBy(l => l.SentAt ?? DateTime.MaxValue)
            .Select(ToCard)
            .ToList();
        CardsView.ItemsSource = rows;

        EmptyLabel.Text = _activeStage switch
        {
            0 => "Bekleyen sipariş yok 🎉",
            1 => "Hazırlanan kalem yok.",
            2 => "Hazır bekleyen kalem yok.",
            _ => "Tamamlanan kalem yok."
        };
    }

    private KdsCardVm ToCard(KdsLineDto l)
    {
        int stage = StageForStatus(l.LineStatus);

        // Aksiyon: 0→Başlat, 1→Hazır, 2→Teslim Et, 3 yok
        var (actText, actColor) = stage switch
        {
            0 => ("▶ Başlat", KdsColor.Info),
            1 => ("✓ Hazır", KdsColor.Success),
            2 => ("📦 Teslim Et", KdsColor.Accent),
            _ => ("", KdsColor.Muted),
        };

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.WaiterName)) meta.Add($"👤 {l.WaiterName}");
        if (l.CoverCount > 0) meta.Add($"{l.CoverCount} kişi");
        if (!string.IsNullOrWhiteSpace(l.ZoneName)) meta.Add(l.ZoneName!);

        var rows = new List<KdsRowVm>
        {
            new(l.LineId, $"{l.Qty:0.##}x {l.ProductName}", l.ReyonName, l.KitchenNote, l.StatusColor ?? "#E9A23B")
        };

        var children = (l.Children ?? new())
            .Select(c => new KdsChildRowVm($"+ {c.Qty:0.##}x {c.ProductName}", c.KitchenNote))
            .ToList();

        return new KdsCardVm
        {
            LineId = l.LineId,
            OrderId = l.OrderId,
            Stage = stage,
            TitleText = string.IsNullOrWhiteSpace(l.TableCodes) ? l.OrderCode : l.TableCodes!,
            TypeLabel = l.TypeLabel ?? "MASA",
            TypeColor = TypeColorFor(l),
            ElapsedText = l.ElapsedText ?? "",
            IsCritical = l.IsCritical,
            MetaText = string.Join("  ·  ", meta),
            IsVip = l.IsVip,
            Priority = l.Priority,
            IsSpicy = l.IsSpicy,
            HasAllergy = l.HasAllergy,
            AllergyText = "⚠ " + (l.AllergyNote ?? "Alerji"),
            IsCooling = l.IsCooling,
            CoolingText = $"🔔 Serviste {l.ElapsedText} — soğuyor",
            Rows = rows,
            Children = children,
            ActionText = actText,
            ActionColor = actColor,
        };
    }

    private static Color TypeColorFor(KdsLineDto l) => l.TypeLabel switch
    {
        "ONLINE" => KdsColor.Purple,
        "PAKET" => KdsColor.Info,
        "GEL-AL" => KdsColor.Warning,
        "KIOSK" => KdsColor.Success,
        _ => KdsColor.Accent,   // MASA
    };

    // ===== Sekme / çip dokunuşları =====
    private void Stage_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not KdsStageVm s) return;
        _activeStage = s.Index;
        foreach (var x in _stages) x.Active = x.Index == _activeStage;
        UpdateCounts();
        Render();
    }

    private void Filter_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not KdsFilterVm f) return;
        _activeFilter = f.Key;
        foreach (var x in _filters) x.Active = x.Key == _activeFilter;
        UpdateCounts();
        Render();
    }

    // ===== Aksiyonlar =====
    // Kart-altı buton: aşamaya göre bir adım ilerlet.
    private async void CardAction_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not KdsCardVm c) return;
        await Advance(c.LineId, c.Stage);
    }

    // Satıra dokun → o satırı bir aşama ilerlet (kısmi onay).
    private async void Row_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not KdsRowVm r) return;
        var line = _all.FirstOrDefault(l => l.LineId == r.LineId);
        if (line is null) return;
        await Advance(r.LineId, StageForStatus(line.LineStatus));
    }

    private async Task Advance(int lineId, int fromStage)
    {
        try
        {
            switch (fromStage)
            {
                case 0: await _api.KitchenStartAsync(lineId); break;     // Bekleyen → Hazırlanıyor
                case 1: await _api.KitchenReadyAsync(lineId); break;     // Hazırlanıyor → Hazır
                case 2: await _api.KitchenDeliveredAsync(lineId); break; // Hazır → Tamamlandı
                default: return;                                         // Tamamlandı: işlem yok
            }
            await Load();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Refresh_Clicked(object sender, EventArgs e) => await Load();

    private void Theme_Clicked(object sender, EventArgs e) => MauiTheme.Cycle();

    private async void Logout_Clicked(object sender, EventArgs e)
    {
        _timer?.Stop();
        // Merkezi çıkış: SignalR durdurma + token/AppState temizleme + LoginPage.
        if (Application.Current is App app) await app.LogoutAsync();
    }
}
