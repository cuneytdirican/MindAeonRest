namespace MindAeonRest.Helpers;

/// <summary>
/// appsettings.json dosyasındaki "Database" bölümüne karşılık gelen ayarlar.
/// Varsayılan değerler ilk çalıştırmada dosyaya yazılır.
/// </summary>
public class DatabaseSettings
{
    public string Server { get; set; } = "sqlSunucuAdi";
    public string UserId { get; set; } = "sqlKullanicisi";
    public string Password { get; set; } = "KullaniciSifresi";
    public string DatabaseName { get; set; } = "MindAeonRest";//Kulalnıcı Değiştirenez
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeout { get; set; } = 30;
}

/// <summary>
/// appsettings.json "Kiosk" bölümü. KIOSKNO bu terminalin (kurulumun) KIOSK numarasıdır.
/// Login'de okunur ve oturum boyunca DEĞİŞTİRİLEMEZ — terminal kimliği config'te sabittir.
/// </summary>
public class KioskSettings
{
    public int No { get; set; } = 1;   // KIOSKNO — varsayılan 1 No'lu KIOSK
}

/// <summary>appsettings.json kök nesnesi.</summary>
/// <summary>appsettings.json "Licence" bölümü — lisans portalı API adresi (online aktivasyon/ciro raporu).
/// Boşsa yalnız offline (kopyala-yapıştır) aktivasyon kullanılır.</summary>
public class LicenceSettings
{
    public string ApiUrl { get; set; } = "";   // ör. https://mindaeonrestlicenceapi.mobilteg.com
}

public class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public KioskSettings Kiosk { get; set; } = new();
    public LicenceSettings Licence { get; set; } = new();
}
