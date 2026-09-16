using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

/// <summary>V2 menü ürün kartı görünüm modeli.</summary>
public partial class MenuProductVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public decimal TaxRate { get; init; }
    public bool HasImage { get; init; }
    public List<int> CategoryIds { get; init; } = new();
    public string PriceText => $"₺{Price:N2}";
    public string ImageUrl { get; init; } = "";
    public bool ShowImage => HasImage && !string.IsNullOrEmpty(ImageUrl);
    public bool ShowPlaceholder => !ShowImage;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

/// <summary>Kampanya çipi.</summary>
public partial class CampaignChipVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public bool Applied { get; init; }
    public bool Eligible { get; init; }

    // Üst satır: uygulanmışsa "✓ Ad", uygun (uygulanmamış) ise "🎁 Ad", değilse sadece "Ad"
    public string Label => Applied ? $"✓ {Name}" : Eligible ? $"🎁 {Name}" : Name;

    // İkinci satır rozeti — uygun kampanyayı vurgular
    public string StatusText => Applied ? "UYGULANDI" : Eligible ? "KULLANILABİLİR" : "";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    // Uygun-ama-uygulanmamış kampanya parlasın (kalın çerçeve + vurgu rengi)
    public bool Highlight => Eligible && !Applied;

    public Color BgColor => Applied ? FilterChipVm.AppColor("Accent")
        : Eligible ? FilterChipVm.AppColor("Warning") : FilterChipVm.AppColor("Surface");
    public Color FgColor => Applied || Eligible ? Colors.White : FilterChipVm.AppColor("TextMuted");

    // Vurgulu/uygulanmış çipte daha kalın, dikkat çeken kenarlık
    public Color StrokeColor => Highlight ? FilterChipVm.AppColor("Warning")
        : Applied ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("BorderColor");
    public double StrokeW => Highlight || Applied ? 2 : 1;

    // Rozet (KULLANILABİLİR/UYGULANDI) için yarı saydam zıt etiket
    public Color BadgeBg => Color.FromArgb("#33FFFFFF");
    public Color BadgeFg => Colors.White;
}

/// <summary>Menü ekranındaki canlı sepet/adisyon satırı (sağ panel).</summary>
public partial class CartLineVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Qty { get; init; }
    public decimal LineTotal { get; init; }
    public int LineStatus { get; init; }
    public string QtyText => $"{Qty:0.##}x";
    public string TotalText => $"₺{LineTotal:N2}";
    // Henüz gönderilmemiş satır vurgulanır (mutfağa gitmeyi bekliyor)
    public bool Unsent => LineStatus == 0;
    public string StatusText => LineStatus switch
    {
        1 => "Mutfakta", 2 => "Hazır", 3 => "Teslim", 5 => "Hazırlanıyor", _ => "Yeni"
    };
    public Color StatusColor => LineStatus switch
    {
        3 => FilterChipVm.AppColor("Success"),
        2 => FilterChipVm.AppColor("Warning"),
        1 or 5 => FilterChipVm.AppColor("Accent"),
        _ => FilterChipVm.AppColor("Accent"),
    };
    public Color NameColor => Unsent ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("TextColor");
}

/// <summary>Adisyon satırı görünüm modeli (statü rozetli).</summary>
public partial class LineV2Vm : ObservableObject
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public decimal Qty { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public int LineStatus { get; init; }
    public bool IsTreat { get; init; }
    public string? KitchenNote { get; init; }

    public string QtyText => $"{Qty:0.##}";
    public string UnitText => $"₺{UnitPrice:N2}";
    public string TotalText => $"₺{LineTotal:N2}";
    public bool Editable => LineStatus == 0;   // sadece gönderilmemiş satır düzenlenir
    public bool NotEditable => !Editable;

    // Mutfak notu/açıklama satırda gösterilir (varsa)
    public bool HasNote => !string.IsNullOrWhiteSpace(KitchenNote);
    public string NoteText => HasNote ? $"📝 {KitchenNote}" : "";

    public string StatusText => LineStatus switch
    {
        1 => "Mutfakta", 2 => "Hazır · Serviste", 3 => "Teslim Edildi", 4 => "İptal", 5 => "Hazırlanıyor", _ => "Beklemede"
    };
    public Color StatusColor => LineStatus switch
    {
        3 => FilterChipVm.AppColor("Success"),
        2 => FilterChipVm.AppColor("Warning"),
        1 or 5 => FilterChipVm.AppColor("Accent"),
        4 => FilterChipVm.AppColor("Danger"),
        _ => FilterChipVm.AppColor("TextMuted"),
    };
}

