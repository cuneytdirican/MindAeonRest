using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public partial class ReadyItemsPage : ContentPage
{
    private readonly ApiClient _api;
    private readonly HubService _hub;

    public ReadyItemsPage(ApiClient api, HubService hub)
    {
        InitializeComponent();
        _api = api; _hub = hub;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.TicketReady += OnReady;
        await Load();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.TicketReady -= OnReady;
    }

    private async void OnReady(int lineId) => await Load();

    private async Task Load()
    {
        try
        {
            Refresher.IsRefreshing = true;
            var all = await _api.GetAsync<List<KdsLineDto>>("/api/kitchen/tickets");
            ItemsView.ItemsSource = all.Where(t => t.LineStatus == 2).ToList();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
        finally { Refresher.IsRefreshing = false; }
    }

    private async void Deliver_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not KdsLineDto t) return;
        try
        {
            await _api.PostAsync($"/api/kitchen/lines/{t.LineId}/delivered");
            await Load();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private async void Refresh_Clicked(object sender, EventArgs e) => await Load();
    private async void Back_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();
}
