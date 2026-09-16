using MindAeonRest.Helpers;
using MindAeonRest.Licensing;

namespace MindAeonRestWeb.Services;

/// <summary>Back-office sol menü ağacı — WPF MainWindow navigasyonunun birebir karşılığı (17 bölüm).
/// Url null → henüz taşınmadı ("yakında", pasif). Feature → lisans kapısı (null=Core). AdminOnly → yalnız SysAdmin/Patron.
/// Form taşındıkça ilgili öğenin Url'i doldurulur.</summary>
public static class BackOfficeNav
{
    // Help: opsiyonel kısa Türkçe açıklama (bağlam-farkındalık için). null → çağıran Label'a düşer.
    // /api/ai/ask gibi uçlar path→{Label,Section,Help} çözüp AI'ya "kullanıcı şu ekranda: ..." bağlamı verir.
    // EN SONDA opsiyonel → mevcut tüm new(...) çağrıları değişmeden derlenir.
    public sealed record Item(string Label, string Icon, string? Url, Feature? Feature = null, bool AdminOnly = true, string? Help = null);
    public sealed record Section(string Title, string Icon, List<Item> Items);

    public static List<Section> All { get; } = new()
    {
        new("POS Ekranları", "🖥", new()
        {
            new("Adisyon (Garson)", "🧾", "/PosHandoff?mode=waiter", Feature.Adisyon),
            new("Mutfak (KDS)", "👨‍🍳", "/PosHandoff?mode=kitchen", Feature.Kitchen),
            new("Kasiyer", "💳", "/PosHandoff?mode=cashier"),
            new("Patron Ekranı", "📊", "/PosHandoff?mode=patron"),
        }),
        new("Ürün Yönetimi", "📦", new()
        {
            new("Kategoriler", "🗂", "/BackOffice/Categories", Feature.ProductBasic, Help: "Ürün kategorilerini yönetirsiniz"),
            new("Ürünler", "📦", "/BackOffice/Products", Feature.ProductBasic, Help: "Ürün kartları, reçete ve fiyatlarını yönetirsiniz"),
            new("Vergi Grupları", "🧾", "/BackOffice/TaxGroups"),
            new("Birimler", "📏", "/BackOffice/Units"),
            new("Fiyat Listeleri", "🏷", "/BackOffice/PriceLists", Feature.ProductBasic),
            new("Kod Serileri", "🔢", "/BackOffice/CodeSeries"),
            new("Satış Durdurma", "⛔", "/BackOffice/SalesStop", Feature.ProductBasic),
            new("Toplu Fiyat", "₺", "/BackOffice/BulkPrice", Feature.ProductBasic),
            new("Fiyat Matrisi", "🔀", "/BackOffice/PriceMatrix", Feature.ProductBasic),
            new("Fiyat Aktarımı", "🔁", "/BackOffice/PriceTransfer", Feature.ProductBasic),
            new("Hızlı Ürün", "⚡", "/BackOffice/QuickProduct", Feature.ProductBasic),
            new("CSV İçe Aktarım", "⬆", "/BackOffice/CsvImport", Feature.ProductBasic),
            new("Depolar", "🏬", "/BackOffice/Warehouses"),
            new("Kampanyalar", "🎁", "/BackOffice/Campaigns", Feature.Campaign),
            new("Kampanya Gönderimi", "📣", "/BackOffice/CampaignSend", Feature.Campaign),
        }),
        new("Müşteri Yönetimi", "👥", new()
        {
            new("Müşteriler", "👥", "/BackOffice/Customers", Feature.BasicCustomer),
            new("Müşteri Grupları", "👪", "/BackOffice/CustomerGroups", Feature.BasicCustomer),
        }),
        new("Lokasyon Yönetimi", "📍", new()
        {
            new("Lokasyonlar", "📍", "/BackOffice/Zones", Feature.LocationTables),
            new("Reyonlar (Mutfak)", "🍳", "/BackOffice/Reyons", Feature.Kitchen),
            new("Masa Planı", "🪑", "/BackOffice/TableLayout", Feature.LocationTables),
            new("Masa QR Kodları", "📱", "/BackOffice/QrCodes", Feature.LocationTables),
            new("Reyon Yönlendirme", "🔀", "/BackOffice/ReyonRouting", Feature.Kitchen),
        }),
        new("Finans — Tanımlar", "🏦", new()
        {
            new("Bankalar", "🏦", "/BackOffice/Banks", Feature.PreAccounting),
            new("Banka Hesapları", "💳", "/BackOffice/BankAccounts", Feature.PreAccounting),
            new("Kredi Kartı POS", "💳", "/BackOffice/Pos", Feature.PreAccounting),
            new("Nakit Kasalar", "💵", "/BackOffice/CashRegisters"),
            new("KIOSK'lar", "🖥", "/BackOffice/Kiosks", Feature.SelfService),
            new("Yemek Çekleri", "🍽", "/BackOffice/MealVouchers"),
            new("Yemek Çek Hesapları", "🧾", "/BackOffice/MealVoucherAccounts"),
        }),
        new("Finans — İşlemler", "💰", new()
        {
            new("Dekont (Kasa/Banka/Kart)", "💰", "/BackOffice/Dekont", Feature.PreAccounting, Help: "Kasa/Banka/Kart giriş-çıkış hareketlerini cariye işlersiniz"),
            new("Banka Hareketleri", "🏦", "/BackOffice/BankMovements", Feature.PreAccounting),
            new("POS Hareketleri", "💳", "/BackOffice/PosMovements", Feature.PreAccounting),
            new("Kasa Hareketleri", "💵", "/BackOffice/CashMovements"),
            new("Çek / Senet", "🧾", "/BackOffice/CekSenet", Feature.PreAccounting),
            new("Yemek Çeki Çıkışı", "🎫", "/BackOffice/MealVoucherOut"),
            new("Ekstreler", "📒", "/BackOffice/Extracts", Feature.PreAccounting),
            new("Hediye Çekleri", "🎁", "/BackOffice/GiftCards"),
            new("Sadakat Ayarları", "⭐", "/BackOffice/Loyalty"),
            new("İade (Ödeme Sonrası)", "↩", "/BackOffice/Returns"),
        }),
        new("Rezervasyon", "🗓", new()
        {
            new("Rezervasyon Etiketleri", "🏷", "/BackOffice/ResTags", Feature.Reservation),
            new("Rezervasyon Ekranı", "🗓", "/BackOffice/Reservations", Feature.Reservation),
        }),
        new("Paket / Kurye", "🛵", new()
        {
            new("Kuryeler", "🛵", "/BackOffice/Couriers", Feature.Delivery),
            new("Yemek Sitesi Ayarları", "🛍", "/BackOffice/DeliverySettings", Feature.Delivery),
        }),
        new("Şirket Yönetimi", "🏢", new()
        {
            new("Şirket Bilgileri", "🏢", "/BackOffice/Company"),
            new("Şubeler", "🏪", "/BackOffice/Branches"),
        }),
        new("Proje / Personel", "🧑", new()
        {
            new("Projeler", "📁", "/BackOffice/Projects", Feature.PreAccounting),
            new("Personel", "🧑", "/BackOffice/Personnel", Feature.PreAccounting),
            new("Puantaj / Mesai", "⏱", "/BackOffice/Personnel/Attendance", Feature.PreAccounting),
            new("Gider Tipleri", "🧾", "/BackOffice/ExpenseTypes", Feature.PreAccounting),
        }),
        new("Ön Muhasebe", "📒", new()
        {
            new("Cari Hareketler", "📒", "/BackOffice/CariHareket", Feature.PreAccounting),
            new("Alış Siparişleri", "🧾", "/BackOffice/Documents?type=10", Feature.PreAccounting),
            new("Satış Siparişleri", "🧾", "/BackOffice/Documents?type=11", Feature.PreAccounting),
            new("Alış İrsaliyeleri", "🚚", "/BackOffice/Documents?type=12", Feature.PreAccounting),
            new("Satış İrsaliyeleri", "🚚", "/BackOffice/Documents?type=13", Feature.PreAccounting),
            new("Alış Faturaları", "📄", "/BackOffice/Documents?type=14", Feature.PreAccounting),
            new("Satış Faturaları", "📄", "/BackOffice/Documents?type=15", Feature.PreAccounting),
            new("Alış İade Faturaları", "↩", "/BackOffice/Documents?type=16", Feature.PreAccounting),
            new("Satış İade Faturaları", "↪", "/BackOffice/Documents?type=17", Feature.PreAccounting),
            new("Depo Transfer", "🔁", "/BackOffice/Documents?type=18", Feature.PreAccounting),
            new("Sayım", "🧮", "/BackOffice/Documents?type=19", Feature.PreAccounting),
            new("Fire", "🗑", "/BackOffice/Documents?type=20", Feature.PreAccounting),
            new("Giderler", "💸", "/BackOffice/Expenses", Feature.PreAccounting),
            new("Muhasebe Aktarım", "📤", "/BackOffice/Accounting/Export", Feature.PreAccounting),
        }),
        new("Raporlar", "📊", new()
        {
            new("Z Raporu (Gün Sonu · X/Z)", "📊", "/BackOffice/Reports/XZ", Help: "Gün sonu X/Z kapanışı ve tahsilat özeti"),
            new("Fiş / Adisyon", "📑", "/BackOffice/Reports/Fiche", Feature.AdvancedReports),
            new("Z Raporları", "📅", "/BackOffice/Reports/ZList"),
            new("Masa Raporları", "🪑", "/BackOffice/Reports/Tables", Feature.AdvancedReports),
            new("Stok Raporları", "📦", "/BackOffice/Reports/Stock", Feature.AdvancedReports, Help: "Depo bazında stok giriş/çıkış ve bakiye raporları"),
            new("Stok Zekâsı", "🧠", "/BackOffice/Reports/StockIntelligence", Feature.AdvancedReports, Help: "Teorik/gerçek stok sapması ve fire analizi"),
            new("Menü Analizi", "📈", "/BackOffice/Reports/MenuEngineering", Feature.AdvancedReports, Help: "Ürün karlılık ve popülerlik (menü mühendisliği) analizi"),
            new("Cari Ekstre / Bakiye", "📄", "/BackOffice/Reports/CariBalance", Feature.PreAccounting),
            new("Gider Raporu", "📉", "/BackOffice/Reports/Expense", Feature.PreAccounting),
            new("Sipariş İzleme", "🧾", "/BackOffice/Orders"),
            new("KIOSK Kasa Paneli", "💰", "/BackOffice/KioskDashboard", Feature.SelfService),
            new("Puan / Sadakat Raporu", "⭐", "/BackOffice/LoyaltyReport"),
            new("Geri Bildirim", "⭐", "/BackOffice/Reports/Feedback"),
        }),
        new("Kullanıcı Yönetimi", "👤", new()
        {
            new("Kullanıcılar", "👤", "/BackOffice/Users"),
            new("Roller", "🛡", "/BackOffice/Roles"),
            // Self-service: her giriş yapmış kullanıcı KENDİ 2FA'sını yönetir → AdminOnly=false.
            new("Güvenlik / 2FA", "🔐", "/BackOffice/Security/TwoFactor", AdminOnly: false),
        }),
        new("Sistem", "⚙", new()
        {
            new("Sistem Tanımları", "⚙", "/BackOffice/SystemDefinitions"),
            new("Denetim İzi", "🕵", "/BackOffice/AuditTrail"),
            new("e-Fatura Ayarları", "📄", "/BackOffice/EInvoice"),
            new("Bildirim Ayarları", "🔔", "/BackOffice/Notifications"),
            new("Ekran Sürümü", "🖥", "/BackOffice/UiVersion"),
            new("Lisans", "🔐", "/BackOffice/License"),
            new("Sistem Yönetimi", "🛠", "/BackOffice/SystemManagement"),
            new("Kurulum Sihirbazı", "🧙", "/BackOffice/SetupWizard"),
            new("AI Hub", "🤖", "/BackOffice/AiHub", Feature.AiHub, Help: "Doğal dille rapor/sorgu ve AI sağlayıcı ayarları"),
        }),
        // Çok-kiracılı provisioning konsolu. YALNIZ SysAdmin görür (Visible içinde özel kapı) — Patron dahi göremez.
        new("Süper Admin", "🏛", new()
        {
            new("Pano", "📈", "/BackOffice/SysAdmin/Overview", Help: "Sistem geneli özet, lisans ve sağlık"),
            new("Şirketler", "🏢", "/BackOffice/SysAdmin/Tenants", Help: "Tüm kiracı şirketleri; aktif/pasif ve denetim"),
            new("Yeni Şirket", "➕", "/BackOffice/SysAdmin/NewTenant", Help: "Yeni kiracı şirket + veritabanı + ilk yönetici kurulumu"),
            new("Kullanıcılar", "👤", "/BackOffice/SysAdmin/Users", Help: "Tüm kiracıların kullanıcıları; rol, şirket erişimi, parola sıfırlama"),
            new("Roller", "🛡", "/BackOffice/SysAdmin/Roles", Help: "Sistem rolleri ve yetkileri"),
        }),
    };

    /// <summary>Oturumun lisans + rolüne göre görünür öğeler (boş bölümler gizlenir).</summary>
    public static IEnumerable<Section> Visible()
    {
        bool admin = Session.IsSysAdmin || Session.HasRole("Patron");
        foreach (var s in All)
        {
            // "Süper Admin" (çok-kiracılı provisioning) YALNIZ SysAdmin — Patron dahi görmez (çapraz-tenant güç).
            if (s.Title == "Süper Admin" && !Session.IsSysAdmin) continue;
            var items = s.Items.Where(i =>
                // AI Hub: admin/Patron yerine rol bazlı Permissions.Ai kapısı (yetkisiz kullanıcı nav'da görmesin — WPF paritesi).
                i.Feature == Feature.AiHub
                    ? (Permissions.Ai && Session.LicenseHas(Feature.AiHub))
                    : ((!i.AdminOnly || admin) &&
                       (i.Feature is null || Session.LicenseHas(i.Feature.Value)))).ToList();
            if (items.Count > 0) yield return s with { Items = items };
        }
    }
}
