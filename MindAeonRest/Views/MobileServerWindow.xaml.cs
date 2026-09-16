using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using QRCoder;

namespace MindAeonRest.Views;

public partial class MobileServerWindow : Window
{
    private readonly DispatcherTimer _timer;

    public MobileServerWindow()
    {
        InitializeComponent();
        UrlText.Text = MobileServer.Url;
        RenderQr(MobileServer.Url);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshStatus();
        Loaded += async (_, _) => await RefreshStatus();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private async System.Threading.Tasks.Task RefreshStatus()
    {
        bool up = await MobileServer.IsRunningAsync();
        StatusDot.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, up ? "SuccessBrush" : "DangerBrush");
        StatusText.Text = up ? $"Çalışıyor — {MobileServer.Url}" : "Durduruldu";
        StartBtn.IsEnabled = !up;
        StopBtn.IsEnabled = up;

        bool svc = await System.Threading.Tasks.Task.Run(MobileServer.IsServiceInstalled);
        ServiceStatus.Text = svc ? "✓ Kurulu (otomatik başlıyor)" : "Kurulu değil";
        InstallSvcBtn.IsEnabled = !svc;
        UninstallSvcBtn.IsEnabled = svc;

        bool installed = MobileServer.IsInstalled();
        var ver = MobileServer.InstalledVersion();
        VersionText.Text = installed ? (ver is null ? "API kurulu" : $"API sürümü: {ver}") : "API kurulu değil";
        UpdateBtn.IsEnabled = installed;

        bool pwa = MobileServer.IsPwaInstalled();
        PwaStatus.Text = pwa ? "✓ Kurulu" : (installed ? "Kurulu değil" : "Önce API'yi kurun");
        PwaUrlText.Text = pwa ? MobileServer.PwaUrl : "";
        PwaInstallBtn.IsEnabled = installed;
        PwaCopyBtn.IsEnabled = pwa;
    }

