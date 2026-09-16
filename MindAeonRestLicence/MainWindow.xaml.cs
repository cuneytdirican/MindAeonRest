using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MindAeonRest.Licensing;

namespace MindAeonRestLicence;

public partial class MainWindow : Window
{
    private static readonly string KeyPath = Path.Combine(AppContext.BaseDirectory, "private.xml");
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "issued-licenses.json");
    private string? _privateKey;
    private RequestPayload? _decoded;

    public MainWindow()
    {
        InitializeComponent();
        PkgCombo.ItemsSource = new[] { "Lite", "Standart", "Enterprise" };
        PkgCombo.SelectedIndex = 1;
        TryLoadKey(KeyPath);
        Refresh_Click(this, new RoutedEventArgs());
    }

    private void TryLoadKey(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                _privateKey = File.ReadAllText(path);
                KeyStatus.Text = $"✓ Özel anahtar yüklü ({Path.GetFileName(path)})";
                KeyStatus.Foreground = (System.Windows.Media.Brush)FindResource("Ok");
            }
        }
        catch { }
    }

    private void PickKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Özel anahtar (private.xml)", Filter = "Anahtar|*.xml;*.key|Tüm|*.*" };
        if (dlg.ShowDialog() == true) TryLoadKey(dlg.FileName);
    }

    private void Decode_Click(object sender, RoutedEventArgs e)
    {
        _decoded = LicenseCrypto.DecodeRequest(RequestBox.Text?.Trim() ?? "");
        if (_decoded is null) { DecodedText.Text = "⚠ Talep anahtarı çözülemedi (geçersiz)."; DecodedText.Foreground = (System.Windows.Media.Brush)FindResource("Danger"); return; }
        DecodedText.Text = $"Şirket Kodu: {_decoded.CompanyCode}\nDonanım: {_decoded.HardwareId}\nİstenen paket: {_decoded.RequestedPackage}\nSürüm: {_decoded.AppVersion}  ·  Tarih: {_decoded.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
        DecodedText.Foreground = (System.Windows.Media.Brush)FindResource("Text");
        PkgCombo.SelectedIndex = (int)_decoded.RequestedPackage;
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        GenError.Visibility = Visibility.Collapsed;
        if (_privateKey is null) { Err("Önce özel anahtar dosyasını seçin (private.xml)."); return; }
        if (_decoded is null) { Err("Önce talep anahtarını çözün."); return; }
        var pkg = (LicensePackage)PkgCombo.SelectedIndex;
        DateTime? expiry = null;
        if (Dated.IsChecked == true)
        {
            if (ExpiryBox.SelectedDate is not { } d) { Err("Bitiş tarihi seçin (ya da 'Süresiz')."); return; }
            expiry = d.Date.AddDays(1).AddSeconds(-1).ToUniversalTime();
        }
        var lic = new LicensePayload
        {
            LicenseId = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            CompanyCode = _decoded.CompanyCode,
            HardwareId = _decoded.HardwareId,
            Package = pkg,
            MaxBranches = PackageMatrix.MaxBranches(pkg),
            MaxUsers = PackageMatrix.MaxUsers(pkg),
            FeatureBits = (long)PackageMatrix.Features(pkg),
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = expiry
        };
        try
        {
            string key = LicenseCrypto.SignLicense(lic, _privateKey);
            LicenseBox.Text = key;
            LogIssue(lic, key);
            Refresh_Click(this, new RoutedEventArgs());
            MessageBox.Show($"Lisans üretildi ve kaydedildi.\nLisans No: {lic.LicenseId}\nPaket: {PackageMatrix.Name(pkg)}", "Lisans", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Err("Üretim hatası: " + ex.Message); }
    }

    private void LogIssue(LicensePayload lic, string key)
    {
        var list = ReadLog();
        list.Insert(0, new IssuedRecord
        {
            LicenseId = lic.LicenseId, IssuedUtc = lic.IssuedUtc, Dealer = DealerBox.Text.Trim(), Contact = ContactBox.Text.Trim(),
            CompanyCode = lic.CompanyCode, HardwareId = lic.HardwareId, Package = (int)lic.Package,
            MaxBranches = lic.MaxBranches, MaxUsers = lic.MaxUsers, ExpiresUtc = lic.ExpiresUtc, Notes = NoteBox.Text.Trim(), LicenseKey = key
        });
        File.WriteAllText(LogPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<IssuedRecord> ReadLog()
    {
        try { return File.Exists(LogPath) ? JsonSerializer.Deserialize<List<IssuedRecord>>(File.ReadAllText(LogPath)) ?? new() : new(); }
        catch { return new(); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => IssuedGrid.ItemsSource = ReadLog();

    private void CopyLicense_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LicenseBox.Text)) { MessageBox.Show("Önce lisans üretin.", "Lisans"); return; }
        Clipboard.SetText(LicenseBox.Text.Trim());
        MessageBox.Show("Lisans anahtarı panoya kopyalandı.\nMüşteriye e-posta/WhatsApp ile gönderebilirsiniz.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveLicense_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LicenseBox.Text)) { MessageBox.Show("Önce lisans üretin.", "Lisans"); return; }
        var name = string.IsNullOrWhiteSpace(_decoded?.CompanyCode) ? "lisans" : _decoded!.CompanyCode;
        var dlg = new SaveFileDialog { Title = "Lisans dosyası kaydet", FileName = $"{name}.lic", Filter = "Lisans dosyası|*.lic|Metin|*.txt|Tümü|*.*" };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, LicenseBox.Text.Trim());
            MessageBox.Show($"Lisans dosyası kaydedildi:\n{dlg.FileName}\n\nMüşteriye bu dosyayı gönderebilirsiniz.", "Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void IssuedGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IssuedGrid.SelectedItem is not IssuedRecord rec || string.IsNullOrWhiteSpace(rec.LicenseKey)) return;
        Clipboard.SetText(rec.LicenseKey.Trim());
        LicenseBox.Text = rec.LicenseKey.Trim();
        MessageBox.Show($"'{rec.CompanyCode}' ({rec.PackageName}) lisans anahtarı panoya kopyalandı.\nYeniden gönderebilirsiniz.", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Err(string m) { GenError.Text = m; GenError.Visibility = Visibility.Visible; }
}

public class IssuedRecord
{
    public string LicenseId { get; set; } = "";
    public DateTime IssuedUtc { get; set; }
    public string Dealer { get; set; } = "";
    public string Contact { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public int Package { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public string Notes { get; set; } = "";
    public string LicenseKey { get; set; } = "";

    public string IssuedText => IssuedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string PackageName => PackageMatrix.Name((LicensePackage)Package);
    public string ExpiryText => ExpiresUtc is { } e ? e.ToLocalTime().ToString("dd.MM.yyyy") : "Süresiz";
}
