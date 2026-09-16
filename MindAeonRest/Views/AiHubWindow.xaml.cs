using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services.Ai;

namespace MindAeonRest.Views;

public partial class AiHubWindow : Window
{
    private readonly AiHubService _hub = new();
    private readonly ObservableCollection<AiMessage> _messages = new();
    private bool _busy;
    private bool _listening;

    public AiHubWindow()
    {
        InitializeComponent();
        MessagesHost.ItemsSource = _messages;
        LoadSettings();
        _messages.Add(new AiMessage
        {
            IsUser = false,
            Text = "Merhaba! Size nasıl yardımcı olabilirim? Örnek: \"bugünün özeti\", \"en çok satanlar\", " +
                   "\"<cari adı> bakiye\", \"kritik stoklar\", \"nakit pozisyon\", \"faturalanmamış irsaliyeler\"."
        });
    }

    private void LoadSettings()
    {
        try
        {
            var s = new AiSettingsService().Get();
            // BULGU 5: dinleme aktifse veya sesli giriş kapatıldıysa mikrofonu gerçekten durdur
            // (yalnız butonu gizlemek recognizer'ı arka planda sıcak bırakırdı).
            if (_listening || !s.VoiceInput) StopListening();
            ProviderText.Text = $"Sağlayıcı: {s.Provider.ToTr()} · yaratıcılık {s.Temperature:0.0}";
            SpeakToggle.IsChecked = s.VoiceOutput;
            MicButton.Visibility = s.VoiceInput ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { ProviderText.Text = "Sağlayıcı: Yerel"; }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();
    private void Input_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Send(); e.Handled = true; } }

    private async void Send()
    {
        if (_busy) return;
        string q = InputBox.Text.Trim();
        if (q.Length == 0) return;
        InputBox.Clear();
        _messages.Add(new AiMessage { IsUser = true, Text = q });
        ScrollToEnd();

        _busy = true;
        var thinking = new AiMessage { IsUser = false, Text = "…" };
        _messages.Add(thinking);
        try
        {
            AiAnswer ans = await Task.Run(() => _hub.Ask(q));
            _messages.Remove(thinking);
            var msg = new AiMessage { IsUser = false, Text = ans.Text, Table = ans.Table };
            _messages.Add(msg);
            ScrollToEnd();
            if (SpeakToggle.IsChecked == true) SpeechService.Speak(ans.Text);
        }
        catch (Exception ex)
        {
            _messages.Remove(thinking);
            _messages.Add(new AiMessage { IsUser = false, Text = "Hata: " + ex.Message });
        }
        finally { _busy = false; }
    }

    private void ScrollToEnd() => Dispatcher.InvokeAsync(() => Scroller.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    private void Mic_Click(object sender, RoutedEventArgs e)
    {
        if (_listening) { StopListening(); return; }

        bool started = SpeechService.StartListening(
            onResult: text => Dispatcher.BeginInvoke(new Action(() =>
            {
                StopListening();                 // tek atış: tanınınca dinlemeyi kapat
                InputBox.Text = text;
                // BULGU 11: önceki soru hâlâ işleniyorsa Send() guard'da sessizce düşer.
                // Metni kutuda bırak + kullanıcıya ipucu ver; sessiz no-op'a güvenme.
                if (_busy)
                {
                    Dialog.Info("İşlem sürüyor; tanınan metin kutuya yazıldı. Bittiğinde 'Gönder'e basın.", "Sesli Giriş");
                    return;
                }
                Send();                          // otomatik gönder
            })),
            onError: msg => Dispatcher.BeginInvoke(new Action(() =>
            {
                StopListening();
                Dialog.Info(msg, "Sesli Giriş");
            })));

        if (started) SetListening(true);
    }

    private void StopListening()
    {
        SpeechService.StopListening();
        SetListening(false);
    }

    private void SetListening(bool on)
    {
        _listening = on;
        MicButton.Content = on ? "🔴" : "🎤";
        MicButton.ToolTip = on ? "Dinleniyor… durdurmak için tıkla" : "Sesle sor";
        if (on) MicButton.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x2D, 0x20));
        else MicButton.ClearValue(BackgroundProperty);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // BULGU 5: modal Ayarlar açıkken arka planda yakalama/otomatik-gönderim olmasın.
        StopListening();
        var w = new AiSettingsWindow { Owner = this };
        if (w.ShowDialog() == true) LoadSettings();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { SpeechService.Stop(); Close(); }

    protected override void OnClosed(EventArgs e)
    {
        SpeechService.StopListening();
        SpeechService.Stop();
        base.OnClosed(e);
    }
}
