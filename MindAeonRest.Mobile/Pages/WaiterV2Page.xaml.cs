using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

// ===== V2 görünüm modelleri =====

/// <summary>Masalar sekmesindeki filtre çipi.</summary>
public partial class FilterChipVm : ObservableObject
{
    public string Key { get; init; } = "";       // "all" | "zone:<id>" | "dolu" | "bekleyen" | "rezerve" | "bos" | "geciken"
    public string Label { get; init; } = "";
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _active;

    public string Display => Count > 0 ? $"{Label} ({Count})" : Label;
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));

    public Color BgColor => Active ? AppColor("Accent") : AppColor("SurfaceAlt");
    public Color FgColor => Active ? Colors.White : AppColor("TextColor");
    partial void OnActiveChanged(bool value) { OnPropertyChanged(nameof(BgColor)); OnPropertyChanged(nameof(FgColor)); }

    internal static Color AppColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Gray;
}

/// <summary>"Bekleyen işler" mini liste öğesi.</summary>
public record PendingItemVm(string Text);

/// <summary>"Tümünü Gör" ile açılan tam sipariş satırı.</summary>
public record OrderLineRowVm(string Text, string? StatusText)
{
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
}

/// <summary>Masa kartı (zengin) — Masalar sekmesi.</summary>
public partial class TableV2Vm : ObservableObject
{
    public int TableId { get; init; }
    public int ZoneId { get; init; }
    public string ZoneName { get; init; } = "";
    public string Display { get; init; } = "";        // "Masa 2"
    public int Status { get; init; }                  // 0 boş,1 dolu,2 rezerve
    public int Covers { get; init; }
    public string ElapsedText { get; init; } = "";
    public string? Note { get; init; }
    public string? Allergy { get; init; }
    public string OrderSummary { get; init; } = "";
    public string AmountText { get; init; } = "";

    public int? OrderId { get; init; }
    public decimal AmountValue { get; init; }
    public string LastOrderText { get; init; } = "";  // "Son sipariş: 14:32"
    public List<OrderLineRowVm> AllLines { get; init; } = new();

    public bool HasOrderPending { get; init; }
    public bool HasBeveragePending { get; init; }
    public bool IsCalling { get; init; }
    public List<PendingItemVm> Pending { get; init; } = new();

    [ObservableProperty] private bool _expanded;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public string NoteText => "📝 " + Note;
    public bool HasAllergy => !string.IsNullOrWhiteSpace(Allergy);
    public string AllergyText => "⚠ Alerji: " + Allergy;
    public bool HasPending => Pending.Count > 0;
    public bool HasCovers => Covers > 0;
    public bool HasAmount => !string.IsNullOrWhiteSpace(AmountText);
    public bool HasSummary => !string.IsNullOrWhiteSpace(OrderSummary);

    // "Tümünü Gör" sadece dolu / siparişi olan masada anlamlı
    public bool CanExpand => AllLines.Count > 0;
    public bool HasLastOrder => !string.IsNullOrWhiteSpace(LastOrderText);
    public bool HasActions => Status == 1 || OrderId is not null;
    // Ödeme yetkisi: tutar/durum VE rol (Kasiyer/SysAdmin/Admin). Garson'da "Hesap Al" gizlenir.
    public bool CanPayRole { get; init; }
    public bool CanPay => OrderId is not null && AmountValue > 0 && CanPayRole;

    public string ExpandLabel => Expanded ? "Kapat ▾" : "Tümünü Gör ›";
    partial void OnExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandLabel));
        OnPropertyChanged(nameof(ShowExpanded));
        OnPropertyChanged(nameof(ShowSummary));
    }
    public bool ShowExpanded => Expanded && CanExpand;
    // Özet satırı sadece kapalıyken görünür (açıkken tam liste var)
    public bool ShowSummary => HasSummary && !Expanded;

    public string ZoneCaps => (ZoneName ?? "").ToUpperInvariant();

    public bool BadgeOrder => HasOrderPending;
    public bool BadgeBeverage => HasBeveragePending;
    public bool BadgeCall => IsCalling;

    // Aciliyet rengi: çağrı > sipariş bekleyen > meşrubat > dolu > rezerve > boş
    public Color AccentColor => IsCalling ? FilterChipVm.AppColor("Danger")
        : HasOrderPending ? FilterChipVm.AppColor("Warning")
        : HasBeveragePending ? FilterChipVm.AppColor("Purple")
        : Status == 1 ? FilterChipVm.AppColor("Accent")
        : Status == 2 ? FilterChipVm.AppColor("PurpleDeep")
        : FilterChipVm.AppColor("BorderColor");
}

