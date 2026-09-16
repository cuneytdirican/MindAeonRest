using System.Globalization;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public partial class PatronPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly HubService _hub;
    private List<BranchDto> _branches = new();
    private bool _ready;

    public PatronPage(AppState state, ApiClient api, HubService hub)
    {
        InitializeComponent();
        _state = state; _api = api; _hub = hub;
        SubTitle.Text = $"{_state.Login?.CompanyName}  ·  {_state.Login?.LicensePackage}";
        DayPicker.Date = DateTime.Today;
        ShiftBtn.IsVisible = CanPay;   // Vardiya yalnız Kasiyer/SysAdmin/Admin
        CountBtn.IsVisible = CanPay;   // Mobil sayım yalnız Kasiyer/SysAdmin/Admin
    }

    private async void Count_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new StockCountPage(_state, _api));

    // Vardiya/ödeme yetkili rol mü? (Kasiyer/SysAdmin/Admin)
    private bool CanPay => _state.Login?.Roles?.Any(r =>
        r.Equals("Kasiyer", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) == true;

    private async void Shift_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new ShiftClosePage(_state, _api));

    // FAZ 4c — Personel mesai (time-clock). Vardiyadan ayrıdır; personel işe geliş-gidiş kaydı.
    private async void TimeClock_Clicked(object sender, EventArgs e) =>
        await Navigation.PushAsync(new TimeClockPage(_state, _api));

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _hub.TableChanged += OnTableChanged;
        await _hub.EnsureConnectedAsync();
        if (_branches.Count == 0) await LoadBranches();
        await Load();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.TableChanged -= OnTableChanged;
    }

    private async void OnTableChanged() => await Load();

    private async Task LoadBranches()
    {
        try
        {
            _branches = await _api.GetAsync<List<BranchDto>>("/api/auth/my-branches");
            BranchPicker.ItemsSource = _branches;
            int sel = _branches.FindIndex(b => b.Id == _state.Login?.BranchId);
            BranchPicker.SelectedIndex = sel >= 0 ? sel : (_branches.Count > 0 ? 0 : -1);
            _ready = true;
        }
        catch { _ready = true; }
    }

    private async void Filter_Changed(object sender, EventArgs e) { if (_ready) await Load(); }
    private async void Refresh_Clicked(object sender, EventArgs e) => await Load();

    private int BranchId => (BranchPicker.SelectedItem as BranchDto)?.Id ?? _state.Login?.BranchId ?? 0;

    private async Task Load()
    {
        if (BranchId <= 0) return;
        try
        {
            Refresher.IsRefreshing = true;
            string day = DayPicker.Date.ToString("yyyy-MM-dd");
            var d = await _api.GetAsync<DashboardDto>($"/api/reports/dashboard?branchId={BranchId}&date={day}");
            var tables = await _api.GetAsync<List<TableLiveDto>>($"/api/reports/tables-live?branchId={BranchId}");

            CiroLbl.Text = $"₺{d.SalesTotal:N2}";
            FisLbl.Text = d.AdisyonCount.ToString();
            OrtLbl.Text = $"₺{d.Average:N2}";
            KasaLbl.Text = $"₺{d.KasaTotal:N2}";
            GiftLiabLbl.Text = $"₺{d.GiftCardLiability:N2}";
            LoyaltyLiabLbl.Text = $"₺{d.LoyaltyLiability:N2}";

            var open = tables.Where(t => t.State == "Dolu").ToList();
            OpenTablesLbl.Text = open.Count.ToString();
            OpenAmountLbl.Text = $"₺{open.Sum(t => t.Total):N2}";

            BuildPayBars(d);
            BuildTopBars(d.TopProducts);
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
        finally { Refresher.IsRefreshing = false; }
    }

    private void BuildPayBars(DashboardDto d)
    {
        PayBars.Children.Clear();
        decimal max = Math.Max(Math.Max(d.Nakit, d.Kart), Math.Max(d.Diger, 1m));
        PayBars.Children.Add(Bar("Nakit", $"₺{d.Nakit:N2}", (double)(d.Nakit / max), "#17B26A"));
        PayBars.Children.Add(Bar("Kredi Kartı", $"₺{d.Kart:N2}", (double)(d.Kart / max), "#E91E63"));
        PayBars.Children.Add(Bar("Diğer (Yemek Çeki vb.)", $"₺{d.Diger:N2}", (double)(d.Diger / max), "#6A1B9A"));
    }

    private void BuildTopBars(List<TopProductDto> top)
    {
        TopBars.Children.Clear();
        NoTop.IsVisible = top.Count == 0;
        if (top.Count == 0) return;
        decimal max = Math.Max(top.Max(p => p.Amount), 1m);
        foreach (var p in top.Take(8))
            TopBars.Children.Add(Bar(p.Name, $"₺{p.Amount:N2}", (double)(p.Amount / max), "#C2185B"));
    }

    /// <summary>Etiket + oranlı yatay çubuk (tema renginde).</summary>
    private static View Bar(string label, string value, double ratio, string color)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        top.Add(new Label { Text = label, TextColor = Color.FromArgb("#2B2B3A"), FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation });
        var val = new Label { Text = value, TextColor = Color.FromArgb("#6A6F7A"), FontSize = 13, FontAttributes = FontAttributes.Bold };
        Grid.SetColumn(val, 1);
        top.Add(val);

        var track = new Grid { HeightRequest = 10, ColumnSpacing = 0 };
        track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(ratio, 0.001), GridUnitType.Star)));
        track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1 - ratio, 0.001), GridUnitType.Star)));
        var fill = new Border { BackgroundColor = Color.FromArgb(color), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 } };
        track.Add(fill);
        var rest = new BoxView { Color = Color.FromArgb("#EAECF1") };
        Grid.SetColumn(rest, 1);
        track.Add(rest);

        return new VerticalStackLayout { Spacing = 4, Children = { top, track } };
    }

    private async void Logout_Clicked(object sender, EventArgs e)
    {
        // Merkezi çıkış: SignalR durdurma + token/AppState temizleme + LoginPage.
        if (Application.Current is App app) await app.LogoutAsync();
    }
}