    private async void UpdateApi_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm("Mobil API yeni sürüme güncellensin mi?\n\nSunucu kısa süre durdurulup yeniden başlatılır. Mevcut appsettings.json ayarlarınız (SQL/Kiosk vb.) KORUNUR.", "API Güncelle"))
            return;
        InstallBtn.IsEnabled = false; UpdateBtn.IsEnabled = false; StartBtn.IsEnabled = false;
        var old = StatusText.Text;
        try
        {
            var (ok, msg) = await MobileServer.UpdateApiAsync(p => Dispatcher.Invoke(() =>
            {
                StatusDot.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                StatusText.Text = p;
            }));
            await System.Threading.Tasks.Task.Delay(2000);
            if (ok) Dialog.Success(msg, "API Güncelle"); else { StatusText.Text = old; Dialog.Warn(msg, "API Güncelle"); }
        }
        finally { InstallBtn.IsEnabled = true; await RefreshStatus(); }
    }

    private async void InstallPwa_Click(object sender, RoutedEventArgs e)
    {
        if (!MobileServer.IsInstalled()) { Dialog.Warn("Önce Mobil API'yi kurun. PWA, API'nin yanına kurulur ve API tarafından sunulur.", "PWA Kur"); return; }
        if (MobileServer.IsPwaInstalled() &&
            !Dialog.Confirm("PWA zaten kurulu. Yeniden indirilip güncellensin mi? (eski içerik temizlenir)", "PWA Kur"))
            return;
        PwaInstallBtn.IsEnabled = false;
        var old = StatusText.Text;
        try
        {
            var (ok, msg) = await MobileServer.DownloadAndInstallPwaAsync(p => Dispatcher.Invoke(() =>
            {
                StatusDot.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                StatusText.Text = p;
            }));
            StatusText.Text = old;
            if (ok) Dialog.Success(msg, "PWA Kur"); else Dialog.Warn(msg + "\n\nİnternet bağlantısını kontrol edin.", "PWA Kur");
        }
        finally { await RefreshStatus(); }
    }

    private void CopyPwa_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(MobileServer.PwaUrl); Dialog.Info("PWA adresi panoya kopyalandı.", "Mobil Sunucu"); } catch { }
    }

    private async void Firewall_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = MobileServer.OpenFirewall();
        if (ok) Dialog.Success(msg + "\nTelefondan tekrar deneyin.", "Güvenlik Duvarı"); else Dialog.Warn(msg, "Güvenlik Duvarı");
        await RefreshStatus();
    }

    private async void InstallService_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = MobileServer.InstallService();
        if (ok) Dialog.Success(msg, "Windows Hizmeti"); else Dialog.Warn(msg, "Windows Hizmeti");
        await System.Threading.Tasks.Task.Delay(1500);
        await RefreshStatus();
    }

    private async void UninstallService_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm("Mobil API Windows hizmeti kaldırılsın mı?", "Windows Hizmeti")) return;
        var (ok, msg) = MobileServer.UninstallService();
        if (ok) Dialog.Info(msg, "Windows Hizmeti"); else Dialog.Warn(msg, "Windows Hizmeti");
        await System.Threading.Tasks.Task.Delay(1000);
        await RefreshStatus();
    }

    private void RenderQr(string text)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            byte[] png = new PngByteQRCode(data).GetGraphic(10);
            var img = new BitmapImage();
            using var ms = new MemoryStream(png);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            QrImage.Source = img;
        }
        catch { /* QR üretilemezse adres yine elle girilebilir */ }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        // API kurulu değilse: indirip kurmayı öner (hızlı kurulum)
        if (!MobileServer.IsInstalled())
        {
            if (Dialog.Confirm("Mobil API bu bilgisayarda kurulu değil.\n\nŞimdi internetten indirilip bu PC'nin SQL ayarlarıyla otomatik kurulsun mu?", "Mobil Sunucu"))
                await InstallThenStartAsync();
            return;
        }
        var (ok, msg) = MobileServer.Start();
        if (!ok) Dialog.Warn(msg, "Mobil Sunucu");
        await System.Threading.Tasks.Task.Delay(2500);
        await RefreshStatus();
    }

    private async void InstallApi_Click(object sender, RoutedEventArgs e)
    {
        if (MobileServer.IsInstalled() &&
            !Dialog.Confirm("API zaten kurulu görünüyor. Yeniden indirilip kurulsun mu? (appsettings bu PC'ye göre yeniden yazılır)", "API Kur"))
            return;
        await InstallThenStartAsync();
    }

    private async System.Threading.Tasks.Task InstallThenStartAsync()
    {
        InstallBtn.IsEnabled = false; StartBtn.IsEnabled = false;
        var oldStatus = StatusText.Text;
        try
        {
            var (ok, msg) = await MobileServer.DownloadAndInstallAsync(p => Dispatcher.Invoke(() =>
            {
                StatusDot.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                StatusText.Text = p;
            }));
            if (ok)
            {
                MobileServer.OpenFirewall();   // 5000 portunu aç (yönetici onayı)
                MobileServer.Start();
                await System.Threading.Tasks.Task.Delay(2500);
                Dialog.Success(msg + "\nSunucu başlatıldı. 'Hizmet Kur' ile otomatik başlatmayı da açabilirsiniz.", "Mobil Sunucu");
            }
            else { StatusText.Text = oldStatus; Dialog.Warn(msg + "\n\nİnternet bağlantısını kontrol edin ya da 'Gözat' ile elle seçin.", "API Kur"); }
        }
        finally { InstallBtn.IsEnabled = true; StartBtn.IsEnabled = true; await RefreshStatus(); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        MobileServer.Stop();
        await System.Threading.Tasks.Task.Delay(800);
        await RefreshStatus();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        UrlText.Text = MobileServer.Url;
        RenderQr(MobileServer.Url);
        await RefreshStatus();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "MindAeonRest.Api.exe seçin",
            Filter = "MindAeonRest API|MindAeonRest.Api.exe|Çalıştırılabilir|*.exe|Tümü|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            MobileServer.CustomExePath = dlg.FileName;
            Dialog.Success("API konumu kaydedildi:\n" + dlg.FileName, "Mobil Sunucu");
            await RefreshStatus();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(MobileServer.Url); Dialog.Info("Adres panoya kopyalandı.", "Mobil Sunucu"); } catch { }
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
