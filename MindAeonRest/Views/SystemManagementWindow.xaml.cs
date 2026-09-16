using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;

namespace MindAeonRest.Views;

/// <summary>SysAdmin için tek merkez: WPF uygulama self-update + mobil sunucu yönetimi.</summary>
public partial class SystemManagementWindow : Window
{
    private AppUpdater.UpdateInfo? _info;

    public SystemManagementWindow()
    {
        InitializeComponent();
        CurrentVersionText.Text = $"Mevcut sürüm: v{AppUpdater.CurrentText}";
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        UpdateBtn.Visibility = Visibility.Collapsed;
        UpdateBtn.IsEnabled = false;
        NotesBox.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = "Güncellemeler kontrol ediliyor…";
        try
        {
            _info = await AppUpdater.CheckAsync();
            if (_info is null)
            {
                UpdateStatusText.Text = "Güncelleme sunucusuna ulaşılamadı.";
                Dialog.Warn("Güncelleme sunucusuna ulaşılamadı.\nİnternet bağlantınızı kontrol edip tekrar deneyin.", "Uygulama Güncelleme");
                return;
            }

            if (!_info.HasUpdate)
            {
                UpdateStatusText.Text = $"En güncel sürümü kullanıyorsunuz (v{_info.Latest}).";
                Dialog.Info($"En güncel sürümü kullanıyorsunuz (v{_info.Latest}).", "Uygulama Güncelleme");
                return;
            }

            // Güncelleme mevcut
            UpdateStatusText.Text = $"Yeni sürüm mevcut: v{_info.Latest}";
            LatestVersionText.Text = $"Yeni sürüm: v{_info.Latest}  (şu an v{AppUpdater.CurrentText})";
            NotesText.Text = string.IsNullOrWhiteSpace(_info.Notes) ? "(Sürüm notu yok)" : _info.Notes;
            NotesBox.Visibility = Visibility.Visible;
            UpdateBtn.Visibility = Visibility.Visible;
            UpdateBtn.IsEnabled = true;
        }
        finally
        {
            CheckBtn.IsEnabled = true;
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null || !_info.HasUpdate) return;
        if (!Dialog.Confirm(
                $"v{_info.Latest} sürümüne güncellensin mi?\n\nUygulama kapanıp yeni sürümle yeniden başlatılacak. Açık ekranlardaki kaydedilmemiş veriler kaybolabilir.",
                "Uygulama Güncelleme"))
            return;

        CheckBtn.IsEnabled = false;
        UpdateBtn.IsEnabled = false;
        try
        {
            var (ok, msg) = await AppUpdater.DownloadAndApplyAsync(
                _info.Url,
                p => Dispatcher.Invoke(() => UpdateStatusText.Text = p));

            if (ok)
            {
                Dialog.Info(msg, "Uygulama Güncelleme");
                Application.Current.Shutdown();
            }
            else
            {
                UpdateStatusText.Text = "Güncelleme başarısız.";
                Dialog.Warn(msg, "Uygulama Güncelleme");
            }
        }
        finally
        {
            CheckBtn.IsEnabled = true;
            UpdateBtn.IsEnabled = true;
        }
    }

    private void OpenMobileServer_Click(object sender, RoutedEventArgs e)
        => new MobileServerWindow { Owner = this }.ShowDialog();

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