/// <summary>Teslim / meşrubat / servis kartı (gruplanmış).</summary>
public partial class DeliverGroupVm : ObservableObject
{
    public string ZoneName { get; init; } = "";
    public string TableName { get; init; } = "";
    public int Covers { get; init; }
    public string ElapsedText { get; init; } = "";
    public List<int> LineIds { get; init; } = new();
    public ObservableCollection<DeliverItemVm> Items { get; init; } = new();
    public string ButtonText => $"Teslim Et • {LineIds.Count}";
    public bool HasCovers => Covers > 0;
    public string ZoneCaps => (ZoneName ?? "").ToUpperInvariant();
}

public record DeliverItemVm(string Text, string? Modifiers)
{
    public bool HasModifiers => !string.IsNullOrWhiteSpace(Modifiers);
}

/// <summary>Masasız (paket / gel-al / self-servis) ödeme bekleyen sipariş kartı — kasiyer bulup ödeyebilsin.</summary>
public partial class NonTableOrderVm : ObservableObject
{
    public int OrderId { get; init; }
    public string Label { get; init; } = "";        // DisplayLabel: teslim adı ?? adisyon kodu
    public string Channel { get; init; } = "";       // ChannelText: "Paket" / "Gel-Al / Self-Servis" / "Kiosk"
    public string OrderCode { get; init; } = "";
    public string? WaiterName { get; init; }
    public string ElapsedText { get; init; } = "";
    public int Covers { get; init; }
    public decimal AmountValue { get; init; }        // Kalan (Total - Paid)
    public string AmountText { get; init; } = "";
    public bool HasBillRequest { get; init; }
    public bool IsPartial { get; init; }
    public bool CanPayRole { get; init; }

    public bool CanPay => AmountValue > 0 && CanPayRole;
    public bool HasCovers => Covers > 0;
    public bool HasElapsed => !string.IsNullOrWhiteSpace(ElapsedText);
    public bool HasWaiter => !string.IsNullOrWhiteSpace(WaiterName);
    public string ChannelCaps => (Channel ?? "").ToUpperInvariant();
    public string SubText => $"Adisyon {OrderCode}" + (HasWaiter ? $" · {WaiterName}" : "");
    public string StatusText => IsPartial ? "Kısmi Ödendi" : (HasBillRequest ? "Ödeme Bekliyor" : "Açık");
    public bool ShowStatus => IsPartial || HasBillRequest;
    public Color StatusColor => IsPartial ? FilterChipVm.AppColor("Warning")
        : HasBillRequest ? FilterChipVm.AppColor("Danger")
        : FilterChipVm.AppColor("Accent");
}

/// <summary>Yeni servis kartı (masa temizliği / çağrı).</summary>
public partial class ServiceCardVm : ObservableObject
{
    public int Id { get; init; }
    public string ZoneName { get; init; } = "";
    public string TableName { get; init; } = "";
    public string Caption { get; init; } = "";
    public string ElapsedText { get; init; } = "";
    public string ZoneCaps => (ZoneName ?? "").ToUpperInvariant();
}

