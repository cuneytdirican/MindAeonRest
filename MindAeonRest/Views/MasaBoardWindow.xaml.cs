using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class MasaBoardWindow : Window
{
    private readonly ZoneService _zoneService = new();
    private readonly TableBoardService _boardService = new();
    private readonly OrderService _orderService = new();
    private readonly CustomerService _customerService = new();
    private readonly int _branchId;
    private DispatcherTimer? _clock;

    public MasaBoardWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";
        UserText.Text = Session.CurrentUser?.FullName ?? "";

        Loaded += (_, _) =>
        {
            StartClock();
            LoadSalons();
            LoadAdisyonList();
            LoadUnpaidList();
            ApplyLeftPanelView();
            RefundToolButton.Visibility = Permissions.Payment ? Visibility.Visible : Visibility.Collapsed;
        };
        Closed += (_, _) => _clock?.Stop();
    }

    private void Refund_Click(object sender, RoutedEventArgs e) => new IadeWindow { Owner = this }.ShowDialog();

    private void StartClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
    }

    private void LoadSalons()
    {
        var zones = _zoneService.GetAll().Where(z => z.BranchId == _branchId).ToList();
        SalonList.ItemsSource = zones;
        if (zones.Count > 0) { SalonList.SelectedIndex = 0; return; }

        // Bu şubede hiç lokasyon yok → boş ekran yerine yol göster
        BoardHost.ItemsSource = null;
        BreadcrumbText.Text = "Bu şubede lokasyon tanımlı değil";
        EmptyHint.Text = "Bu şubede lokasyon/masa yok. Yönetim → Lokasyonlar ve Masa Planı'ndan ekleyin.";
        EmptyHint.Visibility = Visibility.Visible;
        LoadAdisyonList();
    }

    private void Salon_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SalonList.SelectedItem is not Zone z) return;
        BreadcrumbText.Text = $"Katlar › {z.Name}";
        LoadBoard(z.Id);
        LoadAdisyonList();   // adisyon listesi de seçili lokasyona göre gelsin
    }

    private void LoadBoard(int zoneId)
    {
        try
        {
            var items = _boardService.GetBoard(zoneId);
            BoardHost.ItemsSource = items;
            EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void LoadAdisyonList()
    {
        try
        {
            var orders = _orderService.GetOpenOrders(_branchId);
            // Seçili lokasyona ait adisyonlar (board ile tutarlı)
            if (SalonList.SelectedItem is Zone z) orders = orders.Where(o => o.ZoneId == z.Id).ToList();
            AdisyonList.ItemsSource = orders;
            AdisyonCountText.Text = $"{orders.Count} ADİSYON";
            AdisyonTotalText.Text = $"₺{orders.Sum(o => o.Total):N2}";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    /// <summary>Ödeme bekleyen / açık masalar — kasiyer/garson aramadan toplu görür (V2 paritesi).</summary>
    private void LoadUnpaidList()
    {
        if (UnpaidList is null) return;   // henüz yüklenmediyse
        try
        {
            var rows = _orderService.GetUnpaidTables(_branchId);
            UnpaidList.ItemsSource = rows;
            int waiting = rows.Count(r => r.IsPartial || r.HasBillRequest);
            UnpaidCountText.Text = $"{rows.Count} sipariş · {waiting} ödeme bekliyor";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    /// <summary>Sol panelde "Açık Adisyonlar" ile "Ödeme Bekleyen Masalar" görünümünü değiştirir.</summary>
    private void LeftTab_Changed(object sender, RoutedEventArgs e) => ApplyLeftPanelView();

    private void ApplyLeftPanelView()
    {
        if (UnpaidList is null || AdisyonList is null) return;   // InitializeComponent sırasında erken tetiklenmeye karşı
        bool unpaid = TabUnpaid.IsChecked == true;

        UnpaidList.Visibility = unpaid ? Visibility.Visible : Visibility.Collapsed;
        UnpaidCountText.Visibility = unpaid ? Visibility.Visible : Visibility.Collapsed;
        AdisyonList.Visibility = unpaid ? Visibility.Collapsed : Visibility.Visible;
        AdisyonCountText.Visibility = unpaid ? Visibility.Collapsed : Visibility.Visible;

        // Alt toplam etiketi/değeri görünüme göre
        if (unpaid)
        {
            var rows = UnpaidList.ItemsSource as IEnumerable<UnpaidTable> ?? Enumerable.Empty<UnpaidTable>();
            LeftFooterLabel.Text = "KALAN";
            AdisyonTotalText.Text = $"₺{rows.Sum(r => r.Remaining):N2}";
        }
        else
        {
            LeftFooterLabel.Text = "TOPLAM";
            var orders = AdisyonList.ItemsSource as IEnumerable<Order> ?? Enumerable.Empty<Order>();
            AdisyonTotalText.Text = $"₺{orders.Sum(o => o.Total):N2}";
        }
    }

    private void Unpaid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UnpaidList.SelectedItem is UnpaidTable u && _orderService.GetById(u.OrderId) is { } full) OpenAdisyon(full);
    }

    private void Refresh()
    {
        if (SalonList.SelectedItem is Zone z) LoadBoard(z.Id);
        LoadAdisyonList();
        LoadUnpaidList();
        ApplyLeftPanelView();
    }

    private void Table_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TableBoardItem item }) return;
        if (SalonList.SelectedItem is not Zone zone) return;
        try
        {
            Order order;
            if (item.Status == TableBoardStatus.Occupied && item.OrderId is int oid)
            {
                if (_orderService.GetById(oid) is not { } existing)
                { Dialog.Info("Bu masa artık açık değil, liste yenileniyor.", "Masa"); Refresh(); return; }
                order = existing;
            }
            else
            {
                var paCust = _customerService.GetAll().FirstOrDefault(c => c.Code == "PA")
                    ?? throw new InvalidOperationException("Varsayılan 'PA' (perakende) cari kartı bulunamadı. Cari Kartlar'dan oluşturun.");
                order = _orderService.CreateOrder(_branchId, zone.Id, paCust.Id, new[] { item.TableId }, Session.CurrentUserId);
            }
            OpenAdisyon(order);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Adisyon_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AdisyonList.SelectedItem is Order o && _orderService.GetById(o.Id) is { } full) OpenAdisyon(full);
    }

    private bool _adisyonOpen;   // aynı anda iki adisyon penceresi açılmasını engeller (kopya pencere)
    private void OpenAdisyon(Order order)
    {
        if (_adisyonOpen) return;
        _adisyonOpen = true;
        try { new AdisyonWindow(order) { Owner = this }.ShowDialog(); }
        finally { _adisyonOpen = false; }
        // Mutfağa gönderilmeden / hiç ürün eklenmeden kapatıldıysa (yanlışlıkla dokunma) masayı serbest bırak
        try { _orderService.DeleteIfEmpty(order.Id, Session.CurrentUserId); } catch { }
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentUser is { } u) new LockScreenWindow(u) { Owner = this }.ShowDialog();
    }

    private Order? SelectedAdisyon => AdisyonList.SelectedItem as Order;

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAdisyon is not { } o) { Dialog.Info("Önce soldaki listeden bir adisyon seçin.", "Bilgi"); return; }
        if (_orderService.GetById(o.Id) is { } full) OpenAdisyon(full);
    }

    private void Paket_Click(object sender, RoutedEventArgs e)
    {
        new PaketWindow { Owner = this }.ShowDialog();
        Refresh();
    }

    private void Notlar_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAdisyon is not { } o) { Dialog.Info("Önce bir adisyon seçin.", "Bilgi"); return; }
        var w = new NoteWindow(0, o.Note, $"Adisyon {o.Code}") { Owner = this };
        if (w.ShowDialog() != true) return;
        try { _orderService.SetOrderNote(o.Id, w.ResultNote, Session.CurrentUserId); Refresh(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Tasi_Click(object sender, RoutedEventArgs e)
    {
        if (SalonList.SelectedItem is not Zone zone) { Dialog.Info("Önce bir lokasyon seçin.", "Bilgi"); return; }
        var board = _boardService.GetBoard(zone.Id);

        // 1) Taşınacak (dolu) masayı seç — soldan ön-seçim ŞART DEĞİL.
        int orderId;
        if (SelectedAdisyon is { } pre) orderId = pre.Id;   // soldan seçtiyse onu kullan
        else
        {
            var occupied = board.Where(t => !t.IsEmpty && t.OrderId is int)
                                 .Select(t => new ComboItem(t.OrderId!.Value, t.Display)).ToList();
            if (occupied.Count == 0) { Dialog.Info("Bu lokasyonda dolu masa yok.", "Bilgi"); return; }
            var src = new ComboChoiceWindow("Adisyonu Taşı", "Taşınacak (dolu) masa", occupied) { Owner = this };
            if (src.ShowDialog() != true || src.SelectedValue is not int sid) return;
            orderId = sid;
        }

        // 2) Hedef boş masayı seç.
        var empties = board.Where(t => t.IsEmpty).Select(t => new ComboItem(t.TableId, t.Display)).ToList();
        if (empties.Count == 0) { Dialog.Info("Bu lokasyonda boş masa yok.", "Bilgi"); return; }
        var w = new ComboChoiceWindow("Adisyonu Taşı", "Hedef (boş) masa", empties) { Owner = this };
        if (w.ShowDialog() != true || w.SelectedValue is not int target) return;

        try { _orderService.MoveOrder(orderId, target, Session.CurrentUserId); Refresh(); Dialog.Success("Masa taşındı."); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    /// <summary>Kaynak (dolu) adisyonu seçtirir: soldan ön-seçim varsa onu, yoksa lokasyondaki dolu masalardan combo ile.</summary>
    private int? PickSourceOrderId(IReadOnlyList<TableBoardItem> board, string title)
    {
        if (SelectedAdisyon is { } pre) return pre.Id;
        var occupied = board.Where(t => !t.IsEmpty && t.OrderId is int)
                            .Select(t => new ComboItem(t.OrderId!.Value, t.Display)).ToList();
        if (occupied.Count == 0) { Dialog.Info("Bu lokasyonda dolu masa yok.", "Bilgi"); return null; }
        var src = new ComboChoiceWindow(title, "Kaynak (dolu) masa", occupied) { Owner = this };
        if (src.ShowDialog() != true || src.SelectedValue is not int sid) return null;
        return sid;
    }

    /// <summary>Masa Böl: kaynak adisyondan seçilen koltuğun (kişinin) kalemlerini boş hedef masada yeni adisyona böler.</summary>
    private void Bol_Click(object sender, RoutedEventArgs e)
    {
        if (SalonList.SelectedItem is not Zone zone) { Dialog.Info("Önce bir lokasyon seçin.", "Bilgi"); return; }
        var board = _boardService.GetBoard(zone.Id);

        // 1) Kaynak (dolu) adisyon
        if (PickSourceOrderId(board, "Masayı Böl") is not int orderId) return;

        // 2) Bölünecek kişi (koltuk) — o koltuğun ÜST satırları (ParentLineId==null) bölünür
        var lines = _orderService.GetLines(orderId)
            .Where(l => l.LineStatus != OrderLineStatus.Iptal && l.ParentLineId == null && l.SettledClFicheId == null).ToList();
        if (lines.Count == 0) { Dialog.Info("Bu adisyonda bölünebilecek (ödenmemiş) kalem yok.", "Bilgi"); return; }

        var seats = lines.Select(l => l.SeatNo).Distinct().OrderBy(s => s)
            .Select(s => new ComboItem(s, s > 0 ? $"{s}. Kişi" : "Ortak")).ToList();
        if (seats.Count < 2) { Dialog.Info("Bölmek için adisyonda en az iki kişi/koltuk olmalı. Tek koltuk için 'Taşı' kullanın.", "Bilgi"); return; }
        var sw = new ComboChoiceWindow("Masayı Böl", "Bölünecek kişi (koltuk)", seats) { Owner = this };
        if (sw.ShowDialog() != true || sw.SelectedValue is not int seat) return;

        var topIds = lines.Where(l => l.SeatNo == seat).Select(l => l.Id).ToList();
        if (topIds.Count == 0) { Dialog.Info("Seçilen kişide bölünecek kalem yok.", "Bilgi"); return; }

        // 3) Hedef boş masa
        var empties = board.Where(t => t.IsEmpty).Select(t => new ComboItem(t.TableId, t.Display)).ToList();
        if (empties.Count == 0) { Dialog.Info("Bu lokasyonda boş masa yok.", "Bilgi"); return; }
        var tw = new ComboChoiceWindow("Masayı Böl", "Hedef (boş) masa", empties) { Owner = this };
        if (tw.ShowDialog() != true || tw.SelectedValue is not int targetTable) return;

        try { _orderService.SplitOrder(orderId, topIds, targetTable, Session.CurrentUserId); Refresh(); Dialog.Success("Masa bölündü."); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    /// <summary>Masa Birleştir: kaynak adisyonu, seçilen DOLU hedef masanın açık adisyonuna birleştirir (kaynak kapanır).</summary>
    private void Birlestir_Click(object sender, RoutedEventArgs e)
    {
        if (SalonList.SelectedItem is not Zone zone) { Dialog.Info("Önce bir lokasyon seçin.", "Bilgi"); return; }
        var board = _boardService.GetBoard(zone.Id);

        // 1) Kaynak (dolu) adisyon
        if (PickSourceOrderId(board, "Masaları Birleştir") is not int sourceOrderId) return;

        // 2) Hedef DOLU masa (kaynağın masası hariç) → açık adisyonuna birleştir
        var targets = board.Where(t => !t.IsEmpty && t.OrderId is int oid && oid != sourceOrderId)
                           .Select(t => new ComboItem(t.TableId, t.Display)).ToList();
        if (targets.Count == 0) { Dialog.Info("Birleştirilecek başka dolu masa yok.", "Bilgi"); return; }
        var tw = new ComboChoiceWindow("Masaları Birleştir", "Hedef (dolu) masa", targets) { Owner = this };
        if (tw.ShowDialog() != true || tw.SelectedValue is not int targetTable) return;

        var targetOrderId = _orderService.GetOpenByTable(targetTable)?.Id;
        if (targetOrderId is null) { Dialog.Info("Hedef masada açık adisyon bulunamadı, liste yenileniyor.", "Bilgi"); Refresh(); return; }
        if (targetOrderId == sourceOrderId) { Dialog.Info("Kaynak ve hedef adisyon aynı.", "Bilgi"); return; }

        if (!Dialog.Confirm("Kaynak adisyonun tüm kalemleri hedef masaya aktarılacak ve kaynak adisyon kapanacak. Devam edilsin mi?", "Masaları Birleştir")) return;

        try { _orderService.MergeOrders(sourceOrderId, targetOrderId.Value, Session.CurrentUserId); Refresh(); Dialog.Success("Masalar birleştirildi."); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }
}
