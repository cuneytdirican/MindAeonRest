using CommunityToolkit.Mvvm.ComponentModel;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

/// <summary>Ürün kartı görünüm modeli — sepetteki adet (Qty) gözlemlenebilir.</summary>
public partial class ProductVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public decimal TaxRate { get; init; }
    public List<int> CategoryIds { get; init; } = new();
    public string PriceText => $"₺{Price:N2}";

    [ObservableProperty] private decimal _qty;
}

public partial class AdisyonPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly int _tableId;
    private readonly int _zoneId;
    private readonly string _tableName;

    private int _orderId;
    private List<OrderLineDto> _lines = new();
    private List<ProductVm> _allProducts = new();
    private int _catFilter;   // 0 = tümü
    private bool _loaded;

    public AdisyonPage(AppState state, ApiClient api, int tableId, int zoneId, string tableName)
    {
        InitializeComponent();
        _state = state; _api = api; _tableId = tableId; _zoneId = zoneId; _tableName = tableName;
        TitleLabel.Text = tableName;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded) { _loaded = true; await LoadAll(); }
    }

    private async Task LoadAll()
    {
        try
        {
            var res = await _api.GetAsync<ByTableResult>($"/api/orders/by-table/{_tableId}");
            if (res.Order is null)
            {
                var order = await _api.PostAsync<OrderDto>("/api/orders/open", new OpenOrderRequest(_tableId, _zoneId));
                _orderId = order.Id;
                _lines = new();
            }
            else { _orderId = res.Order.Id; _lines = res.Lines ?? new(); }

            var cats = await _api.GetAsync<List<CategoryDto>>("/api/menu/categories");
            cats.Insert(0, new CategoryDto(0, "Tümü", false));
            CatsView.ItemsSource = cats;
            CatsView.SelectedItem = cats[0];

            var prods = await _api.GetAsync<List<MenuProductDto>>("/api/menu/products");
            _allProducts = prods.Where(p => !p.IsSalesStopped).Select(p => new ProductVm
            {
                Id = p.Id, Name = p.Name, Price = p.Price, TaxRate = p.TaxRate, CategoryIds = p.CategoryIds
            }).ToList();

            ShowProducts();
            RefreshLines();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private void Cat_Selected(object sender, SelectionChangedEventArgs e)
    {
        _catFilter = (e.CurrentSelection.FirstOrDefault() as CategoryDto)?.Id ?? 0;
        ShowProducts();
    }

    private void ShowProducts()
    {
        var list = _catFilter == 0 ? _allProducts : _allProducts.Where(p => p.CategoryIds.Contains(_catFilter)).ToList();
        ProductsView.ItemsSource = list;
        RecomputeQty();
    }

    private void RecomputeQty()
    {
        foreach (var vm in _allProducts)
            vm.Qty = _lines.Where(l => l.ParentLineId == null && l.ProductId == vm.Id && l.LineStatus != 4).Sum(l => l.Qty);
    }

    private void RefreshLines()
    {
        var top = _lines.Where(l => l.ParentLineId == null && l.LineStatus != 4).ToList();
        LinesView.ItemsSource = top;
        // Kalan (ödenmemiş, ikram olmayan) tutar — PaymentPage'e giden tutarla aynı mantık
        var remaining = top.Where(l => !l.IsTreat && l.SettledClFicheId == null).Sum(l => l.LineTotal);
        TotalLabel.Text = $"₺{remaining:N2}";
        RecomputeQty();
    }

    private async Task ReloadLines()
    {
        _lines = await _api.GetAsync<List<OrderLineDto>>($"/api/orders/{_orderId}/lines");
        RefreshLines();
    }

    private OrderLineDto? UnsentLine(int productId) =>
        _lines.FirstOrDefault(l => l.ParentLineId == null && l.ProductId == productId && l.LineStatus == 0);

    private async void Plus_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not ProductVm vm) return;
        try
        {
            var line = UnsentLine(vm.Id);
            if (line is not null)
                await _api.PatchAsync($"/api/orders/lines/{line.Id}/qty", new ChangeQtyRequest(line.Qty + 1));
            else
                await _api.PostAsync($"/api/orders/{_orderId}/lines",
                    new AddLineRequest(vm.Id, 1, vm.Price, vm.TaxRate, null, 0));
            await ReloadLines();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Minus_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not ProductVm vm) return;
        var line = UnsentLine(vm.Id);
        if (line is null) { await DisplayAlert("Bilgi", "Gönderilen ürün buradan düşülemez; iptal kullanın.", "Tamam"); return; }
        try
        {
            if (line.Qty > 1) await _api.PatchAsync($"/api/orders/lines/{line.Id}/qty", new ChangeQtyRequest(line.Qty - 1));
            else await _api.DeleteAsync($"/api/orders/lines/{line.Id}");
            await ReloadLines();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Send_Clicked(object sender, EventArgs e)
    {
        try
        {
            await _api.PostAsync($"/api/orders/{_orderId}/send");
            await ReloadLines();
            await DisplayAlert("Mutfak", "Sipariş mutfağa gönderildi.", "Tamam");
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Pay_Clicked(object sender, EventArgs e)
    {
        var total = _lines.Where(l => l.ParentLineId == null && l.LineStatus != 4 && !l.IsTreat && l.SettledClFicheId == null).Sum(l => l.LineTotal);
        if (total <= 0) { await DisplayAlert("Ödeme", "Ödenecek tutar yok.", "Tamam"); return; }
        await Navigation.PushAsync(new PaymentPage(_api, _orderId, total, _tableName,
            onPaid: async () => await Navigation.PopToRootAsync()));   // ödeme sonrası masa panosuna dön
    }

    // ===== Adisyon işlemleri (PC ile aynı) =====
    private async void Ops_Clicked(object sender, EventArgs e)
    {
        var choice = await DisplayActionSheet("Adisyon İşlemleri", "Vazgeç", null,
            "➡️ Taşı", "✂️ Böl", "🔗 Birleştir", "👥 Kişi/Adisyon Aktar", "👥 Kişi Sayısı", "🖨 Mutfağa Yeniden Bas");
        if (choice == "➡️ Taşı") await MoveTable();
        else if (choice == "✂️ Böl") await SplitTable();
        else if (choice == "🔗 Birleştir") await MergeTable();
        else if (choice == "👥 Kişi/Adisyon Aktar") await TransferLines();
        else if (choice == "👥 Kişi Sayısı") await SetCover();
        else if (choice == "🖨 Mutfağa Yeniden Bas") { try { await _api.PostAsync($"/api/orders/{_orderId}/reprint"); await DisplayAlert("Mutfak", "Yeniden gönderildi.", "Tamam"); } catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); } }
    }

    // Tüm bölgelerdeki masaları topla (hedef seçimi için).
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
        if (all.Count == 0) // bölge listesi alınamadıysa en azından mevcut bölge
            try { all = await _api.GetAsync<List<TableCardDto>>($"/api/tables/board?zoneId={_zoneId}"); } catch { }
        return all;
    }

    private async Task<TableCardDto?> PickEmptyTableAsync(string title)
    {
        var all = await AllTablesAsync();
        var empties = all.Where(t => t.Status == 0 && t.TableId != _tableId).ToList();
        if (empties.Count == 0) { await DisplayAlert(title, "Boş masa yok.", "Tamam"); return null; }
        var sel = await DisplayActionSheet(title, "Vazgeç", null, empties.Select(t => t.Display).ToArray());
        return empties.FirstOrDefault(t => t.Display == sel);
    }

    private async Task<TableCardDto?> PickOccupiedTableAsync(string title)
    {
        var all = await AllTablesAsync();
        var occ = all.Where(t => t.Status == 1 && t.OrderId is not null && t.TableId != _tableId).ToList();
        if (occ.Count == 0) { await DisplayAlert(title, "Hedef olabilecek dolu masa yok.", "Tamam"); return null; }
        var sel = await DisplayActionSheet(title, "Vazgeç", null, occ.Select(t => t.Display).ToArray());
        return occ.FirstOrDefault(t => t.Display == sel);
    }

    // Üst satırlardan çoklu seçim (ActionSheet tek-seçimli olduğundan tek tek seçtirir; "Bitir" ile biter).
    private async Task<List<int>?> PickLinesAsync(string title)
    {
        // Yalnız taşınabilir (ödenmemiş) ana satırlar: alt satır, iptal ve ödenmiş (SettledClFicheId dolu) hariç
        var top = _lines.Where(l => l.ParentLineId == null && l.LineStatus != 4 && l.SettledClFicheId == null).ToList();
        if (top.Count == 0) { await DisplayAlert("Bilgi", "Taşınacak (ödenmemiş) kalem yok.", "Tamam"); return null; }
        var chosen = new List<int>();
        while (true)
        {
            var labels = top.Select(l => (chosen.Contains(l.Id) ? "☑ " : "☐ ") + $"{l.Qty:0.##}x {l.ProductName}").ToList();
            labels.Add("✅ Bitir");
            var sel = await DisplayActionSheet(title, "Vazgeç", null, labels.ToArray());
            if (sel is null || sel == "Vazgeç") return null;
            if (sel == "✅ Bitir") return chosen.Count > 0 ? chosen : null;
            var idx = labels.IndexOf(sel);
            if (idx < 0 || idx >= top.Count) continue;
            var id = top[idx].Id;
            if (chosen.Contains(id)) chosen.Remove(id); else chosen.Add(id);
        }
    }

    private async Task MoveTable()
    {
        try
        {
            var target = await PickEmptyTableAsync("Taşınacak masa");
            if (target is null) return;
            await _api.PostAsync($"/api/orders/{_orderId}/move", new MoveRequest(target.TableId));
            await DisplayAlert("Taşı", $"{target.Display} masasına taşındı.", "Tamam");
            await Navigation.PopToRootAsync();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async Task SplitTable()
    {
        var ids = await PickLinesAsync("Bölünecek kalemleri seçin");
        if (ids is null || ids.Count == 0) return;
        try
        {
            var target = await PickEmptyTableAsync("Hangi boş masaya bölünsün?");
            if (target is null) return;
            await _api.PostAsync<OrderDto>($"/api/orders/{_orderId}/split", new SplitRequest(target.TableId, ids));
            await DisplayAlert("Böl", $"Adisyon bölündü → {target.Display}", "Tamam");
            await Navigation.PopToRootAsync();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async Task MergeTable()
    {
        try
        {
            var target = await PickOccupiedTableAsync("Hangi masayla birleştirilsin?");
            if (target is null || target.OrderId is not { } toid) return;
            await _api.PostAsync($"/api/orders/{_orderId}/merge", new MergeRequest(toid));
            await DisplayAlert("Birleştir", $"{target.Display} ile birleştirildi.", "Tamam");
            await Navigation.PopToRootAsync();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async Task TransferLines()
    {
        var ids = await PickLinesAsync("Aktarılacak kalemleri seçin");
        if (ids is null || ids.Count == 0) return;
        try
        {
            var target = await PickOccupiedTableAsync("Hangi adisyona aktarılsın?");
            if (target is null || target.OrderId is not { } toid) return;
            await _api.PostAsync($"/api/orders/{_orderId}/transfer-lines", new TransferLinesRequest(toid, ids));
            await DisplayAlert("Aktar", $"Kalemler aktarıldı → {target.Display}", "Tamam");
            await Navigation.PopToRootAsync();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async Task SetCover()
    {
        var s = await DisplayPromptAsync("Kişi Sayısı", "Masadaki kişi sayısı:", "Tamam", "Vazgeç", keyboard: Keyboard.Numeric);
        if (!int.TryParse(s, out var n) || n < 0) return;
        try { await _api.PostAsync($"/api/orders/{_orderId}/cover", new { count = n }); }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    // Ödeme yetkili rol mü? İkram/İskonto yalnız bu rolde görünür (Garson'da gizli; backend de 403 verir).
    private bool CanPay => _state.Login?.Roles?.Any(r =>
        r.Equals("Kasiyer", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) == true;

    private async void Line_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not OrderLineDto line) return;
        LinesView.SelectedItem = null;
        string treatLabel = line.IsTreat ? "İkramı Kaldır" : "🎁 İkram";
        string? choice;
        if (CanPay)
            choice = await DisplayActionSheet(line.ProductName ?? "Kalem", "Vazgeç", null,
                treatLabel, "％ İskonto", "📝 Mutfak Notu", "🚫 İptal Et");
        else
            choice = await DisplayActionSheet(line.ProductName ?? "Kalem", "Vazgeç", null,
                "📝 Mutfak Notu", "🚫 İptal Et");
        try
        {
            switch (choice)
            {
                case "🎁 İkram":
                case "İkramı Kaldır":
                    await _api.PostAsync($"/api/orders/lines/{line.Id}/treat", new TreatRequest(!line.IsTreat));
                    break;
                case "％ İskonto":
                    var d = await DisplayPromptAsync("İskonto", $"İndirim tutarı (₺) — en çok {line.LineTotal:N2}:", "Uygula", "Vazgeç", keyboard: Keyboard.Numeric);
                    if (d is null) return;
                    if (!decimal.TryParse((d ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt) || amt < 0)
                    { await DisplayAlert("İskonto", "Geçerli bir tutar girin.", "Tamam"); return; }
                    if (amt > line.LineTotal)
                    { await DisplayAlert("İskonto", $"İndirim satır tutarını ({line.LineTotal:N2}) aşamaz.", "Tamam"); return; }
                    await _api.PostAsync($"/api/orders/lines/{line.Id}/discount", new DiscountRequest(amt));
                    break;
                case "📝 Mutfak Notu":
                    var note = await DisplayPromptAsync("Mutfak Notu", "Not:", "Kaydet", "Vazgeç");
                    if (note is null) return;
                    await _api.PostAsync($"/api/orders/lines/{line.Id}/note", new NoteRequest(note));
                    break;
                case "🚫 İptal Et":
                    var reason = await DisplayPromptAsync("İptal", "İptal sebebi:", "Devam", "Vazgeç");
                    if (reason is null) return;
                    bool stock = await DisplayAlert("Stok", "Ürün stoğa geri eklensin mi?", "Stoğa İade", "Fire");
                    await _api.DeleteAsync($"/api/orders/lines/{line.Id}?reason={Uri.EscapeDataString(reason)}&returnToStock={stock.ToString().ToLowerInvariant()}");
                    break;
                default: return;
            }
            await ReloadLines();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Back_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();
}
