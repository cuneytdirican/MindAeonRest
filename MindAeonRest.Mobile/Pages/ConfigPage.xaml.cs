using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

public partial class ConfigPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;

    public ConfigPage(AppState state, ApiClient api)
    {
        InitializeComponent();
        _state = state;
        _api = api;
        // Sadece elle girilmiş override gösterilir; bulut merkez varsayılanı boş bırakılır.
        UrlEntry.Text = _state.ApiBaseUrlOverride ?? "";
    }

    private async void Scan_Clicked(object sender, EventArgs e)
    {
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Kamera İzni", "QR okumak için kamera izni gerekli. Ayarlardan izin verin.", "Tamam");
            return;
        }
        await Navigation.PushModalAsync(new ScanPage(async value =>
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            UrlEntry.Text = AppState.NormalizeUrl(value.Trim());
            Status(true, "QR okundu, bağlantı deneniyor…");
            if (await _api.TestConnectionAsync(UrlEntry.Text))
            {
                _state.SaveApiUrl(UrlEntry.Text);
                (Application.Current as App)?.GoTo<LoginPage>();
            }
            else Status(false, "✗ QR okundu ama sunucuya ulaşılamadı. Aynı ağda mısınız?");
        }));
    }

    private async void Test_Clicked(object sender, EventArgs e)
    {
        var url = UrlEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(url)) { Status(false, "Adres girin."); return; }
        TestBtn.IsEnabled = false;
        Status(true, "Bağlanılıyor…");
        bool ok = await _api.TestConnectionAsync(url);
        Status(ok, ok ? "✓ Bağlantı başarılı" : "✗ Sunucuya ulaşılamadı");
        TestBtn.IsEnabled = true;
    }

    private async void Save_Clicked(object sender, EventArgs e)
    {
        var url = UrlEntry.Text ?? "";
        // Boş → override kaldır, bulut merkeze dön.
        if (string.IsNullOrWhiteSpace(url))
        {
            _state.SaveApiUrl(null);
            (Application.Current as App)?.GoTo<LoginPage>();
            return;
        }
        SaveBtn.IsEnabled = false;
        if (!await _api.TestConnectionAsync(url))
        {
            Status(false, "✗ Sunucuya ulaşılamadı. Adresi kontrol edin.");
            SaveBtn.IsEnabled = true;
            return;
        }
        _state.SaveApiUrl(url);
        (Application.Current as App)?.GoTo<LoginPage>();
    }

    /// <summary>Özel override'ı temizler ve bulut merkez varsayılanına döner.</summary>
    private void Default_Clicked(object sender, EventArgs e)
    {
        _state.SaveApiUrl(null);
        UrlEntry.Text = "";
        (Application.Current as App)?.GoTo<LoginPage>();
    }

    private void Status(bool ok, string msg)
    {
        StatusLabel.Text = msg;
        StatusLabel.TextColor = ok ? Color.FromArgb("#5DBB7E") : Color.FromArgb("#E05D5D");
        StatusLabel.IsVisible = true;
    }
}