/// <summary>Böl / Kişi Aktar kalem seçim modalı satırı (çoklu seçim).</summary>
public partial class PickLineVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Qty { get; init; }
    public decimal LineTotal { get; init; }
    public string QtyText => $"{Qty:0.##}x";
    public string TotalText => $"₺{LineTotal:N2}";

    [ObservableProperty] private bool _selected;
    public string Check => Selected ? "☑" : "☐";
    public Color BgColor => Selected ? FilterChipVm.AppColor("SurfaceAlt") : FilterChipVm.AppColor("Surface");
    public Color StrokeColor => Selected ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("BorderColor");
    partial void OnSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(Check));
        OnPropertyChanged(nameof(BgColor));
        OnPropertyChanged(nameof(StrokeColor));
    }
}

public partial class MenuV2Page : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly HubService _hub;
    private readonly int _tableId;
    private readonly int _zoneId;
    private readonly string _zoneName;
    private readonly string _tableName;

    private int _orderId;
    private int _cover;
    private List<OrderLineDto> _lines = new();
    private List<MenuProductVm> _allProducts = new();
    private int _catFilter;
    private string _search = "";
    private bool _loaded;

    public MenuV2Page(AppState state, ApiClient api, HubService hub, int tableId, int zoneId, string zoneName, string tableName)
    {
        InitializeComponent();
        _state = state; _api = api; _hub = hub;
        _tableId = tableId; _zoneId = zoneId; _zoneName = zoneName; _tableName = tableName;

        TitleLabel.Text = tableName;
        ZoneLabel.Text = (zoneName ?? "").ToUpperInvariant();
        ThemeBtn.Text = MauiTheme.CurrentGlyph;
        ShowMenu(true);
        PayBar.IsVisible = CanPay;   // Ödeme butonu yalnız ödeme yetkili rolde (Garson'da gizli)
        SizeChanged += (_, _) => UpdateCartPanelVisibility();
    }

    // Geniş ekranda (tablet/yatay) canlı sepet yan paneli görünür; dar ekranda gizli.
    private bool _wide;
    private void UpdateCartPanelVisibility()
    {
        bool wide = Width >= 720;          // tablet / yatay eşiği
        if (wide == _wide && CartPanel.IsVisible == wide) return;
        _wide = wide;
        CartPanel.IsVisible = wide;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.TableChanged += OnChanged;
        _hub.TicketReady += OnReady;
        MauiTheme.Changed += OnThemeChanged;
        await _hub.EnsureConnectedAsync();
        UpdateCartPanelVisibility();
        if (!_loaded) { _loaded = true; await LoadAll(); }
        else await ReloadLines();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.TableChanged -= OnChanged;
        _hub.TicketReady -= OnReady;
        MauiTheme.Changed -= OnThemeChanged;
        // Masaya girip ürün eklemeden çıkıldıysa boş adisyonu sil → masa "Dolu" kalmasın.
        // Boş değilse (ürün/kısmi ödeme varsa) sunucu tarafında no-op; ödemeye gidişte de güvenli.
        if (_orderId > 0)
        {
            int oid = _orderId;
            _ = Task.Run(async () => { try { await _api.PostAsync($"/api/orders/{oid}/delete-if-empty", new { }); } catch { } });
        }
    }

    private async void OnChanged() => await ReloadLines();
    private async void OnReady(int lineId) => await ReloadLines();
    private void OnThemeChanged() { ThemeBtn.Text = MauiTheme.CurrentGlyph; RefreshLines(); RenderCampaigns(_campaigns); }

    private void ShowError(string? msg)
    {
        ErrorLabel.Text = msg ?? "";
        ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(msg);
    }

    private List<CampaignChipVm> _campaigns = new();

    private async Task LoadAll()
    {
        try
        {
            ShowError(null);
            var res = await _api.GetAsync<ByTableResult>($"/api/orders/by-table/{_tableId}");
            if (res.Order is null)
            {
                var order = await _api.PostAsync<OrderDto>("/api/orders/open", new OpenOrderRequest(_tableId, _zoneId));
                _orderId = order.Id;
                _cover = Math.Max(1, order.CoverCount);
                _lines = new();
            }
            else { _orderId = res.Order.Id; _cover = Math.Max(1, res.Order.CoverCount); _lines = res.Lines ?? new(); }

            var cats = await _api.GetAsync<List<CategoryDto>>("/api/menu/categories");
            cats.Insert(0, new CategoryDto(0, "Tümü", false));
            CatsView.ItemsSource = cats;
            CatsView.SelectedItem = cats[0];

            var baseUrl = _state.ApiBaseUrl ?? "";
            var prods = await _api.GetAsync<List<MenuProductDto>>("/api/menu/products");
            _allProducts = prods.Where(p => !p.IsSalesStopped).Select(p => new MenuProductVm
            {
                Id = p.Id, Name = p.Name, Description = p.Description, Price = p.Price, TaxRate = p.TaxRate,
                HasImage = p.HasImage, CategoryIds = p.CategoryIds,
                ImageUrl = p.HasImage ? $"{baseUrl}/api/menu/products/{p.Id}/image" : ""
            }).ToList();

            await LoadCampaigns();
            ShowProducts();
            RefreshLines();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task LoadCampaigns()
    {
        try
        {
            var list = await _api.GetAsync<List<CampaignDto>>($"/api/orders/{_orderId}/campaigns");
            _campaigns = list.Select(c => new CampaignChipVm { Id = c.Id, Name = c.Name, Applied = c.Applied, Eligible = c.Eligible }).ToList();
        }
        catch { _campaigns = new(); }
        RenderCampaigns(_campaigns);
    }

    private void RenderCampaigns(List<CampaignChipVm> list)
    {
        CampaignsView.ItemsSource = null;
        CampaignsView.ItemsSource = list;
        CampaignsScroll.IsVisible = list.Count > 0;
    }

    private void ShowMenu(bool menu)
    {
        MenuView.IsVisible = menu;
        AdisyonView.IsVisible = !menu;
        MenuTabBtn.BackgroundColor = menu ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("SurfaceAlt");
        MenuTabBtn.TextColor = menu ? Colors.White : FilterChipVm.AppColor("TextColor");
        AdisyonTabBtn.BackgroundColor = !menu ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("SurfaceAlt");
        AdisyonTabBtn.TextColor = !menu ? Colors.White : FilterChipVm.AppColor("TextColor");
    }

    private void Tab_Menu(object sender, EventArgs e) => ShowMenu(true);
    private void Tab_Adisyon(object sender, EventArgs e) => ShowMenu(false);

    private void Search_Changed(object sender, TextChangedEventArgs e) { _search = e.NewTextValue ?? ""; ShowProducts(); }

    private void Cat_Selected(object sender, SelectionChangedEventArgs e)
    {
        _catFilter = (e.CurrentSelection.FirstOrDefault() as CategoryDto)?.Id ?? 0;
        ShowProducts();
    }

    private void ShowProducts()
    {
        IEnumerable<MenuProductVm> list = _allProducts;
        if (_catFilter != 0) list = list.Where(p => p.CategoryIds.Contains(_catFilter));
        if (!string.IsNullOrWhiteSpace(_search))
            list = list.Where(p => p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));
        ProductsView.ItemsSource = list.ToList();
    }

    private async Task ReloadLines()
    {
        try { _lines = await _api.GetAsync<List<OrderLineDto>>($"/api/orders/{_orderId}/lines"); RefreshLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void RefreshLines()
    {
        var top = _lines.Where(l => l.ParentLineId == null && l.LineStatus != 4).ToList();
        var vms = top.Select(l => new LineV2Vm
        {
            Id = l.Id, ProductId = l.ProductId, Name = l.ProductName ?? "", Qty = l.Qty,
            UnitPrice = l.UnitPrice, LineTotal = l.LineTotal, LineStatus = l.LineStatus, IsTreat = l.IsTreat,
            KitchenNote = l.KitchenNote
        }).ToList();
        LinesView.ItemsSource = vms;

        var total = top.Sum(l => l.LineTotal);
        TotalLabel.Text = $"₺{total:N2}";
        int adisyonCount = top.Count;
        AdisyonTabBtn.Text = $"Adisyon ({adisyonCount})";

        int unsent = top.Count(l => l.LineStatus == 0);
        SendBtn.Text = unsent > 0 ? $"🍳 Mutfağa Gönder ({unsent})" : "Sepet boş — menüden ürün ekleyin";
        SendBtn.IsEnabled = unsent > 0;
        SendBtn.BackgroundColor = unsent > 0 ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("ButtonColor");

        DateLabel.Text = DateTime.Now.ToString("dd.MM.yyyy");
        CoverLabel.Text = $"👤 {_cover}";

        RefreshCart(top, total, unsent);
    }

    // ===== Canlı sepet/adisyon paneli (menü ekranında) =====
    private void RefreshCart(List<OrderLineDto> top, decimal total, int unsent)
    {
        // En son eklenen/değişen en üstte olsun: gönderilmemişler önce, sonra id'ye göre tersten
        var ordered = top
            .OrderByDescending(l => l.LineStatus == 0)
            .ThenByDescending(l => l.Id)
            .Select(l => new CartLineVm
            {
                Id = l.Id, Name = l.ProductName ?? "", Qty = l.Qty,
                LineTotal = l.LineTotal, LineStatus = l.LineStatus
            }).ToList();

        CartView.ItemsSource = ordered;
        CartTotalLabel.Text = $"₺{total:N2}";
        CartCountLabel.Text = top.Count == 0 ? "Sepet boş" : $"{top.Count} kalem";
        CartEmptyLabel.IsVisible = top.Count == 0;
        CartView.IsVisible = top.Count > 0;

        CartSendBtn.Text = unsent > 0 ? $"🍳 Gönder ({unsent})" : "Bekleyen yok";
        CartSendBtn.IsEnabled = unsent > 0;
        CartSendBtn.BackgroundColor = unsent > 0 ? FilterChipVm.AppColor("Accent") : FilterChipVm.AppColor("ButtonColor");
    }

    private OrderLineDto? UnsentLine(int productId) =>
        _lines.FirstOrDefault(l => l.ParentLineId == null && l.ProductId == productId && l.LineStatus == 0);

    private async void Add_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not MenuProductVm vm) return;
        try
        {
            // Anlık geri bildirim — sunucu yanıtını beklemeden kullanıcı eklendiğini görsün
            BumpAdisyonBadge();
            ShowToast($"{vm.Name} eklendi");

            var line = UnsentLine(vm.Id);
            if (line is not null)
                await _api.PatchAsync($"/api/orders/lines/{line.Id}/qty", new ChangeQtyRequest(line.Qty + 1));
            else
                await _api.PostAsync($"/api/orders/{_orderId}/lines", new AddLineRequest(vm.Id, 1, vm.Price, vm.TaxRate, null, 0));
            await ReloadLines();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // İyimser rozet artışı: gerçek sayım ReloadLines ile gelene kadar "Adisyon (N+1)" göster
    private void BumpAdisyonBadge()
    {
        var current = _lines.Count(l => l.ParentLineId == null && l.LineStatus != 4);
        AdisyonTabBtn.Text = $"Adisyon ({current + 1})";
    }

    // ===== Kısa anlık bildirim (toast) — son eklenen ürün =====
    private CancellationTokenSource? _toastCts;
    private async void ShowToast(string msg)
    {
        ToastLabel.Text = msg;
        ToastBorder.IsVisible = true;
        ToastBorder.Opacity = 0;
        await ToastBorder.FadeTo(1, 120);

        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var tok = _toastCts.Token;
        try
        {
            await Task.Delay(1500, tok);
            await ToastBorder.FadeTo(0, 200);
            if (!tok.IsCancellationRequested) ToastBorder.IsVisible = false;
        }
        catch (TaskCanceledException) { /* yeni toast geldi */ }
    }

    private async void LinePlus_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not LineV2Vm vm) return;
        try { await _api.PatchAsync($"/api/orders/lines/{vm.Id}/qty", new ChangeQtyRequest(vm.Qty + 1)); await ReloadLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void LineMinus_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not LineV2Vm vm) return;
        try
        {
            if (vm.Qty > 1) await _api.PatchAsync($"/api/orders/lines/{vm.Id}/qty", new ChangeQtyRequest(vm.Qty - 1));
            else await _api.DeleteAsync($"/api/orders/lines/{vm.Id}");
            await ReloadLines();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // Satır açıklama/mutfak notu — DisplayPromptAsync ile düzenlenir (mevcut not initialValue olarak gelir)
    private async void LineNote_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not LineV2Vm vm) return;
        await EditLineNote(vm);
    }

    private async Task EditLineNote(LineV2Vm vm)
    {
        var note = await DisplayPromptAsync("Açıklama", "Örn: az pişmiş, sossuz...", "Kaydet", "Vazgeç",
            initialValue: vm.KitchenNote ?? "");
        if (note is null) return;   // Vazgeç
        try { await _api.UpdateLineNoteAsync(vm.Id, note); await ReloadLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // Ödeme yetkili rol mü? İkram/İskonto yalnız bu rolde görünür (Garson'da gizli; backend de 403 verir).
    private bool CanPay => _state.Login?.Roles?.Any(r =>
        r.Equals("Kasiyer", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) == true;

    // Satıra dokununca aksiyon menüsü (gönderilmiş satırlar dahil): açıklama / iptal
    // (+ ödeme yetkili rolde ikram / iskonto)
    private async void Line_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not LineV2Vm vm) return;
        string treatLabel = vm.IsTreat ? "İkramı Kaldır" : "🎁 İkram";
        string? choice;
        if (CanPay)
            choice = await DisplayActionSheet(vm.Name, "Vazgeç", null,
                treatLabel, "％ İskonto", "📝 Açıklama", "🚫 İptal Et");
        else
            choice = await DisplayActionSheet(vm.Name, "Vazgeç", null, "📝 Açıklama", "🚫 İptal Et");

        switch (choice)
        {
            case "🎁 İkram":
            case "İkramı Kaldır": await TreatLine(vm); break;
            case "％ İskonto": await DiscountLine(vm); break;
            case "📝 Açıklama": await EditLineNote(vm); break;
            case "🚫 İptal Et": await CancelLine(vm); break;
        }
    }

    // İkram: onay → durumu tersine çevir → satırları yenile
    private async Task TreatLine(LineV2Vm vm)
    {
        bool ok = await DisplayAlert("İkram",
            vm.IsTreat ? $"{vm.Name} ikramı kaldırılsın mı?" : $"{vm.Name} ikram edilsin mi (ücretsiz)?",
            "Evet", "Vazgeç");
        if (!ok) return;
        try { await _api.TreatAsync(vm.Id, !vm.IsTreat); await ReloadLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // İskonto: tutar sor (satır tutarıyla sınırlı) → uygula → satırları yenile
    private async Task DiscountLine(LineV2Vm vm)
    {
        var s = await DisplayPromptAsync("İskonto", $"İndirim tutarı (₺) — en çok {vm.LineTotal:N2}:",
            "Uygula", "Vazgeç", keyboard: Keyboard.Numeric);
        if (s is null) return;   // Vazgeç
        if (!decimal.TryParse((s ?? "").Replace(',', '.'),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt)
            || amt < 0)
        {
            ShowError("Geçerli bir tutar girin."); return;
        }
        if (amt > vm.LineTotal) { ShowError($"İndirim satır tutarını ({vm.LineTotal:N2}) aşamaz."); return; }
        try { await _api.DiscountAsync(vm.Id, amt); await ReloadLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void LineCancel_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not LineV2Vm vm) return;
        await CancelLine(vm);
    }

    // İptal: mutfağa gitmişse Stoğa İade/Fire seçtir, her durumda iptal sebebi sor (AdisyonPage deseni)
    private async Task CancelLine(LineV2Vm vm)
    {
        var reason = await DisplayPromptAsync("İptal", "İptal sebebi:", "Devam", "Vazgeç");
        if (reason is null) return;
        // Gönderilmiş (mutfağa gitmiş) satırda stoğa iade / fire seçtir; gönderilmemişte iade gerekmez
        bool stock = false;
        if (vm.LineStatus != 0)
            stock = await DisplayAlert("Stok", "Ürün stoğa geri eklensin mi?", "Stoğa İade", "Fire");
        try
        {
            await _api.DeleteAsync($"/api/orders/lines/{vm.Id}?reason={Uri.EscapeDataString(reason)}&returnToStock={stock.ToString().ToLowerInvariant()}");
            await ReloadLines();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Send_Clicked(object sender, EventArgs e)
    {
        try
        {
            await _api.PostAsync($"/api/orders/{_orderId}/send");
            await ReloadLines();
            ShowMenu(false);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Campaign_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not CampaignChipVm c) return;
        try
        {
            if (c.Applied) await _api.DeleteAsync($"/api/orders/{_orderId}/campaigns");
            else await _api.PostAsync($"/api/orders/{_orderId}/campaigns/{c.Id}");
            await LoadCampaigns();
            await ReloadLines();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // ===== Kişi sayısı (stepper) =====
    private async void CoverMinus_Clicked(object sender, EventArgs e) { if (_cover > 0) _cover--; await SaveCover(); }
    private async void CoverPlus_Clicked(object sender, EventArgs e) { _cover++; await SaveCover(); }
    private async Task SaveCover()
    {
        CoverLabel.Text = $"👤 {_cover}";
        try { await _api.PostAsync($"/api/orders/{_orderId}/cover", new CoverRequest(_cover)); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // ===== Diğer alt çubuk eylemleri =====
    private async void Reset_Clicked(object sender, EventArgs e)
    {
        var unsent = _lines.Where(l => l.ParentLineId == null && l.LineStatus == 0).ToList();
        if (unsent.Count == 0) { await DisplayAlert("Sıfırla", "Gönderilmemiş ürün yok.", "Tamam"); return; }
        bool ok = await DisplayAlert("Sıfırla", "Gönderilmemiş ürünler kaldırılsın mı?", "Evet", "Vazgeç");
        if (!ok) return;
        try { foreach (var l in unsent) await _api.DeleteAsync($"/api/orders/lines/{l.Id}"); await ReloadLines(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void Reprint_Clicked(object sender, EventArgs e)
    {
        try { await _api.PostAsync($"/api/orders/{_orderId}/reprint"); await DisplayAlert("Yazdır", "Mutfağa yeniden gönderildi.", "Tamam"); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Summary_Clicked(object sender, EventArgs e) => ShowMenu(false);

    // Ödeme Al: kalan (ödenmemiş, ikram olmayan) tutarı hesapla → PaymentPage'e git (AdisyonPage deseni)
    private async void Pay_Clicked(object sender, EventArgs e)
    {
        if (!CanPay) return;   // güvenlik: Garson'da gizli, yine de korunsun
        var remaining = _lines
            .Where(l => l.ParentLineId == null && l.LineStatus != 4 && !l.IsTreat && l.SettledClFicheId == null)
            .Sum(l => l.LineTotal);
        if (remaining <= 0) { await DisplayAlert("Ödeme", "Ödenecek tutar yok.", "Tamam"); return; }
        await Navigation.PushAsync(new PaymentPage(_api, _orderId, remaining, _tableName,
            onPaid: async () => await Navigation.PopToRootAsync(), coverCount: _cover));
    }

    // ===== Masa Notu modalı =====
    private List<string> _selectedGeneral = new();
    private List<string> _selectedAllergy = new();

    private async void Note_Clicked(object sender, EventArgs e)
    {
        try
        {
            NoteGeneralView.ItemsSource = null;
            NoteAllergyView.ItemsSource = null;
            var presets = await _api.NotePresetsAsync();
            NoteGeneralView.ItemsSource = presets.General;
            NoteAllergyView.ItemsSource = presets.Allergy;
        }
        catch { /* önayar yoksa boş bırak */ }
        NoteOverlay.IsVisible = true;
    }

    private void NoteGeneral_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not string s) return;
        var cur = NoteEditor.Text ?? "";
        NoteEditor.Text = string.IsNullOrWhiteSpace(cur) ? s : cur + ", " + s;
    }

    private void NoteAllergy_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not string s) return;
        if (_selectedAllergy.Contains(s)) _selectedAllergy.Remove(s); else _selectedAllergy.Add(s);
        AllergyLabel.Text = _selectedAllergy.Count > 0 ? "Alerji: " + string.Join(", ", _selectedAllergy) : "";
        AllergyLabel.IsVisible = _selectedAllergy.Count > 0;
    }

    private async void NoteSave_Clicked(object sender, EventArgs e)
    {
        try
        {
            await _api.SetOrderNoteAsync(_orderId, NoteEditor.Text);
            await _api.SetAllergyAsync(_orderId, _selectedAllergy.Count > 0 ? string.Join(", ", _selectedAllergy) : null);
            await _api.SetOrderFlagsAsync(_orderId, VipSwitch.IsToggled, PrioritySwitch.IsToggled);
            NoteOverlay.IsVisible = false;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void NoteClear_Clicked(object sender, EventArgs e)
    {
        NoteEditor.Text = "";
        _selectedAllergy.Clear();
        AllergyLabel.Text = ""; AllergyLabel.IsVisible = false;
        try { await _api.SetOrderNoteAsync(_orderId, null); await _api.SetAllergyAsync(_orderId, null); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void NoteCancel_Clicked(object sender, EventArgs e) => NoteOverlay.IsVisible = false;

    // ===== MASA İŞLEMLERİ (Taşı / Böl / Birleştir / Kişi Aktar) =====
    private async void Ops_Clicked(object sender, EventArgs e)
    {
        var choice = await DisplayActionSheet("⇄ Masa İşlemleri", "Vazgeç", null,
            "➡️ Taşı", "✂️ Böl", "🔗 Birleştir", "👥 Kişi/Adisyon Aktar");
        switch (choice)
        {
            case "➡️ Taşı": await OpsMove(); break;
            case "✂️ Böl": await OpsSplit(); break;
            case "🔗 Birleştir": await OpsMerge(); break;
            case "👥 Kişi/Adisyon Aktar": await OpsTransfer(); break;
        }
    }

    private async Task<List<TableCardDto>> AllTablesAsync()
    {
        var all = new List<TableCardDto>();
        List<ZoneDto> zones;
        try { zones = await _api.GetAsync<List<ZoneDto>>("/api/tables/zones"); } catch { zones = new(); }
        foreach (var z in zones)
        {
            try { all.AddRange(await _api.GetAsync<List<TableCardDto>>($"/api/tables/board?zoneId={z.Id}")); }
            catch { /* bölge atlanır */ }
        }
        return all;
    }

    // Boş masa seç (Taşı / Böl). Seçilen masa veya null döner.
    private async Task<TableCardDto?> PickEmptyTableAsync(string title)
    {
        var all = await AllTablesAsync();
        var empties = all.Where(t => t.Status == 0 && t.TableId != _tableId).ToList();
        if (empties.Count == 0) { await DisplayAlert(title, "Boş masa yok.", "Tamam"); return null; }
        var names = empties.Select(t => t.Display).ToArray();
        var sel = await DisplayActionSheet(title, "Vazgeç", null, names);
        return empties.FirstOrDefault(t => t.Display == sel);
    }

    // Dolu / açık adisyonu olan başka masa seç (Birleştir / Aktar). Seçilen masa veya null döner.
    private async Task<TableCardDto?> PickOccupiedTableAsync(string title)
    {
        var all = await AllTablesAsync();
        var occ = all.Where(t => t.Status == 1 && t.OrderId is not null && t.TableId != _tableId).ToList();
        if (occ.Count == 0) { await DisplayAlert(title, "Hedef olabilecek dolu masa yok.", "Tamam"); return null; }
        var names = occ.Select(t => t.Display).ToArray();
        var sel = await DisplayActionSheet(title, "Vazgeç", null, names);
        return occ.FirstOrDefault(t => t.Display == sel);
    }

    private async Task OpsMove()
    {
        try
        {
            var target = await PickEmptyTableAsync("Taşınacak masa");
            if (target is null) return;
            await _api.MoveOrderAsync(_orderId, target.TableId);
            await DisplayAlert("Taşı", $"{target.Display} masasına taşındı.", "Tamam");
            await Navigation.PopAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task OpsMerge()
    {
        try
        {
            var target = await PickOccupiedTableAsync("Hangi masayla birleştirilsin?");
            if (target is null || target.OrderId is not { } toid) return;
            await _api.MergeOrderAsync(_orderId, toid);
            await DisplayAlert("Birleştir", $"{target.Display} ile birleştirildi.", "Tamam");
            await Navigation.PopAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task OpsSplit()
    {
        var ids = await PickLinesAsync("Bölünecek kalemleri seçin");
        if (ids is null || ids.Count == 0) return;
        try
        {
            var target = await PickEmptyTableAsync("Hangi boş masaya bölünsün?");
            if (target is null) return;
            await _api.SplitOrderAsync(_orderId, target.TableId, ids);
            await DisplayAlert("Böl", $"Adisyon bölündü → {target.Display}", "Tamam");
            await Navigation.PopAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task OpsTransfer()
    {
        var ids = await PickLinesAsync("Aktarılacak kalemleri seçin");
        if (ids is null || ids.Count == 0) return;
        try
        {
            var target = await PickOccupiedTableAsync("Hangi adisyona aktarılsın?");
            if (target is null || target.OrderId is not { } toid) return;
            await _api.TransferLinesAsync(_orderId, toid, ids);
            await DisplayAlert("Aktar", $"Kalemler aktarıldı → {target.Display}", "Tamam");
            await Navigation.PopAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // ===== Kalem seçim modalı (çoklu) — TaskCompletionSource ile await edilir =====
    private TaskCompletionSource<List<int>?>? _pickTcs;

    private Task<List<int>?> PickLinesAsync(string title)
    {
        // Yalnız taşınabilir (ödenmemiş) ana satırlar: alt satır ve ödenmiş (SettledClFicheId dolu) hariç
        var top = _lines.Where(l => l.ParentLineId == null && l.LineStatus != 4 && l.SettledClFicheId == null).ToList();
        if (top.Count == 0)
        {
            DisplayAlert("Bilgi", "Taşınacak (ödenmemiş) kalem yok.", "Tamam");
            return Task.FromResult<List<int>?>(null);
        }
        var vms = top.Select(l => new PickLineVm
        {
            Id = l.Id, Name = l.ProductName ?? "", Qty = l.Qty, LineTotal = l.LineTotal
        }).ToList();
        LinePickTitle.Text = title;
        LinePickView.ItemsSource = vms;
        LinePickOverlay.IsVisible = true;
        _pickTcs = new TaskCompletionSource<List<int>?>();
        return _pickTcs.Task;
    }

    private void PickLine_Tapped(object sender, EventArgs e)
    {
        if ((sender as VisualElement)?.BindingContext is not PickLineVm vm) return;
        vm.Selected = !vm.Selected;
    }

    private void PickLineAll_Clicked(object sender, EventArgs e)
    {
        if (LinePickView.ItemsSource is not IEnumerable<PickLineVm> vms) return;
        var list = vms.ToList();
        bool allOn = list.All(v => v.Selected);
        foreach (var v in list) v.Selected = !allOn;
    }

    private void PickLineCancel_Clicked(object sender, EventArgs e)
    {
        LinePickOverlay.IsVisible = false;
        _pickTcs?.TrySetResult(null);
        _pickTcs = null;
    }

    private void PickLineGo_Clicked(object sender, EventArgs e)
    {
        var ids = LinePickView.ItemsSource is IEnumerable<PickLineVm> vms
            ? vms.Where(v => v.Selected).Select(v => v.Id).ToList() : new List<int>();
        if (ids.Count == 0) { ShowError("En az bir kalem seçin."); return; }
        ShowError(null);
        LinePickOverlay.IsVisible = false;
        _pickTcs?.TrySetResult(ids);
        _pickTcs = null;
    }

    private void Theme_Clicked(object sender, EventArgs e) { MauiTheme.Cycle(); ThemeBtn.Text = MauiTheme.CurrentGlyph; }
    private async void Back_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();
}
