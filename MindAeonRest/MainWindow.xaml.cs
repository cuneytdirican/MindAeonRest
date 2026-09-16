using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRest.Views;

namespace MindAeonRest;

public partial class MainWindow : Window
{
    private enum Page { Dashboard, Companies, Branches, Users, Roles, TaxGroups, Categories, Products, Warehouses, Customers, CustomerGroups, PriceLists, SalesStop, CodeSeries, Zones, Reyons, Banks, BankAccounts, Pos, CashRegisters, Kiosks, MealVouchers, MealVoucherAccounts, ResTags, Couriers, Projects, Personnel, ExpenseTypes, CariHareket }

    private readonly UserService _userService = new();
    private readonly RoleService _roleService = new();
    private readonly CompanyService _companyService = new();
    private readonly BranchService _branchService = new();
    private readonly TaxGroupService _taxService = new();
    private readonly ProductCategoryService _categoryService = new();
    private readonly ProductService _productService = new();
    private readonly WarehouseService _warehouseService = new();
    private readonly CustomerService _customerService = new();
    private readonly CustomerGroupService _customerGroupService = new();
    private readonly PriceListService _priceListService = new();
    private readonly ProductBranchSettingsService _branchSettingsService = new();
    private readonly CodeSeriesService _codeSeriesService = new();
    private readonly ZoneService _zoneService = new();
    private readonly ReyonService _reyonService = new();
    private readonly BankService _bankService = new();
    private readonly BankAccountService _bankAccountService = new();
    private readonly PosService _posService = new();
    private readonly CashRegisterService _cashRegisterService = new();
    private readonly KioskService _kioskService = new();
    private readonly MealVoucherService _mealVoucherService = new();
    private readonly MealVoucherAccountService _mealVoucherAccountService = new();
    private readonly ReservationTagService _resTagService = new();
    private readonly CourierService _courierService = new();
    private readonly ProjectService _projectService = new();
    private readonly PersonnelService _personnelService = new();
    private readonly ExpenseTypeService _expenseTypeService = new();
    private readonly CariHareketService _cariService = new();

    private Page _current = Page.Roles;
    private bool _themeReady;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private int CompanyId => Session.CurrentCompany?.Id ?? 0;
    private string CompanyName => Session.CurrentCompany?.Name ?? string.Empty;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var user = Session.CurrentUser;
        if (user is not null)
        {
            UserNameText.Text = user.FullName;
            UserRoleText.Text = user.RoleName;
            AvatarInitial.Text = string.IsNullOrWhiteSpace(user.FullName)
                ? "?" : user.FullName.Trim()[0].ToString().ToUpperInvariant();

            TopUserName.Text = user.FullName;
            TopUserRole.Text = user.RoleName;
            TopAvatarInitial.Text = AvatarInitial.Text;
        }

        // Tema seçici
        ThemeCombo.ItemsSource = ThemeManager.Options;
        ThemeCombo.SelectedValue = ThemeManager.Current;
        _themeReady = true;

        CompanyContextText.Text = $"{CompanyName} / {Session.CurrentBranch?.Name ?? "-"} · KIOSK #{Session.CurrentKioskNo}";

        ResetNavVisibility();             // rol/yetki bazlı görünürlük
        ApplyLicense();                   // LİSANS: pakette olmayan modül/menü/buton gizlenir (aramada da çıkmaz)
        BuildNavCatalog();

        // Açılış: admin → şube özeti (Dashboard); aksi halde ürünler
        if (Session.IsSysAdmin && Session.CurrentBranch is not null) NavDashboard.IsChecked = true;
        else NavProducts.IsChecked = true;

