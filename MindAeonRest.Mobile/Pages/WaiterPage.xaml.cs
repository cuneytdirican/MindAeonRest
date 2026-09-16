using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public partial class WaiterPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly HubService _hub;
    private List<ZoneDto> _zones = new();
    private ZoneDto? _zone;

    public WaiterPage(AppState state, ApiClient api, HubService hub)
    {
        InitializeComponent();
        _state = state;
        _api = api;
        _hub = hub;
        SubTitle.Text = $"{_state.Login?.BranchName}  ·  {_state.Login?.FullName}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.TicketReady += OnTicketReady;
        _hub.TableChanged += OnTableChanged;
        await _hub.EnsureConnectedAsync();
        if (_zones.Count == 0) await LoadZones();
        else await LoadBoard();   // adisyondan dönünce tazele
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.TicketReady -= OnTicketReady;
        _hub.TableChanged -= OnTableChanged;
    }

    private async void OnTicketReady(int lineId)
    {
        // Mutfaktan ürün hazır → şeridi vurgula + kısa bildirim
        ReadyBtn.Text = "🔔 HAZIR!";
        await Task.Delay(2500);
        ReadyBtn.Text = "🔔 Hazır";
    }

    private async void OnTableChanged() => await LoadBoard();

    private async void Ready_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new ReadyItemsPage(_api, _hub));

    private async Task LoadZones()
    {
        try
        {
            _zones = await _api.GetAsync<List<ZoneDto>>("/api/tables/zones");
            ZonesView.ItemsSource = _zones;
            if (_zones.Count > 0) ZonesView.SelectedItem = _zones[0];
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Zone_Selected(object sender, SelectionChangedEventArgs e)
    {
        _zone = e.CurrentSelection.FirstOrDefault() as ZoneDto;
        await LoadBoard();
    }

    private async Task LoadBoard()
    {
        if (_zone is null) { TablesView.ItemsSource = null; return; }
        try
        {
            Refresher.IsRefreshing = true;
            var board = await _api.GetAsync<List<TableCardDto>>($"/api/tables/board?zoneId={_zone.Id}");
            TablesView.ItemsSource = board;
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
        finally { Refresher.IsRefreshing = false; }
    }

    private async void Table_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TableCardDto t) return;
        TablesView.SelectedItem = null;
        if (_zone is null) return;
        await Navigation.PushAsync(new AdisyonPage(_state, _api, t.TableId, _zone.Id, t.Display));
    }

    private async void Refresh_Clicked(object sender, EventArgs e) => await LoadBoard();

    private async void Logout_Clicked(object sender, EventArgs e)
    {
        // Merkezi çıkış: SignalR'ı da durdurur (eski JWT ile bağlı kalmasın).
        if (Application.Current is App app) await app.LogoutAsync();
    }
}
