# MindAeonRest

MindAeonRest, restoranlar ve kafeler için geliştirilmiş çok kiracılı (multi-tenant) bir adisyon, POS ve işletme yönetimi platformudur. Masa/QR siparişi, mutfak ekranı, kasa, stok, cari, raporlama, sadakat ve online rezervasyon modüllerini tek ekosistemde birleştirir.

> **Bu proje Mindaeon ile geliştirilmiştir.**
> İsteyen herkes projeyi **ücretsiz** olarak kullanabilir; **ticari kullanım dahil** her türlü kullanıma açıktır. Lisans: **MIT** (bkz. [LICENSE](LICENSE)).

## Özellikler

- Çok kiracılı yapı: şirket bazlı veritabanı izolasyonu ve JWT tabanlı kimlik doğrulama
- Kasa/POS (WPF), mutfak ekranı ve garson mobil uygulaması (.NET MAUI)
- Web back office: ürün/masa/şube/kullanıcı yönetimi, stok, cari, kasa, raporlar
- Müşteri tarafı PWA: QR ile masa siparişi, sadakat portalı, online rezervasyon, anket
- SignalR ile canlı sipariş/mutfak akışı
- Merkezî lisanslama: Licence API + Portal, imzalı lisans token'ları, paket ve özellik yönetimi

## Proje Yapısı

| Proje | Açıklama |
| --- | --- |
| `MindAeonRest` | WPF kasa/POS uygulaması (`net9.0-windows`) |
| `MindAeonRest.Api` | ASP.NET Core REST API, JWT, SignalR |
| `MindAeonRestWeb` | Razor Pages web back office |
| `MindAeonRest.Pwa` | Müşteri tarafı PWA (QR sipariş, portal) |
| `MindAeonRest.Mobile` | .NET MAUI mobil uygulama (Android/Windows) |
| `MindAeonRest.Core` | Ortak iş mantığı, veri erişimi ve servisler |
| `MindAeonRest.Licensing` | Lisans imzalama/doğrulama çekirdeği |
| `MindAeonRest.Licence.Api` | Merkezî lisans API'si |
| `MindAeonRest.Licence.Portal` | Lisans yönetim portalı |
| `MindAeonRest.Core.Tests` | Birim testleri (xUnit) |

## Gereksinimler

- .NET 9 SDK
- SQL Server (2019 veya üzeri önerilir)
- Windows (WPF ve Windows hedefli MAUI için)

## Kurulum

1. Depoyu klonlayın ve çözümü derleyin:

   ```bash
   git clone https://github.com/cuneytdirican/MindAeonRest.git
   cd MindAeonRest
   dotnet build MindAeonRest.sln
   ```

2. Veritabanı bağlantısını yapılandırın. Depodaki `appsettings.json` dosyaları **yalnızca yer tutucu** içerir; gerçek değerleri asla commit etmeyin.

   ASP.NET Core projelerinde (`MindAeonRest.Api`, `MindAeonRestWeb`, `MindAeonRest.Licence.Api`) ortam değişkenleri kullanılabilir:

   ```text
   Database__Server=...
   Database__UserId=...
   Database__Password=...
   Jwt__Key=...
   ```

   WPF uygulaması (`MindAeonRest`) ayarları çalışma dizinindeki `appsettings.json` dosyasından okur. Bu dosyayı yerel ortamınızda doldurun ve repoya eklemeyin.

3. Testleri çalıştırın:

   ```bash
   dotnet test MindAeonRest.Core.Tests/MindAeonRest.Core.Tests.csproj
   ```

## Güvenlik

- Repoda parola, özel anahtar veya üretim sırrı **bulunmaz**; tüm gizli değerler yer tutucudur.
- `appsettings.Production.json`, `appsettings.Local.json`, lisans/Android anahtar dosyaları `.gitignore` ile hariç tutulmuştur.
- Üretimde güçlü ve benzersiz `Jwt__Key` ile `Provisioning__ApiKey` değerleri kullanın.
- Güvenlik açığı bildirimleri için lütfen herkese açık issue açmak yerine doğrudan iletişime geçin.

## Lisans

MIT License — ücretsiz kullanım, değiştirme, dağıtım ve **ticari kullanım** dahil geniş izinler. Ayrıntılar için [LICENSE](LICENSE) dosyasına bakın.