        // Login sonrası sessiz oto-kontrol (yalnız SysAdmin) — UI'yi bloklamaz
        if (Session.IsSysAdmin)
            _ = CheckForUpdatesOnStartupAsync();
    }

    /// <summary>Açılışta arka planda güncelleme kontrolü. Yeni sürüm varsa kullanıcıya sorar;
    /// onaylarsa indirip uygular ve uygulamayı kapatır. Ağ hatası/null → sessiz geçer (engelleme yok).</summary>
    private async System.Threading.Tasks.Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var info = await AppUpdater.CheckAsync();
            if (info is null || !info.HasUpdate) return;

            string notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : info.Notes + "\n";
            if (!Dialog.Confirm(
                    $"Yeni sürüm v{info.Latest} mevcut (şu an v{AppUpdater.CurrentText}).\n{notes}\nŞimdi güncellensin mi?",
                    "Uygulama Güncelleme"))
                return;

            var (ok, msg) = await AppUpdater.DownloadAndApplyAsync(info.Url);
            if (ok)
            {
                Dialog.Info(msg, "Uygulama Güncelleme");
                Application.Current.Shutdown();
            }
            else
            {
                Dialog.Warn(msg, "Uygulama Güncelleme");
            }
        }
        catch { /* sessiz geç: güncelleme açılışı engellemesin */ }
    }

    // ===== Global / menü araması =====
    private sealed class NavSearchItem
    {
        public string Icon { get; set; } = "";
        public string Label { get; set; } = "";
        public string Group { get; set; } = "";
        public Action Go { get; set; } = () => { };
    }

    private readonly List<NavSearchItem> _navCatalog = new();

    /// <summary>Rol/yetki bazlı görünürlük (lisans öncesi taban). Lisans yükseltince yeniden çağrılır.</summary>
    private void ResetNavVisibility()
    {
        bool isAdmin = Session.IsSysAdmin;
        var adminVis = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        GrpCompany.Visibility = GrpCompanyItems.Visibility = adminVis;
        GrpUsers.Visibility = GrpUsersItems.Visibility = adminVis;
        GrpLocation.Visibility = GrpLocationItems.Visibility = adminVis;
        GrpFinance.Visibility = GrpFinanceItems.Visibility = adminVis;
        GrpPaket.Visibility = GrpPaketItems.Visibility = adminVis;
        SysDefButton.Visibility = adminVis;
        SystemMgmtButton.Visibility = adminVis;
        EInvoiceButton.Visibility = adminVis;
        UiVersionButton.Visibility = adminVis;
        CsvImportButton.Visibility = adminVis;
        PriceTransferButton.Visibility = adminVis;
        BulkPriceButton.Visibility = adminVis;
        QuickProductButton.Visibility = adminVis;
        PriceMatrixButton.Visibility = adminVis;
        CampaignButton.Visibility = adminVis;

        static Visibility V(bool ok) => ok ? Visibility.Visible : Visibility.Collapsed;
        PosButton.Visibility = V(Permissions.Orders);
        PaketButton.Visibility = V(Permissions.Orders);
        KioskButton.Visibility = V(Permissions.Orders);
        KdsButton.Visibility = V(Permissions.Kitchen);
        KasaButton.Visibility = V(Permissions.Cash);
        RaporButton.Visibility = V(Permissions.Reports);
        ShiftCloseButton.Visibility = V(Permissions.Payment);
        IadeButton.Visibility = V(Permissions.Payment);
        NavDashboard.Visibility = V(Permissions.Reports);
        AiHubButton.Visibility = V(Permissions.Ai);

        // Lisansla daraltılan diğer gruplar/bölümler tabanda görünür (ApplyLicense tekrar kısacak)
        GrpReservation.Visibility = GrpReservationItems.Visibility = Visibility.Visible;
        GrpReports.Visibility = GrpReportsItems.Visibility = Visibility.Visible;
        GrpAccounting.Visibility = GrpAccountingItems.Visibility = Visibility.Visible;
        GrpOrders.Visibility = GrpOrdersItems.Visibility = Visibility.Visible;
        GrpWaybills.Visibility = GrpWaybillsItems.Visibility = Visibility.Visible;
        GrpDepot.Visibility = GrpDepotItems.Visibility = Visibility.Visible;
        GrpInvoices.Visibility = GrpInvoicesItems.Visibility = Visibility.Visible;
        NavProjects.Visibility = NavExpenseTypes.Visibility = Visibility.Visible;
        NavBanks.Visibility = NavBankAccounts.Visibility = NavPos.Visibility = Visibility.Visible;
        CashExtractButton.Visibility = Visibility.Visible;
        FinBankSection.Visibility = FinCardSection.Visibility = FinCheckSection.Visibility = FinVoucherSection.Visibility = Visibility.Visible;
        ReyonRouteButton.Visibility = Visibility.Visible;
    }

    /// <summary>LİSANS kısıtlaması: aktif paketin özelliğinde OLMAYAN modül/menü/buton gizlenir (nav + arama).</summary>
    private void ApplyLicense()
    {
        var lic = Session.CurrentLicense;
        if (lic is null) return;   // lisans bağlamı yoksa (güvenli) dokunma
        bool Has(MindAeonRest.Licensing.Feature f) => lic.Has(f);
        static Visibility V(bool ok) => ok ? Visibility.Visible : Visibility.Collapsed;
        void El(UIElement el, bool ok) => el.Visibility = V(ok);
        void Grp(UIElement header, UIElement items, bool ok) { var v = V(ok); header.Visibility = v; items.Visibility = v; }
        bool Vis(UIElement el) => el.Visibility == Visibility.Visible;

        // İşlem butonları (paket modülü) — yetki zaten uygulandı, üstüne lisans
        El(KdsButton, Vis(KdsButton) && Has(MindAeonRest.Licensing.Feature.Kitchen));
        El(KioskButton, Vis(KioskButton) && Has(MindAeonRest.Licensing.Feature.SelfService));
        El(PaketButton, Vis(PaketButton) && Has(MindAeonRest.Licensing.Feature.Delivery));
        El(AiHubButton, Vis(AiHubButton) && Has(MindAeonRest.Licensing.Feature.AiHub));
        El(CampaignButton, Vis(CampaignButton) && Has(MindAeonRest.Licensing.Feature.Campaign));
        El(ReyonRouteButton, Has(MindAeonRest.Licensing.Feature.Kitchen));

        // Paket modülü grupları
        Grp(GrpReservation, GrpReservationItems, Has(MindAeonRest.Licensing.Feature.Reservation));
        Grp(GrpPaket, GrpPaketItems, Has(MindAeonRest.Licensing.Feature.Delivery));
        Grp(GrpReports, GrpReportsItems, Has(MindAeonRest.Licensing.Feature.AdvancedReports));

        // ÖN MUHASEBE (yalnız Enterprise) — cari + evrak (sipariş/irsaliye/depo/fatura)
        bool pa = Has(MindAeonRest.Licensing.Feature.PreAccounting);
        Grp(GrpAccounting, GrpAccountingItems, pa);
        Grp(GrpOrders, GrpOrdersItems, pa);
        Grp(GrpWaybills, GrpWaybillsItems, pa);
        Grp(GrpDepot, GrpDepotItems, pa);
        Grp(GrpInvoices, GrpInvoicesItems, pa);
        El(NavProjects, pa);
        El(NavExpenseTypes, pa);

        // FİNANS: gelişmiş (banka/kart/çek/yemek çeki işlemleri + banka/pos tanımı + nakit ekstre) PA;
        // kasa giriş/çıkış + Nakit Kasalar + KIOSK'lar + Yemek Çek tanımları Lite/Standart'ta kalır.
        El(NavBanks, pa);
        El(NavBankAccounts, pa);
        El(NavPos, pa);
        El(CashExtractButton, pa);
        El(FinBankSection, pa);
        El(FinCardSection, pa);
        El(FinCheckSection, pa);
        El(FinVoucherSection, pa);
    }

    /// <summary>Lisans ekranı (durum/yükselt). Etkinleştirme sonrası nav+arama yeni pakete göre tazelenir.</summary>
    private void License_Click(object sender, RoutedEventArgs e)
    {
        var company = Session.CurrentCompany;
        if (company is null) return;
        var status = new LicenseService().GetStatus(company.Code, MindAeonRest.Licensing.HardwareId.Current);
        var w = new LicenseWindow(company.Code, company.Name, status) { Owner = this };
        w.ShowDialog();
        if (w.Status is not null)
        {
            Session.CurrentLicense = w.Status;
            ResetNavVisibility();
            ApplyLicense();
            BuildNavCatalog();
        }
    }

    private void MobileServer_Click(object sender, RoutedEventArgs e)
        => new MobileServerWindow { Owner = this }.ShowDialog();

    private void SystemManagement_Click(object sender, RoutedEventArgs e)
        => new SystemManagementWindow { Owner = this }.ShowDialog();

    private void BuildNavCatalog()
    {
        _navCatalog.Clear();
        // Sol navdaki tüm sayfalar (etiketleriyle) — rol/yetkisi olan kullanıcı erişebilsin.
        // LİSANS: gizlenen (Collapsed) sayfa aramada da ÇIKMAZ.
        foreach (var rb in FindRadios(NavRoot))
        {
            if (rb.Visibility != Visibility.Visible) continue;
            var rbb = rb;
            _navCatalog.Add(new NavSearchItem
            {
                Icon = rbb.Tag?.ToString() ?? "•",
                Label = rbb.Content?.ToString() ?? "",
                Group = "Yönetim",
                Go = () => { if (rbb.IsChecked == true) rbb.IsChecked = false; rbb.IsChecked = true; }
            });
        }
        // POS / işlem ekranları (yetkiye göre)
        void Add(string icon, string label, Func<bool> vis, Action go)
        { if (vis()) _navCatalog.Add(new NavSearchItem { Icon = icon, Label = label, Group = "İşlem", Go = go }); }

        // LİSANS: pakette olmayan işlem aramada da çıkmaz (Permissions + LicenseHas birlikte)
        bool Lic(MindAeonRest.Licensing.Feature f) => Session.LicenseHas(f);
        var KITCHEN = MindAeonRest.Licensing.Feature.Kitchen;
        var SELF = MindAeonRest.Licensing.Feature.SelfService;
        var DELIVERY = MindAeonRest.Licensing.Feature.Delivery;
        var PA = MindAeonRest.Licensing.Feature.PreAccounting;
        var ADV = MindAeonRest.Licensing.Feature.AdvancedReports;

        Add("🍽", "POS / Adisyon (Masalar)", () => Permissions.Orders, () => Pos_Click(this, new RoutedEventArgs()));
        Add("🍳", "Mutfak (KDS)", () => Permissions.Kitchen && Lic(KITCHEN), () => Kds_Click(this, new RoutedEventArgs()));
        Add("🖥", "KIOSK", () => Permissions.Orders && Lic(SELF), () => Kiosk_Click(this, new RoutedEventArgs()));
        Add("📦", "Paket / Take Away", () => Permissions.Orders && Lic(DELIVERY), () => Paket_Click(this, new RoutedEventArgs()));
        Add("💰", "Kasa", () => Permissions.Cash, () => Kasa_Click(this, new RoutedEventArgs()));
        Add("📊", "X / Z Rapor", () => Permissions.Reports, () => Rapor_Click(this, new RoutedEventArgs()));
        Add("🧾", "Vardiya Kapanışı", () => Permissions.Payment, () => ShiftClose_Click(this, new RoutedEventArgs()));
        Add("↩", "İade", () => Permissions.Payment, () => Iade_Click(this, new RoutedEventArgs()));
        Add("🧠", "AI Hub", () => Permissions.Ai && Lic(MindAeonRest.Licensing.Feature.AiHub), () => AiHub_Click(this, new RoutedEventArgs()));

        // Dekontlar + finans işlem/rapor ekranları — aramadan erişilebilsin (lisansa göre)
        bool admin = Session.IsSysAdmin;
        void AddF(string icon, string label, Action go) => Add(icon, label, () => admin, go);                      // her zaman (admin)
        void AddPA(string icon, string label, Action go) => Add(icon, label, () => admin && Lic(PA), go);          // ön muhasebe
        void AddR(string icon, string label, Action go) => Add(icon, label, () => admin && Lic(ADV), go);          // detaylı rapor
        AddF("⬆", "Kasa Giriş Dekontu", () => OpenDekont(DekontKind.KasaGiris));
        AddF("⬇", "Kasa Çıkış Dekontu", () => OpenDekont(DekontKind.KasaCikis));
        AddPA("⬆", "Banka Giriş Dekontu", () => OpenDekont(DekontKind.BankaGiris));
        AddPA("⬇", "Banka Çıkış Dekontu", () => OpenDekont(DekontKind.BankaCikis));
        AddPA("⬆", "Kredi Kartı Tahsilat Evrağı", () => OpenDekont(DekontKind.KartTahsilat));
        AddPA("⬇", "Kredi Kartı Ödeme Evrağı", () => OpenDekont(DekontKind.KartOdeme));
        AddPA("🏦", "Banka Hareketleri", () => Banka_Click(this, new RoutedEventArgs()));
        AddPA("💳", "POS / Kredi Kartı Hareketleri", () => PosHareket_Click(this, new RoutedEventArgs()));
        AddPA("🧾", "Çek / Senet Portföyü", () => CekSenet_Click(this, new RoutedEventArgs()));
        AddPA("📆", "Çek / Senet Vade Takibi", () => CekVade_Click(this, new RoutedEventArgs()));
        AddPA("⏰", "Vadesi Yaklaşan Çek/Senet", () => CekVadeYakin_Click(this, new RoutedEventArgs()));
        AddPA("📒", "Nakit Kasa Ekstresi", () => CashExtract_Click(this, new RoutedEventArgs()));
        AddPA("📒", "Banka Hesap Ekstresi", () => BankExtract_Click(this, new RoutedEventArgs()));
        AddPA("📒", "POS Hareket Ekstresi", () => PosExtract_Click(this, new RoutedEventArgs()));
        AddPA("📒", "Yemek Çeki Ekstresi", () => VoucherExtract_Click(this, new RoutedEventArgs()));
        AddPA("🎫", "Yemek Çeki Çıkışı (Satış Faturası)", () => YemekCekiCikis_Click(this, new RoutedEventArgs()));
        Add("🎁", "Hediye Çekleri (sat / iptal / ekstre)", () => Permissions.Cash, () => GiftCard_Click(this, new RoutedEventArgs()));
        Add("⭐", "Sadakat Ayarları (puan oranları)", () => admin || Permissions.Cash, () => LoyaltySettings_Click(this, new RoutedEventArgs()));
        Add("⭐", "Puan / Sadakat Raporu (yükümlülük / bakiye / ekstre)", () => admin || Permissions.Cash, () => LoyaltyReport_Click(this, new RoutedEventArgs()));
        Add("💳", "Ödeme Yöntemi Kırılımı (tahsilat raporu)", () => Permissions.Cash, () => PaymentBreakdown_Click(this, new RoutedEventArgs()));
        AddPA("📄", "Cari Hesap Ekstresi", () => CariEkstre_Click(this, new RoutedEventArgs()));
        AddR("📑", "Fiş / Adisyon Raporu", () => FicheReport_Click(this, new RoutedEventArgs()));
        AddR("📅", "Z Raporları Listesi", () => ZReportList_Click(this, new RoutedEventArgs()));
        AddR("🪑", "Masa Raporları (anlık + geçmiş)", () => MasaRapor_Click(this, new RoutedEventArgs()));
        AddR("💰", "KIOSK Kasa Paneli", () => KioskDashboard_Click(this, new RoutedEventArgs()));
        AddF("🚀", "Kurulum Sihirbazı (adım adım hızlı kurulum)", () => new RestaurantWizardWindow { Owner = this }.ShowDialog());
        AddF("📋", "Restoran Tanımları (tüm tanımlar)", () => OpenSetupWizard());
        AddF("🔐", "Lisans (paket / yükselt / talep anahtarı)", () => License_Click(this, new RoutedEventArgs()));
        AddF("📱", "Mobil Sunucu (adres + QR + başlat/durdur)", () => MobileServer_Click(this, new RoutedEventArgs()));
        Add("🔀", "Reyon Yönlendirme (Mutfak/Bar)", () => admin && Lic(KITCHEN), () => ReyonRoute_Click(this, new RoutedEventArgs()));
        AddPA("📄", "Satış Faturaları", () => SalesInvoice_Click(this, new RoutedEventArgs()));
        AddPA("📄", "Alış Faturaları", () => PurchaseInvoice_Click(this, new RoutedEventArgs()));
    }

    private static IEnumerable<RadioButton> FindRadios(DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is RadioButton rb && rb.GroupName == "Nav") yield return rb;
            if (child is DependencyObject d)
                foreach (var sub in FindRadios(d)) yield return sub;
        }
    }

    // Türkçe I/ı/İ/i ve aksanları katlayan arama normalizasyonu ("AI"→"ai", "İÇECEK"→"icecek")
    internal static string SearchNorm(string s) =>
        s.ToLowerInvariant().Replace("i̇", "i").Replace('İ', 'i').Replace('ı', 'i').Replace('î', 'i');

    private void NavSearch_Changed(object sender, TextChangedEventArgs e)
    {
        string q = NavSearchBox.Text.Trim();
        if (q.Length == 0) { NavSearchPopup.IsOpen = false; return; }
        var tokens = SearchNorm(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _navCatalog
            .Where(i => tokens.All(t => SearchNorm(i.Label).Contains(t)))
            .Take(40).ToList();
        NavSearchList.ItemsSource = matches;
        if (matches.Count > 0) NavSearchList.SelectedIndex = 0;
        NavSearchPopup.IsOpen = matches.Count > 0;
    }

    private void NavSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ExecuteNavPick(); e.Handled = true; }
        else if (e.Key == Key.Down && NavSearchPopup.IsOpen) { NavSearchList.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) NavSearchPopup.IsOpen = false;
    }

    private void NavSearchList_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ExecuteNavPick(); e.Handled = true; } }
    private void NavSearch_Pick(object sender, MouseButtonEventArgs e) => ExecuteNavPick();

    private void ExecuteNavPick()
    {
        if (NavSearchList.SelectedItem is not NavSearchItem it) return;
        NavSearchPopup.IsOpen = false;
        NavSearchBox.Text = "";
        try { it.Go(); } catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void NavDashboard_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Dashboard);
    private void NavCompanies_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Companies);
    private void NavBranches_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Branches);
    private void NavUsers_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Users);
    private void NavRoles_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Roles);
    private void NavTax_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.TaxGroups);
    private void NavCategories_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Categories);
    private void NavProducts_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Products);
    private void NavWarehouses_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Warehouses);
    private void NavCustomers_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Customers);
    private void NavCustomerGroups_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.CustomerGroups);
    private void NavPriceLists_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.PriceLists);
    private void NavSalesStop_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.SalesStop);
    private void NavCodeSeries_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.CodeSeries);
    private void NavZones_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Zones);
    private void NavReyons_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Reyons);
    private void ReyonRoute_Click(object sender, RoutedEventArgs e) => new ReyonRouteMatrixWindow { Owner = this }.ShowDialog();

    private void SetupWizard_Click(object sender, RoutedEventArgs e) => OpenSetupWizard();
    private void RestaurantWizard_Click(object sender, RoutedEventArgs e) => new RestaurantWizardWindow { Owner = this }.ShowDialog();

    /// <summary>Sistemi sıfırdan kurmak için tüm tanımları iş sırasına göre tek ekranda toplayan sihirbaz.</summary>
    private void OpenSetupWizard()
    {
        int companyId = Session.CurrentCompany?.Id ?? 0;
        var steps = new List<SetupStepVM>();
        int n = 0;
        void Add(string icon, string title, string desc, Func<int> count, string unit, Action open) =>
            steps.Add(new SetupStepVM
            {
                No = ++n, Icon = icon, Title = title, Desc = desc,
                StatusFunc = () => { int c = count(); return (c, c > 0 ? $"{c} {unit} ✓" : $"henüz yok"); },
                OpenAction = () => { open(); Activate(); }
            });
        void Nav(System.Windows.Controls.RadioButton rb) { rb.IsChecked = false; rb.IsChecked = true; }
        int SafeCount(Func<int> f) { try { return f(); } catch { return 0; } }

        Add("🏢", "Şirket Bilgileri", "Şirket adı, logo, vergi bilgileri. (Sistem zaten bir şirketle açılır.)",
            () => SafeCount(() => new CompanyService().GetAll().Count), "şirket", () => Nav(NavCompanies));
        Add("🏪", "Şubeler", "Restoranın şubelerini tanımlayın. Her şubenin kendi lokasyon/kasa/POS'u olur.",
            () => SafeCount(() => new BranchService().GetByCompany(companyId).Count), "şube", () => Nav(NavBranches));
        Add("📍", "Lokasyonlar", "Her şubede Salon, Bahçe, Bar gibi lokasyonlar (zone).",
            () => SafeCount(() => new ZoneService().GetAll().Count), "lokasyon", () => Nav(NavZones));
        Add("🍳", "Reyonlar (Mutfak/Bar)", "Her lokasyona mutfak/bar reyonu + yazıcısı. Mutfak fişi reyonun yazıcısından çıkar.",
            () => SafeCount(() => new ReyonService().GetAll().Count), "reyon", () => Nav(NavReyons));
        Add("🔀", "Reyon Yönlendirme", "Lokasyon + Kategori → hangi reyona düşsün? (Beach'ten bira → Beach Bar gibi.)",
            () => SafeCount(() => new ReyonRouteService().GetAll().Count), "kural", () => ReyonRoute_Click(this, new RoutedEventArgs()));
        Add("🪑", "Masa Planı", "Lokasyonlara masaları yerleştirin (sürükle-bırak plan).",
            () => SafeCount(() => new ZoneService().GetAll().Sum(z => new TableService().GetByZone(z.Id).Count)), "masa", () => Layout_Click(this, new RoutedEventArgs()));
        Add("％", "KDV Grupları", "Satış/alış KDV oranları. Ürün fiyatlarında kullanılır.",
            () => SafeCount(() => new TaxGroupService().GetAll().Count), "KDV grubu", () => Nav(NavTax));
        Add("📂", "Kategoriler", "Ürün kategorileri (İçecek, Yemek, Tatlı…). Reyon yönlendirme bunlara göre çalışır.",
            () => SafeCount(() => new ProductCategoryService().GetAll().Count), "kategori", () => Nav(NavCategories));
        Add("🏬", "Depolar", "Stok depoları (şube bazlı). Ürün stokları burada tutulur.",
            () => SafeCount(() => new WarehouseService().GetAll().Count), "depo", () => Nav(NavWarehouses));
        Add("🍔", "Ürünler & Fiyatlar", "Ürünleri, birim/KDV/fiyatlarını tanımlayın. (Birim/KDV/kategori '+' ile hızlı eklenir.)",
            () => SafeCount(() => new ProductService().GetAll().Count), "ürün", () => Nav(NavProducts));
        Add("🏦", "Bankalar & Hesaplar", "Banka ve banka hesapları. Tahsilat/ödeme dekontlarında kullanılır.",
            () => SafeCount(() => new BankAccountService().GetAll().Count), "banka hesabı", () => Nav(NavBankAccounts));
        Add("💵", "Kasalar", "Nakit kasalar (şube/KIOSK kapsamlı). Ödeme ve dekontlarda kullanılır.",
            () => SafeCount(() => new CashRegisterService().GetAll().Count), "kasa", () => Nav(NavCashRegisters));
        Add("💳", "Kredi Kartı POS", "POS cihazları. Kart tahsilatlarında kullanılır.",
            () => SafeCount(() => new PosService().GetAll().Count), "POS", () => Nav(NavPos));
        Add("🎫", "Yemek Çek Hesapları", "Yemek çeki firmaları/hesapları (Multinet, Sodexo…).",
            () => SafeCount(() => new MealVoucherAccountService().GetAll().Count), "hesap", () => Nav(NavMealVoucherAccounts));
        Add("🖥", "KIOSK'lar (+ Fiş Yazıcısı)", "Her terminalin KIOSK tanımı + hesap/fiş yazıcısı. (KIOSKNO appsettings.json'dan gelir.)",
            () => SafeCount(() => new KioskService().GetAll().Count), "KIOSK", () => Nav(NavKiosks));
        Add("🛡", "Roller", "Yetki rolleri (SysAdmin, Garson, Kasiyer, Mutfak, İade, Z_Rapor…).",
            () => SafeCount(() => new RoleService().GetAll().Count), "rol", () => Nav(NavRoles));
        Add("👤", "Kullanıcılar", "Personel girişleri + rolleri. Çoklu rol verilebilir.",
            () => SafeCount(() => new UserService().GetAll().Count), "kullanıcı", () => Nav(NavUsers));
        Add("🧾", "Cariler (Müşteriler)", "Müşteri/tedarikçi cari kartları. Adisyon/fatura/dekontlarda kullanılır.",
            () => SafeCount(() => new CustomerService().GetAll().Count), "cari", () => Nav(NavCustomers));

        new SetupWizardWindow(steps) { Owner = this }.Show();
    }
    private void NavBanks_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Banks);
    private void NavKiosks_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Kiosks);
    private void NavBankAccounts_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.BankAccounts);
    private void NavPos_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Pos);
    private void NavCashRegisters_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.CashRegisters);
    private void NavMealVouchers_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.MealVouchers);
    private void NavMealVoucherAccounts_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.MealVoucherAccounts);
    private void NavResTags_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.ResTags);
    private void NavCouriers_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Couriers);
    private void NavProjects_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Projects);
    private void NavPersonnel_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.Personnel);
    private void NavExpenseTypes_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.ExpenseTypes);
    private void NavCariHareket_Checked(object sender, RoutedEventArgs e) => ShowPage(Page.CariHareket);
    private void CariEkstre_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Cari Hesap Ekstresi", ReportFilterOptions.Customer | ReportFilterOptions.DateRange, ReportBuilders.CariEkstre) { Owner = this }.ShowDialog();
    private void CariBakiye_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Cari Bakiye Listesi", ReportFilterOptions.DateRange, ReportBuilders.CariBakiye) { Owner = this }.ShowDialog();
    private void OpenDocList(DocType t) { var w = new DocumentListWindow(t) { Owner = this }; w.ShowDialog(); }
    private void SalesOrder_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.SatisSiparis);
    private void PurchaseOrder_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.AlisSiparis);
    private void SalesWaybill_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.SatisIrsaliye);
    private void PurchaseWaybill_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.AlisIrsaliye);
    private void Unbilled_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Faturalanmamış İrsaliyeler", ReportFilterOptions.DateRange, ReportBuilders.FaturalanmamisIrsaliye) { Owner = this }.ShowDialog();
    private void Transfer_Click2(object sender, RoutedEventArgs e) => OpenDocList(DocType.DepoTransfer);
    private void Count_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.Sayim);
    private void Fire_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.Fire);
    private void Gider_Click(object sender, RoutedEventArgs e) { var w = new GiderListWindow { Owner = this }; w.ShowDialog(); }
    private void OrderHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null) { Dialog.Info("Sipariş izleme için aktif şube gerekir.", "Uyarı"); return; }
        new OrderListWindow { Owner = this }.ShowDialog();
    }
    private void StockQty_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Stok Miktar Raporu", ReportFilterOptions.Branch | ReportFilterOptions.Warehouse | ReportFilterOptions.Search, ReportBuilders.StokMiktar) { Owner = this }.ShowDialog();
    private void StockMove_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Stok Hareket Raporu", ReportFilterOptions.DateRange | ReportFilterOptions.Warehouse | ReportFilterOptions.Search, ReportBuilders.StokHareket) { Owner = this }.ShowDialog();
    private void ExpenseReport_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Gider Raporu", ReportFilterOptions.DateRange, ReportBuilders.Giderler) { Owner = this }.ShowDialog();
    private void FicheReport_Click(object sender, RoutedEventArgs e)
        => new FicheReportWindow { Owner = this }.ShowDialog();
    private void ZReportList_Click(object sender, RoutedEventArgs e)
        => new ZReportListWindow { Owner = this }.ShowDialog();
    private void MasaRapor_Click(object sender, RoutedEventArgs e)
        => new MasaRaporWindow { Owner = this }.ShowDialog();
    private void KioskDashboard_Click(object sender, RoutedEventArgs e)
        => new KioskDashboardWindow { Owner = this }.ShowDialog();
    private void SalesInvoice_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.SatisFatura);
    private void PurchaseInvoice_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.AlisFatura);
    private void SalesReturn_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.SatisIadeFatura);
    private void PurchaseReturn_Click(object sender, RoutedEventArgs e) => OpenDocList(DocType.AlisIadeFatura);
    private void Banka_Click(object sender, RoutedEventArgs e) { var w = new BankaWindow { Owner = this }; w.ShowDialog(); }

    // Para giriş/çıkış dekontları (Kasa/Banka/Kart) — cari seçilirse cari hareketi de oluşur
    private void OpenDekont(DekontKind k)
    {
        if (!Permissions.Cash) { Dialog.Warn("Dekont işlemi için yetkiniz yok."); return; }
        if (Session.CurrentBranch is null) { Dialog.Info("Dekont için aktif bir şube gerekir.", "Uyarı"); return; }
        new DekontWindow(k) { Owner = this }.ShowDialog();
    }
    private void KasaGirisDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.KasaGiris);
    private void KasaCikisDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.KasaCikis);
    private void BankaGirisDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.BankaGiris);
    private void BankaCikisDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.BankaCikis);
    private void KartTahsilatDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.KartTahsilat);
    private void KartOdemeDekont_Click(object sender, RoutedEventArgs e) => OpenDekont(DekontKind.KartOdeme);
    private void BankaBakiye_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Banka Hesap Bakiyeleri", ReportFilterOptions.None, ReportBuilders.BankaBakiye) { Owner = this }.ShowDialog();
    private void PosHareket_Click(object sender, RoutedEventArgs e) { var w = new PosWindow { Owner = this }; w.ShowDialog(); }
    private void CekSenet_Click(object sender, RoutedEventArgs e) { var w = new CekSenetWindow { Owner = this }; w.ShowDialog(); }
    private void CekVade_Click(object sender, RoutedEventArgs e) => new CekVadeWindow { Owner = this }.ShowDialog();
    private void YemekCekiCikis_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Cash) { Dialog.Warn("Bu işlem için yetkiniz yok."); return; }
        if (Session.CurrentBranch is null) { Dialog.Info("Aktif bir şube gerekir.", "Uyarı"); return; }
        new YemekCekiCikisWindow { Owner = this }.ShowDialog();
    }
    private void CekVadeYakin_Click(object sender, RoutedEventArgs e)
    {
        var w = new CekVadeWindow { Owner = this };
        w.Loaded += (_, _) => w.FocusUpcoming();   // "7 gün içinde" filtresine ayarla
        w.ShowDialog();
    }
    private void GiftCard_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Cash) { Dialog.Warn("Hediye çeki işlemleri için yetkiniz yok."); return; }
        if (Session.CurrentBranch is null) { Dialog.Info("Hediye çeki için aktif bir şube gerekir.", "Uyarı"); return; }
        new GiftCardWindow { Owner = this }.ShowDialog();
    }
    private void LoyaltySettings_Click(object sender, RoutedEventArgs e)
    {
        if (!Session.IsSysAdmin && !Permissions.Cash) { Dialog.Warn("Sadakat ayarları için yetkiniz yok."); return; }
        new LoyaltySettingsWindow { Owner = this }.ShowDialog();
    }
    private void LoyaltyReport_Click(object sender, RoutedEventArgs e)
    {
        if (!Session.IsSysAdmin && !Permissions.Cash) { Dialog.Warn("Sadakat raporu için yetkiniz yok."); return; }
        new LoyaltyReportWindow { Owner = this }.ShowDialog();
    }
    private void PaymentBreakdown_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Cash) { Dialog.Warn("Ödeme kırılımı raporu için yetkiniz yok."); return; }
        new PaymentBreakdownWindow { Owner = this }.ShowDialog();
    }
    private void PosBakiye_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("POS / Kredi Kartı Bakiyeleri", ReportFilterOptions.None, ReportBuilders.PosBakiye) { Owner = this }.ShowDialog();
    private void CekSenetRapor_Click(object sender, RoutedEventArgs e)
        => new ReportWindow("Çek / Senet Portföyü", ReportFilterOptions.None, ReportBuilders.CekSenetPortfoy) { Owner = this }.ShowDialog();
    private void CashExtract_Click(object sender, RoutedEventArgs e) => new ExtractWindow(0) { Owner = this }.ShowDialog();
    private void PosExtract_Click(object sender, RoutedEventArgs e) => new ExtractWindow(1) { Owner = this }.ShowDialog();
    private void BankExtract_Click(object sender, RoutedEventArgs e) => new ExtractWindow(2) { Owner = this }.ShowDialog();
    private void VoucherExtract_Click(object sender, RoutedEventArgs e) => new ExtractWindow(3) { Owner = this }.ShowDialog();
    private void Paket_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null) { Dialog.Info("Paket için aktif bir şube gerekir.", "Uyarı"); return; }
        new PaketWindow { Owner = this }.ShowDialog();
    }

    private void DeliverySettings_Click(object sender, RoutedEventArgs e)
    {
        new DeliverySettingsWindow { Owner = this }.ShowDialog();
    }

    private void Iade_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null) { Dialog.Info("İade için aktif bir şube gerekir.", "Uyarı"); return; }
        new IadeWindow { Owner = this }.ShowDialog();
    }
    private void Reservation_Click(object sender, RoutedEventArgs e) => new ReservationWindow { Owner = this }.ShowDialog();

    private void ShowPage(Page page)
    {
        if (!IsLoaded) return;
        _current = page;

        DashboardPanel.Visibility = Visibility.Collapsed;
        CompaniesGrid.Visibility = Visibility.Collapsed;
        BranchesGrid.Visibility = Visibility.Collapsed;
        UsersGrid.Visibility = Visibility.Collapsed;
        RolesGrid.Visibility = Visibility.Collapsed;
        TaxGroupsGrid.Visibility = Visibility.Collapsed;
        CategoriesGrid.Visibility = Visibility.Collapsed;
        ProductsGrid.Visibility = Visibility.Collapsed;
        WarehousesGrid.Visibility = Visibility.Collapsed;
        CustomersGrid.Visibility = Visibility.Collapsed;
        CustomerGroupsGrid.Visibility = Visibility.Collapsed;
        PriceListsGrid.Visibility = Visibility.Collapsed;
        SalesStopGrid.Visibility = Visibility.Collapsed;
        CodeSeriesGrid.Visibility = Visibility.Collapsed;
        ZonesGrid.Visibility = Visibility.Collapsed;
        ReyonsGrid.Visibility = Visibility.Collapsed;
        BanksGrid.Visibility = Visibility.Collapsed;
        BankAccountsGrid.Visibility = Visibility.Collapsed;
        PosGrid.Visibility = Visibility.Collapsed;
        CashRegistersGrid.Visibility = Visibility.Collapsed;
        KiosksGrid.Visibility = Visibility.Collapsed;
        MealVouchersGrid.Visibility = Visibility.Collapsed;
        MealVoucherAccountsGrid.Visibility = Visibility.Collapsed;
        ResTagsGrid.Visibility = Visibility.Collapsed;
        CouriersGrid.Visibility = Visibility.Collapsed;
        ProjectsGrid.Visibility = Visibility.Collapsed;
        PersonnelGrid.Visibility = Visibility.Collapsed;
        ExpenseTypesGrid.Visibility = Visibility.Collapsed;
        CariHareketGrid.Visibility = Visibility.Collapsed;
        ActionButton.Visibility = Visibility.Collapsed;
        ActionButton2.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = (Session.IsSysAdmin && PageTable() is not null) ? Visibility.Visible : Visibility.Collapsed;
        // Liste arama + dışa aktarma yalnızca grid sayfalarında
        bool hasGrid = CurrentGrid() is not null;
        ListSearchBox.Visibility = ExportButton.Visibility = hasGrid ? Visibility.Visible : Visibility.Collapsed;
        if (hasGrid) ListSearchBox.Text = "";

        switch (page)
        {
            case Page.Dashboard:
                PageTitle.Text = "Özet";
                PageSubtitle.Text = $"{Session.CurrentBranch?.Name ?? CompanyName} · bugünün özeti";
                DashboardPanel.Visibility = Visibility.Visible;
                LoadDashboard();
                break;

            case Page.Companies:
                PageTitle.Text = "Şirket Bilgileri";
                PageSubtitle.Text = "Giriş yapılan şirketin tanım bilgileri";
                CompaniesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "Şirketi Düzenle";
                ActionButton.Visibility = Visibility.Visible;
                LoadCompany();
                break;

            case Page.Branches:
                PageTitle.Text = "Şubeler";
                PageSubtitle.Text = $"{CompanyName} şubeleri";
                BranchesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Şube";
                ActionButton.Visibility = Visibility.Visible;
                LoadBranches();
                break;

            case Page.Users:
                PageTitle.Text = "Kullanıcı Yönetimi";
                PageSubtitle.Text = $"Tüm kullanıcılar · {CompanyName} şube erişimleri";
                UsersGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Kullanıcı";
                ActionButton.Visibility = Visibility.Visible;
                LoadUsers();
                break;

            case Page.Roles:
                PageTitle.Text = "Rol Yönetimi";
                PageSubtitle.Text = "Sistemde tanımlı roller · seçili rol için AI yetkisini değiştirebilirsiniz";
                RolesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "🧠 AI Yetkisi Aç/Kapa";
                ActionButton.Visibility = Visibility.Visible;
                LoadRoles();
                break;

            case Page.TaxGroups:
                PageTitle.Text = "Vergi Grupları";
                PageSubtitle.Text = $"{CompanyName} · KDV oran grupları";
                TaxGroupsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Vergi Grubu";
                ActionButton.Visibility = Visibility.Visible;
                LoadTaxGroups();
                break;

            case Page.Categories:
                PageTitle.Text = "Kategoriler";
                PageSubtitle.Text = $"{CompanyName} · ürün kategorileri · satırı seçip Düzenle/Kaldır";
                CategoriesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Kategori";
                ActionButton.Visibility = Visibility.Visible;
                ActionButton2.Content = "✏ Düzenle";
                ActionButton2.Visibility = Visibility.Visible;
                LoadCategories();
                break;

            case Page.Products:
                PageTitle.Text = "Ürünler";
                PageSubtitle.Text = $"{CompanyName} · stok kartları · satırı seçip Düzenle/Kaldır";
                ProductsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Ürün";
                ActionButton.Visibility = Visibility.Visible;
                ActionButton2.Content = "✏ Düzenle";
                ActionButton2.Visibility = Visibility.Visible;
                LoadProducts();
                break;

            case Page.Warehouses:
                PageTitle.Text = "Depolar";
                PageSubtitle.Text = $"{CompanyName} · depolar (firma geneli, şubeye bağlı)";
                WarehousesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Depo";
                ActionButton.Visibility = Visibility.Visible;
                LoadWarehouses();
                break;

            case Page.Customers:
                PageTitle.Text = "Müşteriler";
                PageSubtitle.Text = $"{CompanyName} · cariler";
                CustomersGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Müşteri";
                ActionButton.Visibility = Visibility.Visible;
                LoadCustomers();
                break;

            case Page.CustomerGroups:
                PageTitle.Text = "Müşteri Grupları";
                PageSubtitle.Text = $"{CompanyName} · cari grupları";
                CustomerGroupsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Grup";
                ActionButton.Visibility = Visibility.Visible;
                LoadCustomerGroups();
                break;

            case Page.PriceLists:
                PageTitle.Text = "Fiyat Listeleri";
                PageSubtitle.Text = $"{CompanyName} · fiyat listeleri";
                PriceListsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Liste";
                ActionButton.Visibility = Visibility.Visible;
                LoadPriceLists();
                break;

            case Page.Zones:
                PageTitle.Text = "Lokasyonlar";
                PageSubtitle.Text = $"{CompanyName} · şube bazlı lokasyonlar (zone)";
                ZonesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Lokasyon";
                ActionButton.Visibility = Visibility.Visible;
                LoadZones();
                break;

            case Page.Reyons:
                PageTitle.Text = "Reyonlar (Mutfak)";
                PageSubtitle.Text = $"{CompanyName} · lokasyon altı reyonlar + yazıcı";
                ReyonsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Reyon";
                ActionButton.Visibility = Visibility.Visible;
                LoadReyons();
                break;

            case Page.Banks:
                PageTitle.Text = "Bankalar";
                PageSubtitle.Text = $"{CompanyName} · banka tanımları";
                BanksGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Banka";
                ActionButton.Visibility = Visibility.Visible;
                LoadBanks();
                break;

            case Page.BankAccounts:
                PageTitle.Text = "Banka Hesapları";
                PageSubtitle.Text = $"{CompanyName} · banka hesapları (döviz zorunlu)";
                BankAccountsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Hesap";
                ActionButton.Visibility = Visibility.Visible;
                LoadBankAccounts();
                break;

            case Page.Pos:
                PageTitle.Text = "Kredi Kartı POS";
                PageSubtitle.Text = $"{CompanyName} · şube bazlı POS cihazları";
                PosGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni POS";
                ActionButton.Visibility = Visibility.Visible;
                LoadPos();
                break;

            case Page.CashRegisters:
                PageTitle.Text = "Nakit Kasalar";
                PageSubtitle.Text = $"{CompanyName} · şube bazlı nakit kasalar";
                CashRegistersGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Kasa";
                ActionButton.Visibility = Visibility.Visible;
                LoadCashRegisters();
                break;
            case Page.Kiosks:
                PageTitle.Text = "KIOSK'lar";
                PageSubtitle.Text = $"{CompanyName} · şube bazlı self-servis cihazlar";
                KiosksGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni KIOSK";
                ActionButton.Visibility = Visibility.Visible;
                LoadKiosks();
                break;

            case Page.MealVouchers:
                PageTitle.Text = "Yemek Çekleri";
                PageSubtitle.Text = $"{CompanyName} · yemek çeki firmaları";
                MealVouchersGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Yemek Çeki";
                ActionButton.Visibility = Visibility.Visible;
                LoadMealVouchers();
                break;

            case Page.MealVoucherAccounts:
                PageTitle.Text = "Yemek Çek Hesapları";
                PageSubtitle.Text = $"{CompanyName} · şube bazlı yemek çek hesapları";
                MealVoucherAccountsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Hesap";
                ActionButton.Visibility = Visibility.Visible;
                LoadMealVoucherAccounts();
                break;

            case Page.ResTags:
                PageTitle.Text = "Rezervasyon Etiketleri";
                PageSubtitle.Text = $"{CompanyName} · Doğum Günü, İş Yemeği gibi renkli etiketler";
                ResTagsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Etiket";
                ActionButton.Visibility = Visibility.Visible;
                LoadResTags();
                break;

            case Page.Couriers:
                PageTitle.Text = "Kuryeler";
                PageSubtitle.Text = $"{CompanyName} · paket teslimat kuryeleri";
                CouriersGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Kurye";
                ActionButton.Visibility = Visibility.Visible;
                LoadCouriers();
                break;

            case Page.Projects:
                PageTitle.Text = "Projeler";
                PageSubtitle.Text = $"{CompanyName} · proje kodları (evraklarda zorunlu)";
                ProjectsGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Proje";
                ActionButton.Visibility = Visibility.Visible;
                LoadProjects();
                break;

            case Page.Personnel:
                PageTitle.Text = "Personel";
                PageSubtitle.Text = $"{CompanyName} · personel kartları ve izinleri";
                PersonnelGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Personel";
                ActionButton.Visibility = Visibility.Visible;
                LoadPersonnel();
                break;

            case Page.ExpenseTypes:
                PageTitle.Text = "Gider Tipleri";
                PageSubtitle.Text = $"{CompanyName} · gider kategorileri";
                ExpenseTypesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni Gider Tipi";
                ActionButton.Visibility = Visibility.Visible;
                LoadExpenseTypes();
                break;

            case Page.CariHareket:
                PageTitle.Text = "Cari Hareketler";
                PageSubtitle.Text = $"{CompanyName} · son cari hesap hareketleri (borç/alacak)";
                CariHareketGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Manuel Hareket";
                ActionButton.Visibility = Visibility.Visible;
                LoadCariHareket();
                break;

            case Page.CodeSeries:
                PageTitle.Text = "Kod Serileri";
                PageSubtitle.Text = $"{CompanyName} · stok/müşteri kodu otomatik seri";
                CodeSeriesGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "+ Yeni / Düzenle";
                ActionButton.Visibility = Visibility.Visible;
                LoadCodeSeries();
                break;

            case Page.SalesStop:
                PageTitle.Text = "Satış Durdurma";
                PageSubtitle.Text = $"Şube: {Session.CurrentBranch?.Name ?? "-"} · ürün seçip satışı durdurun/açın";
                SalesStopGrid.Visibility = Visibility.Visible;
                ActionButton.Content = "Satışı Durdur";
                ActionButton.Visibility = Visibility.Visible;
                ActionButton2.Content = "Satışa Aç";
                ActionButton2.Visibility = Visibility.Visible;
                LoadSalesStop();
                break;
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_current)
        {
            case Page.Companies: EditCompany(); break;
            case Page.Branches: EditBranch(null); break;
            case Page.Users: EditUser(null); break;
            case Page.TaxGroups: EditTaxGroup(null); break;
            case Page.Categories: EditCategory(null); break;
            case Page.Products: EditProduct(null); break;
            case Page.Warehouses: EditWarehouse(null); break;
            case Page.Customers: EditCustomer(null); break;
            case Page.CustomerGroups: AddCustomerGroup(); break;
            case Page.PriceLists: AddPriceList(); break;
            case Page.SalesStop: SetSalesStopped(true); break;
            case Page.CodeSeries: EditCodeSeries(null); break;
            case Page.Zones: EditZone(null); break;
            case Page.Reyons: EditReyon(null); break;
            case Page.Banks: EditBank(null); break;
            case Page.BankAccounts: EditBankAccount(null); break;
            case Page.Pos: EditPos(null); break;
            case Page.CashRegisters: EditCashRegister(null); break;
            case Page.Kiosks: EditKiosk(null); break;
            case Page.MealVouchers: EditMealVoucher(null); break;
            case Page.MealVoucherAccounts: EditMealVoucherAccount(null); break;
            case Page.ResTags: EditResTag(null); break;
            case Page.Couriers: EditCourier(null); break;
            case Page.Projects: EditProject(null); break;
            case Page.Personnel: EditPersonnel(null); break;
            case Page.ExpenseTypes: EditExpenseType(null); break;
            case Page.CariHareket: AddCariHareket(); break;
            case Page.Roles: ToggleRoleAi(); break;
        }
    }

    private void AiHub_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Ai) { Dialog.Warn("AI Hub için yetkiniz yok."); return; }
        new Views.AiHubWindow { Owner = this }.ShowDialog();
    }

    private void ToggleRoleAi()
    {
        if (RolesGrid.SelectedItem is not Role role) { Dialog.Info("Önce bir rol seçin.", "Bilgi"); return; }
        try
        {
            _roleService.SetAiPermission(role.Id, !role.CanUseAi, Session.CurrentUserId);
            Dialog.Success($"{role.Name} rolü AI yetkisi: {(!role.CanUseAi ? "AÇIK" : "KAPALI")}");
            LoadRoles();
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void ActionButton2_Click(object sender, RoutedEventArgs e)
    {
        switch (_current)
        {
            case Page.SalesStop: SetSalesStopped(false); break;
            case Page.Products:
                if (ProductsGrid.SelectedItem is Product p) EditProduct(p);
                else Dialog.Info("Düzenlemek için önce listeden bir ürün seçin.", "Ürün seçin");
                break;
            case Page.Categories:
                if (CategoriesGrid.SelectedItem is ProductCategory c) EditCategory(c);
                else Dialog.Info("Düzenlemek için önce listeden bir kategori seçin.", "Kategori seçin");
                break;
        }
    }

    // ===== Dashboard (Patron ekranı) =====
    private void LoadDashboard() => Safe(() => BossDash.Refresh(), "Özet");

    // ===== Şirket =====
    private void LoadCompany() => Safe(() =>
    {
        var company = _companyService.GetById(CompanyId);
        CompaniesGrid.ItemsSource = company is null ? null : new[] { company };
    }, "Şirket bilgileri");

    private void EditCompany()
    {
        var company = _companyService.GetById(CompanyId);
        if (company is null) return;
        var win = new CompanyEditWindow(company) { Owner = this };
        if (win.ShowDialog() == true)
        {
            LoadCompany();
            Session.CurrentCompany = _companyService.GetById(CompanyId); // bağlamı tazele
            CompanyContextText.Text = $"{CompanyName} / {Session.CurrentBranch?.Name ?? "-"} · KIOSK #{Session.CurrentKioskNo}";
        }
    }

    private void CompaniesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CompaniesGrid.SelectedItem is Company) EditCompany();
    }

    // ===== Şubeler =====
    private void LoadBranches() => Safe(() => BranchesGrid.ItemsSource = _branchService.GetByCompany(CompanyId, includeDeleted: true), "Şubeler");

    private void EditBranch(CompanyBranch? existing)
    {
        var win = new BranchEditWindow(CompanyId, CompanyName, existing) { Owner = this };
        if (win.ShowDialog() == true) LoadBranches();
    }

    private void BranchesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BranchesGrid.SelectedItem is CompanyBranch b) EditBranch(b);
    }

    // ===== Kullanıcılar =====
    private void LoadUsers() => Safe(() => UsersGrid.ItemsSource = _userService.GetForCompany(CompanyId, includeDeleted: true), "Kullanıcılar");

    private void EditUser(User? existing)
    {
        if (Session.CurrentCompany is null) return;
        var win = new UserEditWindow(Session.CurrentCompany, existing) { Owner = this };
        if (win.ShowDialog() == true) LoadUsers();
    }

    private void UsersGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UsersGrid.SelectedItem is User u) EditUser(u);
    }

    // ===== Vergi Grupları =====
    private void LoadTaxGroups() => Safe(() => TaxGroupsGrid.ItemsSource = _taxService.GetAll(includeDeleted: true), "Vergi grupları");

    private void EditTaxGroup(TaxGroup? existing)
    {
        var win = new TaxGroupEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadTaxGroups();
    }

    private void TaxGroupsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TaxGroupsGrid.SelectedItem is TaxGroup g) EditTaxGroup(g);
    }

    // ===== Kategoriler =====
    private void LoadCategories() => Safe(() => CategoriesGrid.ItemsSource = _categoryService.GetAll(includeDeleted: false), "Kategoriler");

    private void EditCategory(ProductCategory? existing)
    {
        var win = new ProductCategoryEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadCategories();
    }

    private void CategoriesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is ProductCategory c) EditCategory(c);
    }

    // ===== Ürünler =====
    private void LoadProducts() => Safe(() =>
    {
        var items = _productService.GetAll(includeDeleted: false);
        foreach (var p in items)   // boş kategori "(Kategorisiz)" grubunda toplansın
            if (string.IsNullOrWhiteSpace(p.CategoryNames)) p.CategoryNames = "(Kategorisiz)";
        ProductsGrid.ItemsSource = items;
        // Kategoriye göre grupla + sırala (aynı kategorideki ürünler bir arada, karışmaz)
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ProductsGrid.ItemsSource);
        if (view != null)
        {
            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("CategoryNames"));
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription("CategoryNames", System.ComponentModel.ListSortDirection.Ascending));
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));
            }
        }
    }, "Ürünler");

    private void EditProduct(Product? existing)
    {
        var win = new ProductEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadProducts();
    }

    private void ProductsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProductsGrid.SelectedItem is Product p) EditProduct(_productService.GetById(p.Id) ?? p);
    }

    // ===== Depolar =====
    private void LoadWarehouses() => Safe(() => WarehousesGrid.ItemsSource = _warehouseService.GetAll(includeDeleted: true), "Depolar");

    private void EditWarehouse(Warehouse? existing)
    {
        var win = new WarehouseEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadWarehouses();
    }

    private void WarehousesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WarehousesGrid.SelectedItem is Warehouse w) EditWarehouse(w);
    }

    // ===== Müşteriler =====
    private void LoadCustomers() => Safe(() => CustomersGrid.ItemsSource = _customerService.GetAll(includeDeleted: true), "Müşteriler");

    private void EditCustomer(Customer? existing)
    {
        var win = new CustomerEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadCustomers();
    }

    private void CustomersGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomersGrid.SelectedItem is Customer c) EditCustomer(_customerService.GetById(c.Id) ?? c);
    }

    // ===== Müşteri Grupları =====
    private void LoadCustomerGroups() => Safe(() => CustomerGroupsGrid.ItemsSource = _customerGroupService.GetAll(includeDeleted: true), "Müşteri grupları");

    private void AddCustomerGroup()
    {
        var w = new Views.QuickAddWindow("Yeni Müşteri Grubu", "Kod", "Grup Adı",
            (code, name) => _customerGroupService.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        if (w.ShowDialog() == true) LoadCustomerGroups();
    }

    // ===== Fiyat Listeleri =====
    private void LoadPriceLists() => Safe(() => PriceListsGrid.ItemsSource = _priceListService.GetAll(includeDeleted: true), "Fiyat listeleri");

    private void AddPriceList()
    {
        var w = new Views.QuickAddWindow("Yeni Fiyat Listesi", "Kod", "Liste Adı",
            (code, name) => _priceListService.Create(code, name, Session.CurrentUserId), field1Upper: true) { Owner = this };
        if (w.ShowDialog() == true) LoadPriceLists();
    }

    // ===== Lokasyonlar =====
    private void LoadZones() => Safe(() => ZonesGrid.ItemsSource = _zoneService.GetAll(includeDeleted: true), "Lokasyonlar");

    private void EditZone(Zone? existing)
    {
        var win = new ZoneEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadZones();
    }

    private void ZonesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ZonesGrid.SelectedItem is Zone z) EditZone(z);
    }

    // ===== Reyonlar =====
    private void LoadReyons() => Safe(() => ReyonsGrid.ItemsSource = _reyonService.GetAll(includeDeleted: true), "Reyonlar");

    private void EditReyon(Reyon? existing)
    {
        var win = new ReyonEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadReyons();
    }

    private void ReyonsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ReyonsGrid.SelectedItem is Reyon r) EditReyon(r);
    }

    // ===== Bankalar =====
    private void LoadBanks() => Safe(() => BanksGrid.ItemsSource = _bankService.GetAll(includeDeleted: true), "Bankalar");
    private void EditBank(Bank? existing) { var w = new BankEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadBanks(); }
    private void BanksGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (BanksGrid.SelectedItem is Bank b) EditBank(b); }

    // ===== Banka Hesapları =====
    private void LoadBankAccounts() => Safe(() => BankAccountsGrid.ItemsSource = _bankAccountService.GetAll(includeDeleted: true), "Banka hesapları");
    private void EditBankAccount(BankAccount? existing) { var w = new BankAccountEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadBankAccounts(); }
    private void BankAccountsGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (BankAccountsGrid.SelectedItem is BankAccount a) EditBankAccount(a); }

    // ===== Kredi Kartı POS =====
    private void LoadPos() => Safe(() => PosGrid.ItemsSource = _posService.GetAll(includeDeleted: true), "POS cihazları");
    private void EditPos(CreditCardPos? existing) { var w = new PosEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadPos(); }
    private void PosGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (PosGrid.SelectedItem is CreditCardPos p) EditPos(p); }

    // ===== Nakit Kasalar =====
    private void LoadCashRegisters() => Safe(() => CashRegistersGrid.ItemsSource = _cashRegisterService.GetAll(includeDeleted: true), "Nakit kasalar");
    private void EditCashRegister(CashRegister? existing) { var w = new CashRegisterEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadCashRegisters(); }
    private void CashRegistersGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (CashRegistersGrid.SelectedItem is CashRegister k) EditCashRegister(k); }
    private void LoadKiosks() => Safe(() => KiosksGrid.ItemsSource = _kioskService.GetAll(includeDeleted: true), "KIOSK'lar");
    private void EditKiosk(Kiosk? existing) { var w = new KioskEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadKiosks(); }
    private void KiosksGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (KiosksGrid.SelectedItem is Kiosk k) EditKiosk(k); }

    // ===== Yemek Çekleri =====
    private void LoadMealVouchers() => Safe(() => MealVouchersGrid.ItemsSource = _mealVoucherService.GetAll(includeDeleted: true), "Yemek çekleri");
    private void EditMealVoucher(MealVoucher? existing) { var w = new MealVoucherEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadMealVouchers(); }
    private void MealVouchersGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (MealVouchersGrid.SelectedItem is MealVoucher v) EditMealVoucher(v); }

    // ===== Yemek Çek Hesapları =====
    private void LoadMealVoucherAccounts() => Safe(() => MealVoucherAccountsGrid.ItemsSource = _mealVoucherAccountService.GetAll(includeDeleted: true), "Yemek çek hesapları");
    private void EditMealVoucherAccount(MealVoucherAccount? existing) { var w = new MealVoucherAccountEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadMealVoucherAccounts(); }
    private void MealVoucherAccountsGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (MealVoucherAccountsGrid.SelectedItem is MealVoucherAccount a) EditMealVoucherAccount(a); }

    // ===== Kuryeler =====
    private void LoadCouriers() => Safe(() => CouriersGrid.ItemsSource = _courierService.GetAll(includeDeleted: true), "Kuryeler");
    private void EditCourier(Courier? existing) { var w = new CourierEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadCouriers(); }
    private void CouriersGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (CouriersGrid.SelectedItem is Courier k) EditCourier(k); }

    // ===== Projeler =====
    private void LoadProjects() => Safe(() => ProjectsGrid.ItemsSource = _projectService.GetAll(includeDeleted: true), "Projeler");
    private void EditProject(Project? existing) { var w = new ProjectEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadProjects(); }
    private void ProjectsGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (ProjectsGrid.SelectedItem is Project p) EditProject(p); }

    // ===== Personel =====
    private void LoadPersonnel() => Safe(() => PersonnelGrid.ItemsSource = _personnelService.GetAll(includeDeleted: true), "Personel");
    private void EditPersonnel(Personnel? existing) { var w = new PersonnelEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadPersonnel(); }
    private void PersonnelGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (PersonnelGrid.SelectedItem is Personnel p) EditPersonnel(p); }

    // ===== Gider Tipleri =====
    private void LoadExpenseTypes() => Safe(() => ExpenseTypesGrid.ItemsSource = _expenseTypeService.GetAll(includeDeleted: true), "Gider tipleri");
    private void EditExpenseType(ExpenseType? existing) { var w = new ExpenseTypeEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadExpenseTypes(); }
    private void ExpenseTypesGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (ExpenseTypesGrid.SelectedItem is ExpenseType x) EditExpenseType(x); }

    // ===== Cari Hareketler =====
    private void LoadCariHareket() => Safe(() => CariHareketGrid.ItemsSource = _cariService.GetRecent(300), "Cari hareketler");
    private void AddCariHareket() { var w = new CariHareketEditWindow() { Owner = this }; if (w.ShowDialog() == true) LoadCariHareket(); }

    // ===== Rezervasyon Etiketleri =====
    private void LoadResTags() => Safe(() => ResTagsGrid.ItemsSource = _resTagService.GetAll(includeDeleted: true), "Rezervasyon etiketleri");
    private void EditResTag(ReservationTag? existing) { var w = new ReservationTagEditWindow(existing) { Owner = this }; if (w.ShowDialog() == true) LoadResTags(); }
    private void ResTagsGrid_DoubleClick(object sender, MouseButtonEventArgs e) { if (ResTagsGrid.SelectedItem is ReservationTag t) EditResTag(t); }

    // ===== Kod Serileri =====
    private void LoadCodeSeries() => Safe(() => CodeSeriesGrid.ItemsSource = _codeSeriesService.GetAll(includeDeleted: true), "Kod serileri");

    private void EditCodeSeries(CodeSeries? existing)
    {
        var win = new CodeSeriesEditWindow(existing) { Owner = this };
        if (win.ShowDialog() == true) LoadCodeSeries();
    }

    private void CodeSeriesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CodeSeriesGrid.SelectedItem is CodeSeries s) EditCodeSeries(s);
    }

    // ===== Satış Durdurma (şube bazlı) =====
    private void LoadSalesStop() => Safe(() =>
    {
        int branchId = Session.CurrentBranch?.Id ?? 0;
        SalesStopGrid.ItemsSource = branchId == 0 ? null : _branchSettingsService.GetSalesStatus(branchId);
    }, "Satış durumu");

    private void SetSalesStopped(bool stopped)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("Aktif şube yok. Satış durdurma şube bazlıdır.", "Uyarı");
            return;
        }
        var ids = SalesStopGrid.SelectedItems.OfType<ProductSalesStatus>().Select(p => p.ProductId).ToList();
        if (ids.Count == 0)
        {
            Dialog.Info("Lütfen en az bir ürün seçin.", "Uyarı");
            return;
        }
        Safe(() =>
        {
            _branchSettingsService.SetSalesStopped(ids, Session.CurrentBranch.Id, stopped, Session.CurrentUserId);
            LoadSalesStop();
        }, "Satış durumu güncelleme");
    }

    // ===== Roller =====
    private void LoadRoles() => Safe(() => RolesGrid.ItemsSource = _roleService.GetAll(), "Roller");

    private static void Safe(Action action, string what)
    {
        try { action(); }
        catch (Exception ex)
        {
            Dialog.Error($"{what} yüklenemedi: {ex.Message}", "Hata");
        }
    }

    private void SystemDefs_Click(object sender, RoutedEventArgs e)
    {
        new SystemDefinitionsWindow { Owner = this }.ShowDialog();
    }

    private void EInvoice_Click(object sender, RoutedEventArgs e)
    {
        new EInvoiceSettingsWindow { Owner = this }.ShowDialog();
    }

    private void UiVersion_Click(object sender, RoutedEventArgs e)
    {
        new UiVersionSettingsWindow { Owner = this }.ShowDialog();
    }

    private void CsvImport_Click(object sender, RoutedEventArgs e)
    {
        var win = new CsvImportWindow { Owner = this };
        if (win.ShowDialog() == true && _current == Page.Products) LoadProducts();
    }

    private void PriceTransfer_Click(object sender, RoutedEventArgs e) => new PriceTransferWindow { Owner = this }.ShowDialog();

    private void BulkPrice_Click(object sender, RoutedEventArgs e)
    {
        if (new ProductBulkPriceWindow { Owner = this }.ShowDialog() == true && _current == Page.Products) LoadProducts();
    }

    private void QuickProduct_Click(object sender, RoutedEventArgs e)
    {
        new QuickProductWindow { Owner = this }.ShowDialog();
        if (_current == Page.Products) LoadProducts();
    }

    private void PriceMatrix_Click(object sender, RoutedEventArgs e) => new ProductPriceMatrixWindow { Owner = this }.ShowDialog();

    private void Campaign_Click(object sender, RoutedEventArgs e) => new CampaignWindow { Owner = this }.ShowDialog();

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        new ZoneLayoutWindow { Owner = this }.ShowDialog();
    }

    private void Pos_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("POS için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        // Ekran Sürümü: Adisyon V2 ise yeni garson terminali, değilse mevcut masa tahtası.
        if (new CompanyUiSettingsService().Get().IsAdisyonV2)
            new WaiterTerminalV2Window { Owner = this }.ShowDialog();
        else
            new MasaBoardWindow { Owner = this }.ShowDialog();
    }

    private void Kds_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("Mutfak ekranı için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        // Ekran Sürümü: V2 ise yeni kanban KDS, değilse mevcut ekran.
        if (new CompanyUiSettingsService().Get().IsMutfakV2)
            new KdsWindowV2 { Owner = this }.ShowDialog();
        else
            new KdsWindow { Owner = this }.ShowDialog();
    }

    private void Kasa_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("Kasa için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        new KasaWindow { Owner = this }.ShowDialog();
    }

    private void Rapor_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("Rapor için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        new RaporWindow { Owner = this }.ShowDialog();
    }

    private void ShiftClose_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("Vardiya kapanışı için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        if (!Permissions.Payment)
        {
            Dialog.Warn("Vardiya kapanışı için yetkiniz yok. Yalnız kasiyer veya yönetici yapabilir.");
            return;
        }
        new ShiftCloseWindow { Owner = this }.ShowDialog();
    }

    private void Kiosk_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentBranch is null)
        {
            Dialog.Info("KIOSK için aktif bir şube gerekir.", "Uyarı");
            return;
        }
        new KioskWindow { Owner = this }.ShowDialog();
    }

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        if (sender == GrpProduct) GrpProductItems.Visibility = Vis(GrpProduct.IsChecked);
        else if (sender == GrpCustomer) GrpCustomerItems.Visibility = Vis(GrpCustomer.IsChecked);
        else if (sender == GrpLocation) GrpLocationItems.Visibility = Vis(GrpLocation.IsChecked);
        else if (sender == GrpFinance) GrpFinanceItems.Visibility = Vis(GrpFinance.IsChecked);
        else if (sender == GrpReservation) GrpReservationItems.Visibility = Vis(GrpReservation.IsChecked);
        else if (sender == GrpPaket) GrpPaketItems.Visibility = Vis(GrpPaket.IsChecked);
        else if (sender == GrpCompany) GrpCompanyItems.Visibility = Vis(GrpCompany.IsChecked);
        else if (sender == GrpUsers) GrpUsersItems.Visibility = Vis(GrpUsers.IsChecked);
        else if (sender == GrpErp) GrpErpItems.Visibility = Vis(GrpErp.IsChecked);
        else if (sender == GrpAccounting) GrpAccountingItems.Visibility = Vis(GrpAccounting.IsChecked);
        else if (sender == GrpOrders) GrpOrdersItems.Visibility = Vis(GrpOrders.IsChecked);
        else if (sender == GrpWaybills) GrpWaybillsItems.Visibility = Vis(GrpWaybills.IsChecked);
        else if (sender == GrpDepot) GrpDepotItems.Visibility = Vis(GrpDepot.IsChecked);
        else if (sender == GrpInvoices) GrpInvoicesItems.Visibility = Vis(GrpInvoices.IsChecked);
        else if (sender == GrpReports) GrpReportsItems.Visibility = Vis(GrpReports.IsChecked);

        static Visibility Vis(bool? open) => open == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Liste arama / filtre / dışa aktarma (aktif grid) =====
    private void ListSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var grid = CurrentGrid();
        if (grid?.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view is null) return;
        string q = ListSearchBox.Text?.Trim() ?? "";
        view.Filter = string.IsNullOrEmpty(q) ? null : (o => ItemMatches(o, q));
    }

    private static bool ItemMatches(object o, string q)
    {
        foreach (var p in o.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            object? v;
            try { v = p.GetValue(o); } catch { continue; }
            var s = v as string ?? v?.ToString();
            if (!string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ExportButton.ContextMenu is { } cm) { cm.PlacementTarget = ExportButton; cm.IsOpen = true; }
    }

    private ReportTable? BuildTableFromGrid()
    {
        var grid = CurrentGrid();
        if (grid?.ItemsSource is null) return null;
        var t = new ReportTable { Title = PageTitle.Text, Subtitle = PageSubtitle.Text };
        var paths = new List<string>();
        foreach (var col in grid.Columns)
        {
            if (col is DataGridBoundColumn bc && bc.Binding is Binding b && !string.IsNullOrEmpty(b.Path?.Path))
            {
                t.Columns.Add(new ReportColumn(col.Header?.ToString() ?? "", Math.Max(60, col.ActualWidth)));
                paths.Add(b.Path.Path);
            }
        }
        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        foreach (var item in view)
            t.Rows.Add(paths.Select(p => (object?)(item.GetType().GetProperty(p)?.GetValue(item))).ToArray());
        return t;
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e) { if (BuildTableFromGrid() is { } t) Exporter.SaveExcel(t); }
    private void ExportCsv_Click(object sender, RoutedEventArgs e) { if (BuildTableFromGrid() is { } t) Exporter.SaveCsv(t); }
    private void ExportPdf_Click(object sender, RoutedEventArgs e) { if (BuildTableFromGrid() is { } t) Exporter.PrintPdf(t); }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeReady) return;
        if (ThemeCombo.SelectedValue is AppTheme theme && theme != ThemeManager.Current)
        {
            ThemeManager.ChangeAndSave(theme);
            // Pencereyi yeniden oluştur (oturum korunur, stiller tazelenir)
            var w = new MainWindow();
            Application.Current.MainWindow = w;
            w.Show();
            Close();
        }
    }

    // ===== Soft-delete / geri al (yalnızca SysAdmin) =====
    private (string Table, bool Company)? PageTable() => _current switch
    {
        Page.Branches => ("CompanyBranches", false),
        Page.Users => ("Users", false),
        Page.Roles => ("Roles", false),
        Page.TaxGroups => ("TaxGroups", true),
        Page.Categories => ("ProductCategories", true),
        Page.Products => ("Products", true),
        Page.Warehouses => ("Warehouses", true),
        Page.Customers => ("Customers", true),
        Page.CustomerGroups => ("CustomerGroups", true),
        Page.PriceLists => ("PriceLists", true),
        Page.CodeSeries => ("CodeSeries", true),
        Page.Zones => ("Zones", true),
        Page.Reyons => ("Reyons", true),
        Page.Banks => ("Banks", true),
        Page.BankAccounts => ("BankAccounts", true),
        Page.Pos => ("CreditCardPos", true),
        Page.CashRegisters => ("CashRegisters", true),
        Page.Kiosks => ("Kiosks", true),
        Page.MealVouchers => ("MealVouchers", true),
        Page.MealVoucherAccounts => ("MealVoucherAccounts", true),
        Page.ResTags => ("ReservationTags", true),
        Page.Couriers => ("Couriers", true),
        Page.Projects => ("Projects", true),
        Page.Personnel => ("Personnel", true),
        Page.ExpenseTypes => ("ExpenseTypes", true),
        _ => null
    };

    private DataGrid? CurrentGrid() => _current switch
    {
        Page.CodeSeries => CodeSeriesGrid,
        Page.Zones => ZonesGrid,
        Page.Reyons => ReyonsGrid,
        Page.Banks => BanksGrid,
        Page.BankAccounts => BankAccountsGrid,
        Page.Pos => PosGrid,
        Page.CashRegisters => CashRegistersGrid,
        Page.Kiosks => KiosksGrid,
        Page.MealVouchers => MealVouchersGrid,
        Page.MealVoucherAccounts => MealVoucherAccountsGrid,
        Page.ResTags => ResTagsGrid,
        Page.Couriers => CouriersGrid,
        Page.Projects => ProjectsGrid,
        Page.Personnel => PersonnelGrid,
        Page.ExpenseTypes => ExpenseTypesGrid,
        Page.Branches => BranchesGrid,
        Page.Users => UsersGrid,
        Page.Roles => RolesGrid,
        Page.TaxGroups => TaxGroupsGrid,
        Page.Categories => CategoriesGrid,
        Page.Products => ProductsGrid,
        Page.Warehouses => WarehousesGrid,
        Page.Customers => CustomersGrid,
        Page.CustomerGroups => CustomerGroupsGrid,
        Page.PriceLists => PriceListsGrid,
        _ => null
    };

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Session.IsSysAdmin || PageTable() is not { } map) return;
        if (CurrentGrid()?.SelectedItem is not AuditableEntity sel)
        {
            Dialog.Info("Önce bir kayıt seçin.", "Uyarı");
            return;
        }
        bool newDeleted = !sel.IsDeleted;
        string action = newDeleted ? "silinsin (pasifleştirilsin)" : "geri alınsın";
        if (!Dialog.Confirm($"Seçili kayıt {action} mı?", "Onay"))
            return;

        Safe(() =>
        {
            using var conn = map.Company ? Database.OpenCompany() : Database.OpenConnection();
            Schema.SetDeleted(conn, map.Table, sel.Id, newDeleted, Session.CurrentUserId);
            ShowPage(_current);
        }, "Silme/geri alma");
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        Session.Clear();
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }
}
