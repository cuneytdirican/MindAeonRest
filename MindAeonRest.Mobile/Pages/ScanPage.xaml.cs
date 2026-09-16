using ZXing.Net.Maui;

namespace MindAeonRest.Mobile.Pages;

public partial class ScanPage : ContentPage
{
    private readonly Action<string?> _onResult;
    private bool _handled;

    /// <summary>onResult: okunan değer (iptalde null). Sonuç geldiğinde sayfa kapanır.</summary>
    public ScanPage(Action<string?> onResult)
    {
        InitializeComponent();
        _onResult = onResult;
        Reader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false,
            TryHarder = true
        };
    }

    private void OnDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handled) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        _handled = true;
        Reader.IsDetecting = false;
        Dispatcher.Dispatch(async () =>
        {
            await Navigation.PopModalAsync();
            _onResult(value);
        });
    }

    private async void Cancel_Clicked(object sender, EventArgs e)
    {
        if (_handled) return;
        _handled = true;
        Reader.IsDetecting = false;
        await Navigation.PopModalAsync();
        _onResult(null);
    }
}
