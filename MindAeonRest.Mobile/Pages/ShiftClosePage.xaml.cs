using System.Globalization;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile.Pages;

/// <summary>
/// Vardiya Kapanışı: bu terminaldeki (branch+kiosk) açık vardiyayı gösterir.
/// Açık vardiya yoksa "Vardiya Aç" (açılış kasası) akışı; varsa rapor (ödeme yöntemi + tahsil eden kullanıcı
/// kırılımı, açılış/beklenen nakit) + sayılan nakit + fark + "Vardiya Kapat". Yalnız Kasiyer/SysAdmin/Admin.
/// </summary>
public partial class ShiftClosePage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private ShiftDto? _shift;
    private ShiftReportDto? _report;

    public ShiftClosePage(AppState state, ApiClient api)
    {
        InitializeComponent();
        _state = state;
        _api = api;
    }

    // Ödeme/vardiya yetkili rol mü? (Kasiyer/SysAdmin/Admin) — backend de rol bazlı korur.
    private bool CanPay => _state.Login?.Roles?.Any(r =>
        r.Equals("Kasiyer", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) ||
        r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) == true;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!CanPay)
        {
            await DisplayAlert("Yetki", "Vardiya işlemleri için kasiyer/yönetici yetkisi gerekir.", "Tamam");
            await Navigation.PopAsync();
            return;
        }
        await Load();
    }

    private async Task Load()
    {
        Err(null);
        SetBusy(true);
        OpenPanel.IsVisible = false;
        ReportPanel.IsVisible = false;
        try
        {
            _shift = await _api.ShiftCurrentAsync();
            if (_shift is null)
            {
                // Açık vardiya yok → açılış akışı
                OpeningFloatEntry.Text = "0";
                OpenPanel.IsVisible = true;
            }
            else
            {
                _report = await _api.ShiftReportAsync(_shift.Id);
                RenderReport();
                ReportPanel.IsVisible = true;
            }
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { SetBusy(false); }
    }

    private void RenderReport()
    {
        if (_shift is null || _report is null) return;

        ShiftIdLbl.Text = $"#{_shift.Id}";
        OpenedAtLbl.Text = _report.OpenedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        OpeningFloatLbl.Text = Money(_report.OpeningFloat);
        TotalLbl.Text = Money(_report.Total);
        CashCollectedLbl.Text = Money(_report.CashCollected);
        ExpectedCashLbl.Text = Money(_report.ExpectedCash);

        // Ödeme yöntemi kırılımı
        PaymentsList.Children.Clear();
        var pays = _report.Payments ?? new();
        NoPayLbl.IsVisible = pays.Count == 0;
        foreach (var p in pays)
            PaymentsList.Children.Add(Line(PaymentPage.KindName(p.Kind), Money(p.Amount)));

        // Tahsil eden kullanıcı kırılımı
        ByUserList.Children.Clear();
        var users = _report.ByUser ?? new();
        NoUserLbl.IsVisible = users.Count == 0;
        foreach (var u in users)
            ByUserList.Children.Add(Line(string.IsNullOrWhiteSpace(u.UserName) ? $"#{u.UserId}" : u.UserName, Money(u.Amount)));

        // Sayılan nakdi beklenen ile başlat (kasiyer düzeltir), farkı hesapla
        CountedCashEntry.Text = _report.ExpectedCash.ToString("0.##", CultureInfo.InvariantCulture);
        RecalcDiff();
    }

    /// <summary>Etiket + tutar satırı (tema renginde).</summary>
    private View Line(string label, string value)
    {
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(0, 3) };
        g.Add(new Label { Text = label, FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation }
            .Dyn(Label.TextColorProperty, "TextColor"));
        var val = new Label { Text = value, FontSize = 14, FontAttributes = FontAttributes.Bold }
            .Dyn(Label.TextColorProperty, "TextColor");
        Grid.SetColumn(val, 1);
        g.Add(val);
        return g;
    }

    private void CountedCash_Changed(object? sender, TextChangedEventArgs e) => RecalcDiff();

    private void RecalcDiff()
    {
        if (_report is null) return;
        decimal counted = ParseAmount(CountedCashEntry.Text);
        decimal diff = counted - _report.ExpectedCash;
        DiffLbl.Text = (diff > 0 ? "+" : "") + Money(diff);
        var key = diff == 0 ? "Success" : (diff > 0 ? "Warning" : "Danger");
        DiffLbl.SetDynamicResource(Label.TextColorProperty, key);
    }

    private async void OpenShift_Clicked(object? sender, EventArgs e)
    {
        Err(null);
        decimal opening = ParseAmount(OpeningFloatEntry.Text);
        if (opening < 0) { Err("Açılış kasası negatif olamaz."); return; }

        OpenBtn.IsEnabled = false;
        try
        {
            _shift = await _api.ShiftOpenAsync(opening);
            await DisplayAlert("Vardiya", "Vardiya açıldı.", "Tamam");
            await Load();
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { OpenBtn.IsEnabled = true; }
    }

    private async void CloseShift_Clicked(object? sender, EventArgs e)
    {
        Err(null);
        if (_shift is null) { Err("Açık vardiya bulunamadı."); return; }
        decimal counted = ParseAmount(CountedCashEntry.Text);
        if (counted < 0) { Err("Sayılan nakit negatif olamaz."); return; }

        decimal diff = counted - (_report?.ExpectedCash ?? 0m);
        string diffText = (diff > 0 ? "+" : "") + Money(diff);
        bool ok = await DisplayAlert("Vardiya Kapat",
            $"Sayılan nakit: {Money(counted)}\nBeklenen kasa: {Money(_report?.ExpectedCash ?? 0m)}\nFark: {diffText}\n\nVardiya kapatılsın mı?",
            "Kapat", "Vazgeç");
        if (!ok) return;

        var note = string.IsNullOrWhiteSpace(NoteEntry.Text) ? null : NoteEntry.Text.Trim();
        CloseBtn.IsEnabled = false;
        try
        {
            await _api.ShiftCloseAsync(_shift.Id, counted, note);
            await DisplayAlert("Vardiya", "Vardiya kapatıldı.", "Tamam");
            _shift = null; _report = null;
            await Load();   // kapanış sonrası: açık vardiya yok → açılış paneli
        }
        catch (Exception ex) { Err(ex.Message); CloseBtn.IsEnabled = true; }
    }

    private async void Refresh_Clicked(object? sender, EventArgs e) => await Load();
    private async void Back_Clicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private static decimal ParseAmount(string? text) =>
        decimal.TryParse((text ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static string Money(decimal v) => $"₺{v:N2}";

    private void SetBusy(bool on) { Busy.IsRunning = on; Busy.IsVisible = on; }
    private void Err(string? m) { ErrorLbl.Text = m ?? ""; ErrorLbl.IsVisible = !string.IsNullOrEmpty(m); }
}

internal static class ShiftViewExtensions
{
    /// <summary>Akıcı DynamicResource ataması (tema değişince renk güncellenir).</summary>
    public static T Dyn<T>(this T view, BindableProperty prop, string key) where T : Element
    {
        view.SetDynamicResource(prop, key);
        return view;
    }
}
