using System.Windows;
using MindAeonRest.Data;
using MindAeonRest.Views;

namespace MindAeonRest;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Kayıtlı temayı uygula (pencerelerden önce)
        MindAeonRest.Helpers.ThemeManager.LoadSaved();

        // Form/düzenleme pencereleri açılırken ekranın büyük kısmına yayılsın (az scroll, her şey görünür)
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MindAeonRest.Helpers.DialogSizing.OnWindowLoaded));

        // Yakalanmamış hataları kullanıcıya göster (sessiz çökmeyi önler)
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu.\n\nDetay: {args.Exception.Message}",
                "MindAeonRest - Hata",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 1) Veritabanı sunucusuna bağlantıyı test et
        var (ok, error) = Database.TestConnection();
        if (!ok)
        {
            MessageBox.Show(
                $"Veritabanı sunucusuna bağlanılamadı.\n\nDetay: {error}",
                "MindAeonRest - Bağlantı Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Mobil API yedek başlatma (yalnız API exe'si WPF ile paketlenmişse = sunucu makinesi)
        _ = Helpers.MobileServer.EnsureStartedAsync();

        // 2) Veritabanı / tablolar / varsayılan kayıtları hazırla
        try
        {
            DatabaseInitializer.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Veritabanı hazırlanırken hata oluştu.\n\nDetay: {ex.Message}",
                "MindAeonRest - Kurulum Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // 3) Giriş ekranını göster
        var login = new LoginWindow();
        login.Show();
    }
}
