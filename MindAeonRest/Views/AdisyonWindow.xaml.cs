using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class AdisyonWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly PosCatalogService _catalog = new();
    private readonly TableBoardService _board = new();
    private Order _order;
    private readonly int _branchId;
    private readonly int _zoneId;

    private List<PosProduct> _allProducts = new();
    private readonly ObservableCollection<OrderLine> _lines = new();
    private List<PriceList> _priceLists = new();
    private int? _priceListId;                 // seçili fiyat listesi (null = genel)
    private readonly List<Button> _priceListBtns = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<AdCampaignVM> _campaignVMs = new();
    private bool _campaignsLoaded;
    private int _currentSeat = 1;
    private int _maxSeat = 1;
    private CustomerDisplayWindow? _customerDisplay;

    public AdisyonWindow(Order order)
    {
        InitializeComponent();
        _order = order;
        _branchId = order.BranchId;
        _zoneId = order.ZoneId;

        TableText.Text = string.IsNullOrWhiteSpace(order.TableCodes) ? order.Code : order.TableCodes;
        ZoneText.Text = order.ZoneName;
        WaiterText.Text = Session.CurrentUser?.FullName ?? "";

        var view = (ICollectionView)CollectionViewSource.GetDefaultView(_lines);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OrderLine.SeatText)));
        LinesList.ItemsSource = view;

        Loaded += (_, _) =>
        {
            LoadCatalog();
            ReloadLines();
            PayButton.IsEnabled = Permissions.Payment;   // ödeme yalnızca Kasiyer/Admin
            UpdateFlagVisuals();
        };

        // Müşteri ekranı, adisyon kapanınca da kapansın (tek-sefer abonelik; aç/kapa döngüsünde birikmesin).
        Closed += (_, _) => _customerDisplay?.Close();
    }

    // ===== VIP / Öncelik / Alerji (KDS rozetleri — V2 ile parite) =====
    private void UpdateFlagVisuals()
    {
        VipToggle.Background = _order.IsVip ? (System.Windows.Media.Brush)FindResource("PosAccentBrush") : (System.Windows.Media.Brush)FindResource("PosSubtleBrush");
        VipToggle.Foreground = _order.IsVip ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("PosTextBrush");
        PriorityToggle.Background = _order.Priority ? (System.Windows.Media.Brush)FindResource("PosAccentBrush") : (System.Windows.Media.Brush)FindResource("PosSubtleBrush");
        PriorityToggle.Foreground = _order.Priority ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("PosTextBrush");
        AllergyBtn.Content = string.IsNullOrWhiteSpace(_order.AllergyNote) ? "⚠ Alerji" : "⚠ Alerji ✓";
    }

    private void Vip_Click(object sender, RoutedEventArgs e)
    {
        try { _order.IsVip = !_order.IsVip; _orders.SetOrderFlags(_order.Id, _order.IsVip, null, Session.CurrentUserId); UpdateFlagVisuals(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "VIP"); }
    }

    private void Priority_Click(object sender, RoutedEventArgs e)
    {
        try { _order.Priority = !_order.Priority; _orders.SetOrderFlags(_order.Id, null, _order.Priority, Session.CurrentUserId); UpdateFlagVisuals(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Öncelik"); }
    }

    private void Allergy_Click(object sender, RoutedEventArgs e)
    {
        var w = new TextPromptWindow("Alerji Notu", "Örn: Fıstık alerjisi", _order.AllergyNote, NotePresets.Allergy) { Owner = this };
        if (w.ShowDialog() == true)
        {
            try { _order.AllergyNote = string.IsNullOrWhiteSpace(w.Value) ? null : w.Value.Trim(); _orders.SetAllergyNote(_order.Id, _order.AllergyNote, Session.CurrentUserId); UpdateFlagVisuals(); }
            catch (Exception ex) { Dialog.Error(ex.Message, "Alerji"); }
        }
    }

    private void LoadCatalog()
    {
        try
        {
            var cats = new List<ProductCategory> { new() { Id = 0, Name = "Tümü" } };
            cats.AddRange(_catalog.GetCategories());
            CategoryList.ItemsSource = cats;

            // Fiyat listesi: müşterinin atanmış listesi varsayılan; yoksa ilk aktif liste; o da yoksa Genel
            _priceLists = new PriceListService().GetAll().Where(p => p.IsActive).ToList();
            int? custList = _order.CustomerId > 0 ? new CustomerService().GetById(_order.CustomerId)?.PriceListId : null;
            _priceListId = custList ?? _priceLists.FirstOrDefault()?.Id;
            BuildPriceListBar();

            _allProducts = _catalog.GetProducts(_branchId, _priceListId);
            CategoryList.SelectedIndex = 0;   // tetikler FilterProducts
            LoadCampaigns();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    // Şu an geçerli (şube + tarih/gün/saat) kampanyalar — panele yüklenir (kalıcı görünür)
    private void LoadCampaigns()
    {
        _campaignVMs.Clear();
        foreach (var c in new CampaignService().GetActive(_branchId, DateTime.Now))
            _campaignVMs.Add(new AdCampaignVM(c));
        CampaignHost.ItemsSource = _campaignVMs;
        _campaignsLoaded = true;
        RefreshCampaigns();
    }

    // Her sipariş değişiminde: kriter sağlanıyor mu? canlı güncelle + uygulanan kriteri kaybettiyse uyar+kaldır
    private void RefreshCampaigns()
    {
        if (!_campaignsLoaded) return;
        var applied = _orders.GetAppliedCampaignIds(_order.Id);

        // Uygulanan ama artık kriteri sağlamayan kampanya(lar) → uyar + kaldır
        var lost = _campaignVMs.Where(v => applied.Contains(v.Camp.Id) && !_orders.IsCampaignEligible(_order.Id, v.Camp)).ToList();
        if (lost.Count > 0)
        {
            Dialog.Warn($"'{string.Join(", ", lost.Select(v => v.Camp.Name))}' kampanya(sı) artık kriterleri karşılamıyor. Uygulanan kampanya(lar) kaldırıldı.", "Kampanya");
            try { _orders.RemoveCampaign(_order.Id, Session.CurrentUserId); } catch { }
            ReloadLines();   // yeniden yükle → bu metot tekrar çalışır (artık uygulanan yok)
            return;
        }

        int eligible = 0;
        foreach (var v in _campaignVMs)
        {
            v.IsApplied = applied.Contains(v.Camp.Id);
            v.IsEligible = _orders.IsCampaignEligible(_order.Id, v.Camp);
            if (v.IsEligible && !v.IsApplied) eligible++;
            v.StatusText = v.IsApplied ? "✓ Uygulandı"
                : v.IsEligible ? (v.Camp.IsMandatory ? "🔒 OTOMATİK (zorunlu)" : "🔴 KULLANILABİLİR!")
                : "kriter sağlanmıyor";
        }

        // ZORUNLU kampanyalar: kriter sağlanıyorsa OTOMATİK uygula (kullanıcı seçmesine gerek yok)
        if (!_autoApplying)
        {
            var autoApply = _campaignVMs.Where(v => v.Camp.IsMandatory && v.IsEligible && !v.IsApplied).Select(v => v.Camp).ToList();
            if (autoApply.Count > 0)
            {
                _autoApplying = true;
                bool any = false;
                foreach (var camp in autoApply)
                    try { _orders.ApplyCampaign(_order.Id, camp.Id, _branchId, Session.CurrentUserId); any = true; } catch { }
                if (any) { ReloadLines(); _autoApplying = false; return; }   // tekrar yükle → uygulanmış görünür
                _autoApplying = false;
            }
        }

        CampaignHint.Text = _campaignVMs.Count == 0 ? "Şu an geçerli kampanya yok."
            : eligible > 0 ? $"🔴 {eligible} kampanya kullanılabilir!" : $"{_campaignVMs.Count} kampanya";
    }

    private bool _autoApplying;

    private void ApplyCampaign_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AdCampaignVM vm) return;
        try { _orders.ApplyCampaign(_order.Id, vm.Camp.Id, _branchId, Session.CurrentUserId); ReloadLines(); }
        catch (Exception ex) { Dialog.Warn(ex.Message, "Kampanya"); }
    }

    // Fiyat listesi butonları: "Genel" + her aktif liste; tıkla → fiyatları yeniden çöz
    private void BuildPriceListBar()
    {
        PriceListBar.Children.Clear();
        _priceListBtns.Clear();
        AddPriceListBtn("Genel", null);
        foreach (var pl in _priceLists) AddPriceListBtn(pl.Name, pl.Id);
        HighlightPriceList();
    }

    private void AddPriceListBtn(string text, int? id)
    {
        var b = new Button
        {
            Content = text, Tag = id, Height = 38, MinWidth = 90, Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13, FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0), Padding = new Thickness(12, 0, 12, 0)
        };
        b.Click += (_, _) => SetPriceList(id);
        _priceListBtns.Add(b);
        PriceListBar.Children.Add(b);
    }

    private void SetPriceList(int? id)
    {
        _priceListId = id;
        var now = DateTime.Now;
        foreach (var p in _allProducts) p.ResolvePrice(_branchId, id, now);   // DB'ye gitmeden yeniden çöz
        HighlightPriceList();
        FilterProducts();   // kartlar yeni fiyatla yenilensin
    }

    private void HighlightPriceList()
    {
        var active = (Brush)FindResource("PosAccentBrush");
        var idle = (Brush)FindResource("PosSubtleBrush");
        foreach (var b in _priceListBtns)
        {
            bool on = (b.Tag as int?) == _priceListId;
            b.Background = on ? active : idle;
            b.Foreground = on ? Brushes.White : (Brush)FindResource("PosTextBrush");
        }
    }

    private void FilterProducts()
    {
        int catId = (CategoryList.SelectedItem as ProductCategory)?.Id ?? 0;
        string q = SearchBox.Text?.Trim() ?? "";
        IEnumerable<PosProduct> items = _allProducts;
        if (catId != 0) items = items.Where(p => p.CategoryIds.Contains(catId));
        if (q.Length > 0) items = items.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(q, StringComparison.OrdinalIgnoreCase));
        ProductHost.ItemsSource = items.ToList();
        CrumbText.Text = "Menüler › " + ((CategoryList.SelectedItem as ProductCategory)?.Name ?? "Tümü");
    }

    private void Campaign_Click(object sender, RoutedEventArgs e)
    {
        var w = new CampaignApplyWindow(_order.Id, _branchId) { Owner = this };
        if (w.ShowDialog() == true && w.Changed) ReloadLines();
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e) => FilterProducts();
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_allProducts.Count > 0) FilterProducts(); }

    // ===== Kişi =====
    private void BuildSeats()
    {
        SeatHost.Children.Clear();
        for (int i = 1; i <= _maxSeat; i++)
        {
            int seat = i;
            var b = new Button
            {
                Content = $"{i}",
                Width = 44, Height = 40, Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0),
                Foreground = seat == _currentSeat ? Brushes.White : (Brush)FindResource("PosTextBrush"),
                Background = seat == _currentSeat ? (Brush)FindResource("PosAccentBrush") : (Brush)FindResource("PosSubtleBrush")
            };
            b.Click += (_, _) => { _currentSeat = seat; BuildSeats(); };
            SeatHost.Children.Add(b);
        }
        var bg = (Brush)FindResource("PosSubtleBrush");
        var minus = new Button
        {
            Content = "− Kişi", Height = 40, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PosAccentBrush"), Background = bg,
            ToolTip = "Kişi sayısını azalt (dolu koltuğun altına inmez)"
        };
        minus.Click += (_, _) =>
        {
            int used = _lines.Count == 0 ? 1 : _lines.Max(l => l.SeatNo);
            int nv = Math.Max(Math.Max(1, used), _maxSeat - 1);
            if (nv == _maxSeat) return;
            _maxSeat = nv;
            if (_currentSeat > _maxSeat) _currentSeat = _maxSeat;
            PersistCover(); BuildSeats();
        };
        SeatHost.Children.Add(minus);

        var add = new Button
        {
            Content = "+ Kişi", Height = 40, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PosAccentBrush"), Background = bg,
            ToolTip = "Kişi sayısını artır"
        };
        add.Click += (_, _) => { _maxSeat++; _currentSeat = _maxSeat; PersistCover(); BuildSeats(); };
        SeatHost.Children.Add(add);
    }

    private void PersistCover()
    {
        try { _orders.SetCoverCount(_order.Id, _maxSeat, Session.CurrentUserId); _order.CoverCount = _maxSeat; } catch { }
    }

    // ===== Satırlar =====
    private void ReloadLines()
    {
        _lines.Clear();
        var lines = _orders.GetLines(_order.Id).Where(l => l.LineStatus != OrderLineStatus.Iptal).ToList();
        foreach (var l in lines) _lines.Add(l);
        _order = _orders.GetById(_order.Id) ?? _order;

        // Kişi (kuver) sayısı = kaydedilen CoverCount, en az 1 ve dolu koltuk sayısı kadar
        int usedSeat = lines.Count == 0 ? 1 : lines.Max(l => l.SeatNo);
        _maxSeat = Math.Max(Math.Max(1, _order.CoverCount), usedSeat);
        if (_currentSeat > _maxSeat) _currentSeat = _maxSeat;
        BuildSeats();

        TotalText.Text = _order.TotalText;
        _customerDisplay?.Update(lines, _order.Total);
        RefreshCampaigns();
    }

    private void Customer_Click(object sender, RoutedEventArgs e)
    {
        if (_customerDisplay is { IsVisible: true }) { _customerDisplay.Close(); _customerDisplay = null; return; }
        _customerDisplay = new CustomerDisplayWindow();
        _customerDisplay.Closed += (_, _) => _customerDisplay = null;
        _customerDisplay.Show();
        _customerDisplay.Update(_orders.GetLines(_order.Id).Where(l => l.LineStatus != OrderLineStatus.Iptal).ToList(), _order.Total);
    }

    private OrderLine? Selected => LinesList.SelectedItem as OrderLine;

    private void Product_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PosProduct p) return;
        if (p.IsSalesStopped) { Dialog.Info($"{p.Name} şu an satışa kapalı.", "Satışta değil"); return; }
        try
        {
            decimal price = p.Price;
            string? note = null;
            if (p.HasOptions)
            {
                var w = new OptionPickerWindow(p.Name, p.Price, p.Options) { Owner = this };
                if (w.ShowDialog() != true) return;   // vazgeçildi
                price = p.Price + w.TotalDelta;
                note = string.IsNullOrWhiteSpace(w.NoteText) ? null : w.NoteText;
            }
            _orders.AddLine(_order.Id, _zoneId, p.Id, 1, price, p.TaxRate, note, _currentSeat, Session.CurrentUserId);
            ReloadLines();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) { SearchBox.Focus(); VirtualKeyboardWindow.ShowFor(SearchBox); }

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is PosProduct p && p.HasDescription)
            Dialog.Info(p.Description, p.Name);
    }

    private void Barcode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string code = BarcodeBox.Text.Trim();
        BarcodeBox.Clear();
        if (code.Length == 0) return;
        int? pid = _orders.ResolveProductIdByBarcode(code);
        if (pid is null) { Dialog.Info("Barkod bulunamadı.", "Barkod"); return; }
        var p = _allProducts.FirstOrDefault(x => x.Id == pid.Value);
        if (p is null) { Dialog.Info("Ürün aktif katalogda değil.", "Barkod"); return; }
        if (p.IsSalesStopped) { Dialog.Info($"{p.Name} satışa kapalı.", "Barkod"); return; }
        _orders.AddLine(_order.Id, _zoneId, p.Id, 1, p.Price, p.TaxRate, null, _currentSeat, Session.CurrentUserId);
        ReloadLines();
    }

    private void Line_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not { } line) return;
        var w = new NoteWindow(line.ProductId, line.KitchenNote, line.ProductName ?? "") { Owner = this };
        if (w.ShowDialog() == true) { _orders.UpdateLineNote(line.Id, w.ResultNote, Session.CurrentUserId); ReloadLines(); }
    }

    private void Treat_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("İkram (bedelsiz) için yetkiniz yok; kasiyer/yönetici onayı gerekir."); return; }
        if (Selected is not { } line) { NeedSelect(); return; }
        _orders.SetTreat(line.Id, !line.IsTreat, Session.CurrentUserId);
        ReloadLines();
    }

    private void Discount_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Payment) { Dialog.Warn("İskonto için yetkiniz yok; kasiyer/yönetici onayı gerekir."); return; }
        if (Selected is not { } line) { NeedSelect(); return; }
        var w = new AmountPromptWindow("Satır İskontosu", $"{line.ProductName} — iskonto tutarı (₺)", line.DiscountAmount) { Owner = this };
        if (w.ShowDialog() != true) return;
        decimal max = line.Qty * line.UnitPrice;
        if (w.Amount > max) { Dialog.Warn($"İskonto satır tutarını ({max:N2}) aşamaz."); return; }
        try { _orders.SetLineDiscount(line.Id, w.Amount, Session.CurrentUserId); ReloadLines(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void CancelLine_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } line) { NeedSelect(); return; }
        bool sent = line.LineStatus is OrderLineStatus.MutfagaGonderildi or OrderLineStatus.Hazirlaniyor or OrderLineStatus.Hazir or OrderLineStatus.Teslim;
        bool returnToStock = true;
        if (sent)
        {
            // Mutfağa gitmiş ürün stoğu düşürdü → iptalde Fire mi Stoğa iade mi sorulur (ikisi de HESABA İŞLENMEZ)
            var r = Dialog.YesNoCancel(
                $"'{line.ProductName}' mutfağa gönderilmişti, iptal ediliyor.\n\nMalzemeler ne olsun?\n\n• EVET = STOĞA İADE (kullanılabilir, stoğa geri eklenir)\n• HAYIR = FİRE (çöp/zayi, stoğa dönmez)\n• VAZGEÇ = işlemi iptal et\n\n(Her iki durumda da bu satır hesaba/ödemeye GİRMEZ.)",
                "İptal — Fire mi, Stoğa İade mi?");
            if (r == DialogResultType.Cancel) return;
            returnToStock = r == DialogResultType.Yes;
        }
        else if (!Dialog.Confirm($"'{line.ProductName}' iptal edilsin mi?\n(Mutfağa gönderilmediği için stok etkilenmez, hesaba da girmez.)", "İptal")) return;

        // İptal sebebi (rapor için) — boş bırakılabilir
        var rp = new ReasonPromptWindow("İptal Sebebi", $"'{line.ProductName}' iptal sebebi") { Owner = this };
        if (rp.ShowDialog() != true) return;

        _orders.CancelLine(line.Id, Session.CurrentUserId, returnToStock, string.IsNullOrWhiteSpace(rp.Reason) ? null : rp.Reason);
        ReloadLines();
    }

    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        _orders.Reprint(_order.Id, Session.CurrentUserId);
        ReloadLines();
        Dialog.Info("Gönderilmiş satırların yazdırma sayısı artırıldı (yeniden baskı).", "Yeniden Yazdır");
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tickets = _orders.SendToKitchen(_order.Id, Session.CurrentUserId);
            ReloadLines();
            int printed = 0, noPrinter = 0;
            foreach (var t in tickets)
            {
                if (t.HasPrinter) { if (ReceiptVisual.PrintKitchen(t)) printed++; }
                else noPrinter++;
            }
            string msg = "Bekleyen satırlar mutfağa/reyonlara gönderildi.";
            if (printed > 0) msg += $"\n🖨 {printed} reyon fişi yazıcıdan basıldı.";
            if (noPrinter > 0) msg += $"\n{noPrinter} reyon yazıcısız (yalnız ekran/KDS'te görünür).";
            Dialog.Info(msg, "Mutfağa Gönder");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (_orders.GetLines(_order.Id).All(l => l.LineStatus == OrderLineStatus.Iptal || l.SettledClFicheId != null || l.IsTreat))
        { Dialog.Info("Ödenecek açık satır yok.", "Ödeme"); return; }

        // Ekran Sürümü: V2 ise yeni Kasiyer Ödeme istasyonu, değilse mevcut ödeme penceresi.
        Window w = new CompanyUiSettingsService().Get().IsAdisyonV2
            ? new PaymentWindowV2(_order, Session.CurrentKioskId)
            : new PaymentWindow(_order, Session.CurrentKioskId);
        w.Owner = this;
        if (w.ShowDialog() == true)
        {
            _order = _orders.GetById(_order.Id) ?? _order;
            if (_order.Status == OrderStatus.Kapandi) { DialogResult = true; Close(); }
            else ReloadLines();
        }
    }

    private void Receipt_Click(object sender, RoutedEventArgs e)
    {
        // Hesap fişi yazdırma masayı KAPATMAZ (ödeme alınınca kapanır).
        new HesapFisiWindow(_order) { Owner = this }.ShowDialog();
    }

    /// <summary>Seçili kişinin (koltuğun) ödenmemiş üst kalemlerini boş masaya böler (SplitOrder) ya da dolu masaya aktarır (MoveLines).</summary>
    private void SplitMove_Click(object sender, RoutedEventArgs e)
    {
        // Bölünecek koltuk: seçili satır varsa onun koltuğu, yoksa o an aktif koltuk.
        int seat = Selected?.SeatNo ?? _currentSeat;

        var topIds = _orders.GetLines(_order.Id)
            .Where(l => l.LineStatus != OrderLineStatus.Iptal && l.ParentLineId == null
                        && l.SettledClFicheId == null && l.SeatNo == seat)
            .Select(l => l.Id).ToList();
        if (topIds.Count == 0)
        { Dialog.Info($"Seçili kişide ({(seat > 0 ? seat + ". Kişi" : "Ortak")}) aktarılacak (ödenmemiş) kalem yok.", "Böl/Aktar"); return; }

        var board = _board.GetBoard(_zoneId);

        // Hedef: boş masa = BÖL (yeni adisyon), dolu masa = AKTAR (var olan adisyona). Bu adisyonun masaları hariç.
        var targets = board.Where(t => t.OrderId != _order.Id)
            .Select(t => new ComboItem(t.TableId, t.IsEmpty ? $"{t.Display}  (boş → böl)" : $"{t.Display}  (dolu → aktar)"))
            .ToList();
        if (targets.Count == 0) { Dialog.Info("Bu lokasyonda uygun hedef masa yok.", "Böl/Aktar"); return; }

        var seatLabel = seat > 0 ? $"{seat}. Kişi" : "Ortak";
        var tw = new ComboChoiceWindow("Kişiyi Böl / Aktar", $"{seatLabel} → hedef masa", targets) { Owner = this };
        if (tw.ShowDialog() != true || tw.SelectedValue is not int targetTable) return;

        try
        {
            var targetOrder = _orders.GetOpenByTable(targetTable);
            if (targetOrder is null)
                _orders.SplitOrder(_order.Id, topIds, targetTable, Session.CurrentUserId);   // boş masa → yeni adisyon
            else
                _orders.MoveLines(_order.Id, topIds, targetOrder.Id, Session.CurrentUserId);  // dolu masa → mevcut adisyon
            ReloadLines();
            Dialog.Success(targetOrder is null ? "Kişi yeni masaya bölündü." : "Kişi/kalemler hedef masaya aktarıldı.");
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private static void NeedSelect() => Dialog.Info("Önce bir satır seçin.", "Uyarı");

    private void Back_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentUser is { } u) new LockScreenWindow(u) { Owner = this }.ShowDialog();
    }
}

/// <summary>Adisyon kampanya paneli satırı: kritere uygunluk + uygulanma durumu canlı güncellenir.</summary>
public class AdCampaignVM : INotifyPropertyChanged
{
    public Campaign Camp { get; }
    public AdCampaignVM(Campaign c) => Camp = c;

    public string Name => Camp.Name;
    public string ShortText => Camp.ValueText;

    private bool _eligible;
    public bool IsEligible { get => _eligible; set { _eligible = value; OnCh(nameof(IsEligible)); } }
    private bool _applied;
    public bool IsApplied { get => _applied; set { _applied = value; OnCh(nameof(IsApplied)); } }
    private string _status = "";
    public string StatusText { get => _status; set { _status = value; OnCh(nameof(StatusText)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnCh(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
