using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

/// <summary>
/// FAZ 4c — Basit personel mesai (time-clock) ekranı: personel seç → Giriş / Çıkış.
/// Açık (çıkışsız) mesai varsa "Çıkış", yoksa "Giriş" etkin olur. Branch/kiosk sunucuda (JWT) çözülür.
/// DİKKAT: Bu, kasa vardiyasından (ShiftClosePage) ayrıdır — personelin işe geliş-gidiş kaydıdır.
/// Programatik UI (XAML yok) — tek dosya, mevcut akışları etkilemez.
/// </summary>
public class TimeClockPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;

    private readonly Picker _personnelPicker = new() { Title = "Personel seçiniz" };
    private readonly Label _statusLbl = new() { FontSize = 15 };
    private readonly Entry _noteEntry = new() { Placeholder = "Not (opsiyonel)" };
    private readonly Button _clockInBtn = new() { Text = "Giriş (Mesai Başlat)", IsEnabled = false };
    private readonly Button _clockOutBtn = new() { Text = "Çıkış (Mesai Bitir)", IsEnabled = false };
    private readonly Label _errorLbl = new() { TextColor = Colors.Red, IsVisible = false };
    private readonly ActivityIndicator _busy = new() { IsVisible = false, IsRunning = false };

    private List<PersonnelPickDto> _personnel = new();
    private AttendanceDto? _open;

    public TimeClockPage(AppState state, ApiClient api)
    {
        _state = state;
        _api = api;
        Title = "Mesai (Puantaj)";

        _personnelPicker.SelectedIndexChanged += async (_, _) => await LoadOpenState();
        _clockInBtn.Clicked += ClockIn_Clicked;
        _clockOutBtn.Clicked += ClockOut_Clicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    new Label { Text = "Personel Mesai Takibi", FontSize = 20, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Personel", FontAttributes = FontAttributes.Bold },
                    _personnelPicker,
                    _statusLbl,
                    _noteEntry,
                    _clockInBtn,
                    _clockOutBtn,
                    _errorLbl,
                    _busy,
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPersonnel();
    }

    private async Task LoadPersonnel()
    {
        Err(null);
        SetBusy(true);
        try
        {
            _personnel = await _api.AttendancePersonnelAsync();
            _personnelPicker.ItemsSource = _personnel.Select(p => $"{p.Code} · {p.FullName}").ToList();
            if (_personnel.Count > 0) _personnelPicker.SelectedIndex = 0;
            else _statusLbl.Text = "Bu şubede aktif personel yok.";
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { SetBusy(false); }
    }

    private PersonnelPickDto? Selected =>
        _personnelPicker.SelectedIndex >= 0 && _personnelPicker.SelectedIndex < _personnel.Count
            ? _personnel[_personnelPicker.SelectedIndex] : null;

    private async Task LoadOpenState()
    {
        Err(null);
        var p = Selected;
        if (p is null) { _clockInBtn.IsEnabled = _clockOutBtn.IsEnabled = false; return; }

        SetBusy(true);
        try
        {
            _open = await _api.AttendanceOpenAsync(p.Id);
            if (_open is null)
            {
                _statusLbl.Text = $"{p.FullName}: açık mesai yok. Giriş yapabilirsiniz.";
                _clockInBtn.IsEnabled = true;
                _clockOutBtn.IsEnabled = false;
            }
            else
            {
                _statusLbl.Text = $"{p.FullName}: {_open.ClockIn.ToLocalTime():dd.MM.yyyy HH:mm} girişli AÇIK mesai. Çıkış yapabilirsiniz.";
                _clockInBtn.IsEnabled = false;
                _clockOutBtn.IsEnabled = true;
            }
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void ClockIn_Clicked(object? sender, EventArgs e)
    {
        var p = Selected;
        if (p is null) return;
        Err(null);
        SetBusy(true);
        try
        {
            await _api.ClockInAsync(p.Id, Note());
            _noteEntry.Text = "";
            await DisplayAlert("Mesai", $"{p.FullName} için giriş kaydedildi.", "Tamam");
            await LoadOpenState();
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void ClockOut_Clicked(object? sender, EventArgs e)
    {
        var p = Selected;
        if (p is null) return;
        Err(null);
        SetBusy(true);
        try
        {
            await _api.ClockOutAsync(p.Id, Note());
            _noteEntry.Text = "";
            await DisplayAlert("Mesai", $"{p.FullName} için çıkış kaydedildi.", "Tamam");
            await LoadOpenState();
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { SetBusy(false); }
    }

    private string? Note() => string.IsNullOrWhiteSpace(_noteEntry.Text) ? null : _noteEntry.Text.Trim();
    private void SetBusy(bool on) { _busy.IsVisible = on; _busy.IsRunning = on; }
    private void Err(string? m) { _errorLbl.Text = m ?? ""; _errorLbl.IsVisible = !string.IsNullOrEmpty(m); }
}
