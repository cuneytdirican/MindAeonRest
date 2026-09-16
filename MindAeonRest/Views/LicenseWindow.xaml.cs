using System;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class LicenseWindow : Window
{
    private readonly LicenseService _svc = new();
    private readonly string _companyCode;
    private readonly string _hwId;

    /// <summary>Pencere kapanınca lisans kullanılabilir mi (uygulamaya girilebilir mi).</summary>
    public bool Usable { get; private set; }
    public LicenseStatus Status { get; private set; }

    public LicenseWindow(string companyCode, string companyName, LicenseStatus status)
    {
        InitializeComponent();
        _companyCode = companyCode;
        _hwId = HardwareId.Current;
        Status = status;
        Usable = status.IsUsable;   // deneme geçerliyse kapatınca da devam edilebilir
        CompanyText.Text = $"{companyName} ({companyCode})";
        HwBox.Text = _hwId;
        PkgCombo.ItemsSource = new[]
        {
            new ComboItem((int)LicensePackage.Lite, "Lite"),
            new ComboItem((int)LicensePackage.Standart, "Standart"),
            new ComboItem((int)LicensePackage.Enterprise, "Enterprise")
        };
        PkgCombo.SelectedValue = (int)(status.State == LicenseState.Trial ? LicensePackage.Standart : status.Package);
        ShowStatus();
    }

    private void ShowStatus()
    {
        StatusText.Text = Status.StateText;
        TrialButton.Visibility = Status.State == LicenseState.Trial ? Visibility.Visible : Visibility.Collapsed;
        RebuildRequest();
    }

    private void RebuildRequest()
    {
        var pkg = PkgCombo.SelectedValue is int p ? (LicensePackage)p : LicensePackage.Standart;
        RequestBox.Text = _svc.BuildRequestKey(_companyCode, pkg, DealerBox?.Text);
    }

    private void Pkg_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded) RebuildRequest(); }
    private void Dealer_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (IsLoaded) RebuildRequest(); }

    private void CopyRequest_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(RequestBox.Text); Dialog.Info("Talep anahtarı panoya kopyalandı. Bize iletin.", "Lisans"); } catch { }
    }

    /// <summary>ONLINE kurulum: talep anahtarını GÖMÜLÜ lisans sunucusuna gönderir; onaylı/aktif lisans varsa anında etkinleştirir.
    /// Müşteri adres girmez/bilmez — adres AppConfig.LicenceApiUrl'de gömülüdür.</summary>
    private async void OnlineActivate_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        OnlineBtn.IsEnabled = false;
        var oldText = OnlineBtn.Content;
        OnlineBtn.Content = "Bağlanılıyor…";
        try
        {
            var res = await new LicenceApiClient().ActivateAsync(RequestBox.Text);
            if (res.Status == "issued" && !string.IsNullOrWhiteSpace(res.Token))
            {
                Status = _svc.Activate(_companyCode, res.Token!, _hwId);
                Session.CurrentLicense = Status;
                Usable = Status.IsUsable;
                Dialog.Success($"Lisans online etkinleştirildi.\n{Status.StateText}", "Lisans");
                DialogResult = true; Close();
                return;
            }
            if (res.Status == "pending")
                Dialog.Info(res.Message ?? "Lisans talebiniz alındı. Onaylandığında tekrar 'Online Etkinleştir' deyin.", "Lisans");
            else
                Err(res.Message ?? "Online aktivasyon başarısız. Offline (kopyala-yapıştır) yöntemini kullanabilirsiniz.");
        }
        catch (Exception ex) { Err("Online hata: " + ex.Message); }
        finally { OnlineBtn.IsEnabled = true; OnlineBtn.Content = oldText; }
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var key = (LicenseBox.Text ?? "").Trim();
        if (key.Length == 0) { Err("Lisans anahtarını yapıştırın."); return; }
        try
        {
            Status = _svc.Activate(_companyCode, key, _hwId);
            Session.CurrentLicense = Status;
            Usable = Status.IsUsable;
            Dialog.Success($"Lisans etkinleştirildi.\n{Status.StateText}", "Lisans");
            DialogResult = true; Close();
        }
        catch (Exception ex) { Err(ex.Message); }
    }

    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Lisans dosyası seç", Filter = "Lisans dosyası|*.lic;*.txt|Tümü|*.*" };
        if (dlg.ShowDialog() == true)
        {
            try { LicenseBox.Text = System.IO.File.ReadAllText(dlg.FileName).Trim(); ErrorText.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { Err("Dosya okunamadı: " + ex.Message); }
        }
    }

    private void Trial_Click(object sender, RoutedEventArgs e)
    {
        Session.CurrentLicense = Status;
        Usable = Status.IsUsable;   // Trial → true
        DialogResult = true; Close();
    }

    private void Err(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = Usable; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
