using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class KdsWindow : Window
{
    private readonly KdsService _kds = new();
    private readonly OrderService _orders = new();
    private readonly ZoneService _zoneService = new();
    private readonly ReyonService _reyonService = new();
    private readonly int _branchId;
    private DispatcherTimer? _timer;

    public KdsWindow()
    {
        InitializeComponent();

        // Lisans savunma derinliği (ikinci kat): Mutfak (KDS) modülü pakette yoksa pencereyi açma.
        if (!LicenseGuard.Has(MindAeonRest.Licensing.Feature.Kitchen))
        {
            Dialog.Warn("Mutfak (KDS) modülü lisansınızda yok.", "Lisans");
            Loaded += (_, _) => Close();
            return;
        }

        _branchId = Session.CurrentBranch?.Id ?? 0;
        BranchText.Text = Session.CurrentBranch?.Name ?? "-";

        Loaded += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) => { ClockText.Text = DateTime.Now.ToString("HH:mm"); LoadTickets(); };
            _timer.Start();
            LoadReyons();
        };
        Closed += (_, _) => _timer?.Stop();
    }

    private void LoadReyons()
    {
        var zoneIds = _zoneService.GetAll().Where(z => z.BranchId == _branchId).Select(z => z.Id).ToHashSet();
        var reyons = new List<Reyon> { new() { Id = 0, Name = "Tümü" } };
        reyons.AddRange(_reyonService.GetAll().Where(r => zoneIds.Contains(r.ZoneId)));
        ReyonList.ItemsSource = reyons;
        ReyonList.SelectedIndex = 0;
    }

    private int CurrentReyonId => (ReyonList.SelectedItem as Reyon)?.Id ?? 0;

    private void Reyon_Changed(object sender, SelectionChangedEventArgs e)
    {
        ReyonTitle.Text = (ReyonList.SelectedItem as Reyon)?.Name ?? "";
        LoadTickets();
    }

    private void LoadTickets()
    {
        try
        {
            var lines = _kds.GetTickets(_branchId, CurrentReyonId);
            var tickets = lines.GroupBy(l => l.OrderId).Select(g => new KdsTicket
            {
                OrderId = g.Key,
                OrderCode = g.First().OrderCode,
                TableCodes = g.First().TableCodes,
                WaiterName = g.First().WaiterName,
                EarliestSent = g.Min(x => x.SentAt),
                Lines = g.ToList()
            }).ToList();
            TicketHost.ItemsSource = tickets;
            EmptyHint.Visibility = tickets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Ready_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsLine line) return;
        // Tek-yön ilerleme: Hazır değilse → Hazır; Hazır ise → Teslim; Teslim ise no-op.
        // (Servis ileri-yön guard'ı geri gidişi engellediği için toggle-geri ölü buton oluyordu.)
        OrderLineStatus? next = line.LineStatus switch
        {
            OrderLineStatus.Hazir => OrderLineStatus.Teslim,
            OrderLineStatus.Teslim => null,
            _ => OrderLineStatus.Hazir
        };
        if (next is null) return;
        try
        {
            _orders.SetLineStatus(line.LineId, next.Value, Session.CurrentUserId);
            LoadTickets();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    /// <summary>Fişin tüm satırlarını "Hazırlanıyor" yapar.</summary>
    private void StartTicket_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsTicket ticket) return;
        try
        {
            foreach (var l in ticket.Lines)
                _orders.SetLineStatus(l.LineId, OrderLineStatus.Hazirlaniyor, Session.CurrentUserId);
            LoadTickets();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    /// <summary>Fişin tüm satırlarını "Hazır" yapar.</summary>
    private void ReadyTicket_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not KdsTicket ticket) return;
        try
        {
            foreach (var l in ticket.Lines)
                _orders.SetLineStatus(l.LineId, OrderLineStatus.Hazir, Session.CurrentUserId);
            LoadTickets();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadTickets();
    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Bir adisyonun bu reyondaki hazırlık fişi.</summary>
public class KdsTicket
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string? TableCodes { get; set; }
    public string? WaiterName { get; set; }
    public DateTime? EarliestSent { get; set; }
    public List<KdsLine> Lines { get; set; } = new();

    // --- Rozet / vurgu için türetilmiş özellikler (gruptan dolar) ---
    /// <summary>Adisyonun herhangi bir satırında alerji notu var mı.</summary>
    public bool HasAllergy => Lines.Any(l => l.HasAllergy);
    /// <summary>İlk dolu alerji notu (kart başlığında gösterilir).</summary>
    public string? AllergyNote => Lines.FirstOrDefault(l => l.HasAllergy)?.AllergyNote;
    /// <summary>VIP siparişi.</summary>
    public bool IsVip => Lines.Any(l => l.IsVip);
    /// <summary>Öncelikli sipariş.</summary>
    public bool Priority => Lines.Any(l => l.Priority);
    /// <summary>Acı ürün içeriyor mu.</summary>
    public bool HasSpicy => Lines.Any(l => l.IsSpicy);
    /// <summary>Rozetlerden herhangi biri aktif mi (kart kenarlığını vurgulamak için).</summary>
    public bool IsHighlighted => HasAllergy || IsVip || Priority;

    public string ElapsedText
    {
        get
        {
            if (!EarliestSent.HasValue) return "";
            var s = DateTime.UtcNow - EarliestSent.Value;
            if (s.TotalMinutes < 0) s = TimeSpan.Zero;
            return s.TotalHours >= 1 ? $"{(int)s.TotalHours}sa {s.Minutes}dk" : $"{s.Minutes} dk";
        }
    }
}