public partial class WaiterV2Page : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly HubService _hub;

    private List<ZoneDto> _zones = new();
    private List<TableV2Vm> _allTables = new();
    private List<KdsLineDto> _tickets = new();
    private List<ServiceRequestDto> _services = new();
    private List<UnpaidTableDto> _unpaid = new();

    private readonly ObservableCollection<FilterChipVm> _filters = new();
    private string _activeFilter = "all";
    private int _activeTab; // 0 Masalar,1 Teslim,2 Meşrubat,3 Servis
    private readonly HashSet<int> _expandedTables = new();  // "Tümünü Gör" açık kalan masalar

    public WaiterV2Page(AppState state, ApiClient api, HubService hub)
    {
        InitializeComponent();
        _state = state; _api = api; _hub = hub;

        BrandLabel.Text = _state.Login?.CompanyName ?? "MindAeonRest";
        BranchLabel.Text = _state.Login?.BranchName ?? "";
        UserLabel.Text = _state.Login?.FullName ?? "";
        RoleLabel.Text = _state.Login?.Roles is { Count: > 0 } r ? r[0] : "Garson";
        ThemeBtn.Text = MauiTheme.CurrentGlyph;

        FiltersView.ItemsSource = _filters;
        SetTab(0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.TableChanged += OnAnyChanged;
        _hub.ServiceChanged += OnAnyChanged;
        _hub.NewTicket += OnAnyChanged;
        _hub.TicketReady += OnTicketReady;
        MauiTheme.Changed += OnThemeChanged;
        await _hub.EnsureConnectedAsync();
        await LoadAll();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.TableChanged -= OnAnyChanged;
        _hub.ServiceChanged -= OnAnyChanged;
        _hub.NewTicket -= OnAnyChanged;
        _hub.TicketReady -= OnTicketReady;
        MauiTheme.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        ThemeBtn.Text = MauiTheme.CurrentGlyph;
        // Çiplerin/kartların renkleri DynamicResource olmayan hesaplanmış değerler içerdiğinden tazele
        RebuildFilters();
        RenderActiveTab();
    }

    // Ödeme yetkili rol mü? (Kasiyer/SysAdmin/Admin, case-insensitive) — "Hesap Al" yalnız bu rolde görünür.
    private bool CanPay => _state.Login?.Roles?.Any(r =>
        r.Equals("Kasiyer", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) == true;

    private async void OnAnyChanged() => await LoadAll();
    private async void OnTicketReady(int lineId) => await LoadAll();

    private void ShowError(string? msg)
    {
        ErrorLabel.Text = msg ?? "";
        ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(msg);
    }

    private async Task LoadAll()
    {
        try
        {
            ShowError(null);
            if (_zones.Count == 0)
            {
                try { _zones = await _api.GetAsync<List<ZoneDto>>("/api/tables/zones"); } catch { _zones = new(); }
            }

            var tables = new List<TableV2Vm>();
            try { _unpaid = await _api.UnpaidTablesAsync(); } catch { _unpaid = new(); }
            try { _services = await _api.ServiceRequestsAsync(); } catch { _services = new(); }
            try { _tickets = await _api.KitchenTicketsAsync(false); } catch { _tickets = new(); }

            foreach (var z in _zones)
            {
                List<TableCardDto> board;
                try { board = await _api.GetAsync<List<TableCardDto>>($"/api/tables/board?zoneId={z.Id}"); }
                catch { continue; }

                foreach (var t in board)
                    tables.Add(BuildTableVm(z, t));
            }
            _allTables = tables;

            RebuildFilters();
            RenderActiveTab();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private TableV2Vm BuildTableVm(ZoneDto z, TableCardDto t)
    {
        var unpaid = t.OrderId is { } oid ? _unpaid.FirstOrDefault(u => u.OrderId == oid) : null;
        var svcForTable = _services.Where(s => s.TableId == t.TableId).ToList();
        bool calling = svcForTable.Any(s => s.Type == 0 && s.Status == 0);

        // Bu masaya ait mutfak/bar satırları
        var lines = t.OrderId is { } oid2 ? _tickets.Where(k => k.OrderId == oid2).ToList() : new List<KdsLineDto>();
        bool orderPending = lines.Any(l => l.LineStatus is 0 or 1 or 5);                 // beklemede/mutfakta/hazırlanıyor
        bool bevReady = lines.Any(l => l.StationType == 1 && l.LineStatus == 2);          // hazır meşrubat

        var pending = new List<PendingItemVm>();
        foreach (var l in lines.Where(l => l.LineStatus is 0 or 1 or 2 or 5))
        {
            var mins = l.SentAt is { } s ? (int)Math.Max(0, (DateTime.UtcNow - s).TotalMinutes) : 0;
            var st = l.LineStatus switch { 2 => "hazır", 1 => "mutfakta", 5 => "hazırlanıyor", _ => "bekliyor" };
            pending.Add(new PendingItemVm($"{l.Qty:0.##}x {l.ProductName} · {st} · {mins} dk"));
        }

        // Sipariş özeti (ilk ürün + "+N ürün")
        string summary = "";
        if (lines.Count > 0)
        {
            var first = lines[0];
            summary = $"{first.Qty:0.##}x {first.ProductName}";
            if (lines.Count > 1) summary += $" · +{lines.Count - 1} ürün";
        }

        // "Tümünü Gör" için tüm satırlar + son sipariş saati
        static string LineStatusText(int s) => s switch
        {
            2 => "hazır", 1 => "mutfakta", 3 => "teslim", 5 => "hazırlanıyor", _ => "bekliyor"
        };
        var allLines = lines
            .Select(l => new OrderLineRowVm($"{l.Qty:0.##}x {l.ProductName}", LineStatusText(l.LineStatus)))
            .ToList();

        string lastOrder = "";
        var lastSent = lines.Where(l => l.SentAt is not null).Select(l => l.SentAt!.Value).DefaultIfEmpty().Max();
        if (lastSent != default)
        {
            var local = lastSent.Kind == DateTimeKind.Utc ? lastSent.ToLocalTime() : lastSent;
            lastOrder = $"Son sipariş: {local:HH:mm}";
        }

        // Alerji notu (KDS satırından)
        var allergy = lines.Select(l => l.AllergyNote).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        int covers = unpaid?.CoverCount ?? t.Seats;
        string elapsed = "";
        var opened = t.OpenedAt ?? unpaid?.OpenedAt;
        if (opened is { } o)
        {
            var span = DateTime.UtcNow - (o.Kind == DateTimeKind.Utc ? o : o.ToUniversalTime());
            if (span.TotalMinutes >= 0) elapsed = span.TotalMinutes >= 60 ? $"{(int)span.TotalHours}s {span.Minutes}dk" : $"{(int)span.TotalMinutes} dk";
        }

        decimal amt = unpaid is not null ? (unpaid.Total - unpaid.Paid) : t.Amount;

        return new TableV2Vm
        {
            TableId = t.TableId,
            ZoneId = z.Id,
            ZoneName = z.Name,
            Display = t.Display,
            Status = t.Status,
            Covers = covers,
            ElapsedText = elapsed,
            Note = lines.Select(l => l.OrderNote).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            Allergy = allergy,
            OrderSummary = summary,
            AmountText = amt > 0 ? $"₺{amt:N2}" : "",
            OrderId = t.OrderId,
            AmountValue = amt,
            LastOrderText = lastOrder,
            AllLines = allLines,
            HasOrderPending = orderPending,
            HasBeveragePending = bevReady,
            IsCalling = calling,
            Pending = pending,
            CanPayRole = CanPay,
            Expanded = _expandedTables.Contains(t.TableId) && allLines.Count > 0,
        };
    }

    // ===== Filtreler =====
    private void RebuildFilters()
    {
        _filters.Clear();
        void Add(string key, string label, int count) =>
            _filters.Add(new FilterChipVm { Key = key, Label = label, Count = count, Active = key == _activeFilter });

        Add("all", "Tümü", _allTables.Count);
        foreach (var z in _zones)
            Add($"zone:{z.Id}", z.Name, _allTables.Count(t => t.ZoneId == z.Id));
        Add("dolu", "Dolu", _allTables.Count(t => t.Status == 1));
        Add("bekleyen", "Sipariş Bekleyen", _allTables.Count(t => t.HasOrderPending));
        Add("rezerve", "Rezerve", _allTables.Count(t => t.Status == 2));
        Add("bos", "Boş", _allTables.Count(t => t.Status == 0));
        Add("geciken", "Geciken", _allTables.Count(t => t.Pending.Any(p => p.Text.Contains(" dk") && ParseMin(p.Text) > 10)));
    }

    private static int ParseMin(string text)
    {
        // "... · 12 dk" -> 12
        var idx = text.LastIndexOf("· ", StringComparison.Ordinal);
        if (idx < 0) return 0;
        var tail = text[(idx + 2)..].Replace(" dk", "").Trim();
        return int.TryParse(tail, out var m) ? m : 0;
    }

    private IEnumerable<TableV2Vm> Filtered()
    {
        return _activeFilter switch
        {
            "all" => _allTables,
            "dolu" => _allTables.Where(t => t.Status == 1),
            "bekleyen" => _allTables.Where(t => t.HasOrderPending),
            "rezerve" => _allTables.Where(t => t.Status == 2),
            "bos" => _allTables.Where(t => t.Status == 0),
            "geciken" => _allTables.Where(t => t.Pending.Any(p => ParseMin(p.Text) > 10)),
            _ when _activeFilter.StartsWith("zone:") && int.TryParse(_activeFilter[5..], out var zid) =>
                _allTables.Where(t => t.ZoneId == zid),
            _ => _allTables,
        };
    }

    private void Filter_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not FilterChipVm chip) return;
        _activeFilter = chip.Key;
        foreach (var f in _filters) f.Active = f.Key == _activeFilter;
        RenderTables();
    }

    // ===== Sekmeler =====
    private void Tab_Tables(object sender, EventArgs e) => SetTab(0);
    private void Tab_Deliver(object sender, EventArgs e) => SetTab(1);
    private void Tab_Beverage(object sender, EventArgs e) => SetTab(2);
    private void Tab_Service(object sender, EventArgs e) => SetTab(3);
    private void Tab_NonTable(object sender, EventArgs e) => SetTab(4);

    private void SetTab(int tab)
    {
        _activeTab = tab;
        TablesTab.IsVisible = tab == 0;
        DeliverTab.IsVisible = tab == 1;
        BeverageTab.IsVisible = tab == 2;
        ServiceTab.IsVisible = tab == 3;
        NonTableTab.IsVisible = tab == 4;

        StyleTabBtn(TabTablesBtn, tab == 0);
        StyleTabBtn(TabDeliverBtn, tab == 1);
        StyleTabBtn(TabBeverageBtn, tab == 2);
        StyleTabBtn(TabServiceBtn, tab == 3);
        StyleTabBtn(TabPkgBtn, tab == 4);
        RenderActiveTab();
    }

    private static void StyleTabBtn(Button b, bool active)
    {
        b.BackgroundColor = active ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("SurfaceAlt");
        b.TextColor = active ? Colors.White : FilterChipVm.AppColor("TextColor");
    }

    private void RenderActiveTab()
    {
        UpdateTabCounts();
        switch (_activeTab)
        {
            case 0: RenderTables(); break;
            case 1: RenderDeliver(0); break;
            case 2: RenderDeliver(1); break;
            case 3: RenderService(); break;
            case 4: RenderNonTable(); break;
        }
    }

    private void UpdateTabCounts()
    {
        int deliver = _tickets.Count(l => l.StationType == 0 && l.LineStatus == 2);
        int bev = _tickets.Count(l => l.StationType == 1 && l.LineStatus == 2);
        int svc = _services.Count(s => (s.Type == 1 || s.Type == 0) && s.Status == 0);
        int pkg = _unpaid.Count(u => u.NonTable);
        TabTablesBtn.Text = $"Masalar ({_allTables.Count})";
        TabDeliverBtn.Text = $"Sipariş Teslim ({deliver})";
        TabBeverageBtn.Text = $"Meşrubatlar ({bev})";
        TabServiceBtn.Text = $"Yeni Servis ({svc})";
        TabPkgBtn.Text = $"Paket/Gel-Al ({pkg})";
    }

    private void RenderTables()
    {
        // butonların stilleri kod ile atandığı için tazele
        StyleTabBtn(TabTablesBtn, _activeTab == 0);
        foreach (var f in _filters) { f.Active = f.Key == _activeFilter; }
        TablesView.ItemsSource = Filtered().ToList();
    }

    private void RenderDeliver(int stationType)
    {
        var ready = _tickets.Where(l => l.StationType == stationType && l.LineStatus == 2).ToList();
        var groups = ready
            .GroupBy(l => l.OrderId)
            .Select(g =>
            {
                var f = g.First();
                var vm = new DeliverGroupVm
                {
                    ZoneName = f.ZoneName ?? "",
                    TableName = f.TableCodes ?? f.OrderCode,
                    Covers = f.CoverCount,
                    ElapsedText = f.SentAt is { } s ? $"{(int)Math.Max(0, (DateTime.UtcNow - s).TotalMinutes)} dk" : "",
                    LineIds = g.Select(x => x.LineId).ToList(),
                };
                foreach (var l in g)
                {
                    var mods = l.Children is { Count: > 0 }
                        ? string.Join(", ", l.Children.Select(c => $"{c.Qty:0.##}x {c.ProductName}"))
                        : l.KitchenNote;
                    vm.Items.Add(new DeliverItemVm($"{l.Qty:0.##}x {l.ProductName}", mods));
                }
                return vm;
            }).ToList();

        if (stationType == 0) DeliverView.ItemsSource = groups;
        else BeverageView.ItemsSource = groups;
    }

    private void RenderService()
    {
        var cards = _services
            .Where(s => (s.Type == 1 || s.Type == 0) && s.Status == 0)
            .Select(s => new ServiceCardVm
            {
                Id = s.Id,
                ZoneName = s.ZoneName ?? "",
                TableName = s.TableCode ?? "Masa",
                Caption = s.Type == 1 ? "Servis açılışı / masa temizliği bekliyor" : "Masaya çağırıyor",
                ElapsedText = s.ElapsedText,
            }).ToList();
        ServiceView.ItemsSource = cards;
    }

    // Masasız (paket / gel-al / self-servis) ödeme bekleyen siparişler — masa panosunda görünmez, burada listelenir.
    private void RenderNonTable()
    {
        var cards = _unpaid
            .Where(u => u.NonTable)
            .OrderByDescending(u => u.HasBillRequest)          // "Hesap istiyor" en üste
            .ThenBy(u => u.OpenedAt)
            .Select(u => new NonTableOrderVm
            {
                OrderId = u.OrderId,
                Label = u.Label,
                Channel = u.Channel,
                OrderCode = u.OrderCode,
                WaiterName = u.WaiterName,
                Covers = u.CoverCount,
                ElapsedText = ElapsedFrom(u.OpenedAt),
                AmountValue = u.Remaining,
                AmountText = u.Remaining > 0 ? $"₺{u.Remaining:N2}" : $"₺{u.Total:N2}",
                HasBillRequest = u.HasBillRequest,
                IsPartial = u.IsPartial,
                CanPayRole = CanPay,
            }).ToList();
        NonTableView.ItemsSource = cards;
    }

    // OpenedAt (UTC/Unspecified) → "12 dk" / "1s 5dk" — masa kartlarındaki hesapla aynı.
    private static string ElapsedFrom(DateTime opened)
    {
        var span = DateTime.UtcNow - (opened.Kind == DateTimeKind.Utc ? opened : opened.ToUniversalTime());
        if (span.TotalMinutes < 0) return "";
        return span.TotalMinutes >= 60 ? $"{(int)span.TotalHours}s {span.Minutes}dk" : $"{(int)span.TotalMinutes} dk";
    }

    // ===== Eylemler =====
    private async void Table_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not TableV2Vm t) return;
        await OpenMenu(t);
    }

    private Task OpenMenu(TableV2Vm t) =>
        Navigation.PushAsync(new MenuV2Page(_state, _api, _hub, t.TableId, t.ZoneId, t.ZoneName, t.Display));

    // "Tümünü Gör" / "Kapat" — ekrandan ayrılmadan kart içinde genişler.
    private void ToggleExpand_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not TableV2Vm t || !t.CanExpand) return;
        t.Expanded = !t.Expanded;
        if (t.Expanded) _expandedTables.Add(t.TableId);
        else _expandedTables.Remove(t.TableId);
    }

    // "Ürün Ekle" → menü / sipariş sayfası
    private async void AddItem_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not TableV2Vm t) return;
        await OpenMenu(t);
    }

    // "Hesap Al" → ödeme sayfası
    private async void Pay_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not TableV2Vm t || t.OrderId is not { } oid) return;
        await Navigation.PushAsync(new PaymentPage(_api, oid, t.AmountValue, t.Display, async () =>
        {
            await Navigation.PopAsync();
            await LoadAll();
        }, coverCount: t.Covers));
    }

    // Masasız (paket/gel-al/self-servis) siparişi ödemeye al — settle akışı masa-bağımsızdır.
    private async void PayNonTable_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not NonTableOrderVm v) return;
        await Navigation.PushAsync(new PaymentPage(_api, v.OrderId, v.AmountValue, v.Label, async () =>
        {
            await Navigation.PopAsync();
            await LoadAll();
        }, coverCount: v.Covers));
    }

    private async void Deliver_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not DeliverGroupVm g) return;
        try
        {
            foreach (var id in g.LineIds)
                await _api.KitchenDeliveredAsync(id);
            await LoadAll();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ServiceDone_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not ServiceCardVm c) return;
        try { await _api.CompleteServiceRequestAsync(c.Id); await LoadAll(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Refresh_Clicked(object sender, EventArgs e) => await LoadAll();

    private void Theme_Clicked(object sender, EventArgs e)
    {
        MauiTheme.Cycle();
        ThemeBtn.Text = MauiTheme.CurrentGlyph;
    }

    private async void Logout_Clicked(object sender, EventArgs e)
    {
        // Merkezi çıkış: SignalR'ı da durdurur (eski JWT ile bağlı kalmasın).
        if (Application.Current is App app) await app.LogoutAsync();
    }
}
