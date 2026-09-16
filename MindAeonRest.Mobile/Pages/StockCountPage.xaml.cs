using System.Collections.ObjectModel;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile.Services;
using ZXing.Net.Maui;

namespace MindAeonRest.Mobile.Pages;

/// <summary>
/// FAZ 4b Stok Zekâsı — mobil sayım. Barkod/ürün kodu okut (ZXing kamera, ScanPage deseni) veya elle gir →
/// çözülen ürün için miktar → sayım listesi → gönder. Gönderim bir Sayım evrağı oluşturur ve kesinleştirir
/// (/api/stock/count; DocumentPostingService.PostCount sistem stoğu ile farkı düzeltir).
/// </summary>
public partial class StockCountPage : ContentPage
{
    private readonly AppState _state;
    private readonly ApiClient _api;
    private readonly ObservableCollection<CountRow> _rows = new();
    private bool _scanBusy;

    public StockCountPage(AppState state, ApiClient api)
    {
        InitializeComponent();
        _state = state;
        _api = api;
        LinesView.ItemsSource = _rows;
        Reader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.OneDimensional | BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false,
            TryHarder = true
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (WarehousePicker.ItemsSource is null)
            await LoadWarehouses();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Reader.IsDetecting = false;
    }

    private async Task LoadWarehouses()
    {
        try
        {
            int branch = _state.Login?.BranchId ?? 0;
            var list = await _api.StockWarehousesAsync(branch);
            WarehousePicker.ItemsSource = list;
            if (list.Count > 0) WarehousePicker.SelectedIndex = 0;
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private void ToggleCamera_Clicked(object sender, EventArgs e)
    {
        bool on = !Reader.IsVisible;
        Reader.IsVisible = on;
        Reader.IsDetecting = on;
        CameraToggle.Text = on ? "📷 Kamerayı Kapat" : "📷 Kamera ile Okut";
    }

    private async void OnDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_scanBusy) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        _scanBusy = true;
        Reader.IsDetecting = false;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ResolveAndAdd(value!);
            // kısa gecikme sonrası taramaya devam (aynı barkodun tekrar tekrar okunmasını önle)
            await Task.Delay(600);
            if (Reader.IsVisible) Reader.IsDetecting = true;
            _scanBusy = false;
        });
    }

    private async void Find_Clicked(object sender, EventArgs e)
    {
        var code = CodeEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;
        await ResolveAndAdd(code);
        CodeEntry.Text = "";
    }

    private async Task ResolveAndAdd(string code)
    {
        try
        {
            var p = await _api.ResolveBarcodeAsync(code);
            string? qtyText = await DisplayPromptAsync(p.Name, "Sayılan miktar:", "Ekle", "İptal",
                initialValue: "1", keyboard: Keyboard.Numeric);
            if (qtyText is null) return;
            if (!decimal.TryParse(qtyText.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qty) || qty < 0)
            {
                await DisplayAlert("Geçersiz", "Miktar sayısal olmalı.", "Tamam");
                return;
            }
            var existing = _rows.FirstOrDefault(r => r.ProductId == p.ProductId);
            if (existing is not null) existing.Qty = qty;          // aynı ürün → son sayım geçerli
            else _rows.Add(new CountRow { ProductId = p.ProductId, Code = p.Code, Name = p.Name, Qty = qty });
            LinesView.ItemsSource = null; LinesView.ItemsSource = _rows;   // QtyText yenile
        }
        catch (ApiException ex) { await DisplayAlert("Ürün bulunamadı", ex.Message, "Tamam"); }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
    }

    private void Remove_Clicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is int pid)
        {
            var row = _rows.FirstOrDefault(r => r.ProductId == pid);
            if (row is not null) _rows.Remove(row);
        }
    }

    private async void Submit_Clicked(object sender, EventArgs e)
    {
        if (WarehousePicker.SelectedItem is not StockWarehouseDto wh)
        {
            await DisplayAlert("Depo", "Lütfen bir depo seçin.", "Tamam");
            return;
        }
        if (_rows.Count == 0)
        {
            await DisplayAlert("Sayım", "En az bir ürün sayın.", "Tamam");
            return;
        }
        if (!await DisplayAlert("Onay", $"{_rows.Count} kalem sayım {wh.Name} deposuna işlensin mi?", "Gönder", "Vazgeç"))
            return;
        try
        {
            SubmitBtn.IsEnabled = false;
            var req = new StockCountRequestDto(
                _state.Login?.BranchId, wh.Id,
                _rows.Select(r => new StockCountLineDto(r.ProductId, r.Qty)).ToList(),
                Post: true);
            var res = await _api.SubmitStockCountAsync(req);
            await DisplayAlert("Tamam", $"Sayım kaydedildi ve kesinleştirildi. Fiş: {res.FisNo}", "Tamam");
            _rows.Clear();
        }
        catch (Exception ex) { await DisplayAlert("Hata", ex.Message, "Tamam"); }
        finally { SubmitBtn.IsEnabled = true; }
    }

    private async void Close_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();

    private sealed class CountRow
    {
        public int ProductId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Qty { get; set; }
        public string QtyText => $"{Qty:0.###}";
    }
}
