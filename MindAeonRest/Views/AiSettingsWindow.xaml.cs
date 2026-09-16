using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services.Ai;

namespace MindAeonRest.Views;

public partial class AiSettingsWindow : Window
{
    public AiSettingsWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("AI sağlayıcı ayarları yalnızca yöneticiler içindir.", "AI Ayarları"); Loaded += (_, __) => Close(); return; }
        ProviderCombo.ItemsSource = new[]
        {
            new ComboItem((int)AiProviderType.Local, AiProviderType.Local.ToTr()),
            new ComboItem((int)AiProviderType.Claude, AiProviderType.Claude.ToTr()),
            new ComboItem((int)AiProviderType.OpenAI, AiProviderType.OpenAI.ToTr()),
            new ComboItem((int)AiProviderType.Custom, AiProviderType.Custom.ToTr()),
        };
        PresetCombo.ItemsSource = AiPreset.All;
        Load();
    }

    private void Load()
    {
        var s = new AiSettingsService().Get();
        ProviderCombo.SelectedValue = (int)s.Provider;
        KeyBox.Password = s.ApiKey ?? "";
        ModelBox.Text = s.Model ?? "";
        UrlBox.Text = s.BaseUrl ?? "";
        TempSlider.Value = s.Temperature;
        VoiceOutCheck.IsChecked = s.VoiceOutput;
        VoiceInCheck.IsChecked = s.VoiceInput;
        UpdateVisibility();
    }

    private AiProviderType Selected => (AiProviderType)(int)(ProviderCombo.SelectedValue ?? 0);

    private void Provider_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (!IsLoaded && ProviderCombo.SelectedValue is null) return;
        var p = Selected;
        bool cloud = p is AiProviderType.Claude or AiProviderType.OpenAI or AiProviderType.Custom;
        bool custom = p == AiProviderType.Custom;
        KeyPanel.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        UrlPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        // Hazır sağlayıcı kısayolu her zaman görünür: seçilince provider otomatik "Özel" olur (keşfedilebilirlik).
        PresetPanel.Visibility = Visibility.Visible;
    }

    private void Preset_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is AiPreset p)
        {
            // Hazır sağlayıcı (DeepSeek/OpenRouter/Grok/Gemini/Ollama…) seçildi →
            // sağlayıcıyı otomatik "Özel"e (OpenAI-uyumlu, BaseUrl + anahtar) çevir ki bulut devreye girsin.
            if (Selected != AiProviderType.Custom)
                ProviderCombo.SelectedValue = (int)AiProviderType.Custom;   // Provider_Changed → UpdateVisibility tetikler
            UrlBox.Text = p.BaseUrl;
            // BULGU 6: preset değişince Model'i koşulsuz güncelle; aksi halde eski preset'in
            // model'i yeni BaseUrl ile eşleşip yanlış endpoint/model kaydedilirdi.
            // (ModelBox bir TextBox; metin atamak Preset_Changed'i yeniden tetiklemez.)
            ModelBox.Text = p.Model;
            UpdateVisibility();   // KeyPanel/UrlPanel görünür olsun (idempotent)
        }
    }

    private void Temp_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TempText != null) TempText.Text = e.NewValue.ToString("0.0");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = Selected;
        if (p == AiProviderType.Custom && string.IsNullOrWhiteSpace(UrlBox.Text))
        { Dialog.Warn("Özel sağlayıcı için uç nokta (BaseUrl) gerekli."); return; }

        var s = new AiSettings
        {
            Provider = p,
            ApiKey = string.IsNullOrWhiteSpace(KeyBox.Password) ? null : KeyBox.Password.Trim(),
            Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? null : ModelBox.Text.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(UrlBox.Text) ? null : UrlBox.Text.Trim(),
            Temperature = TempSlider.Value,
            VoiceOutput = VoiceOutCheck.IsChecked == true,
            VoiceInput = VoiceInCheck.IsChecked == true
        };
        try { new AiSettingsService().Save(s, Session.CurrentUserId); DialogResult = true; }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
