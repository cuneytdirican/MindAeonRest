using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class KioskDashboardWindow : Window
{
    private readonly ReportService _reports = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _ready;

    public KioskDashboardWindow()
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;

        var branchItems = new List<ComboItem> { new(0, "Tüm Şubeler") };
        branchItems.AddRange(new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branchItems;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? 0;

        var nos = new KioskService().GetAll().Select(k => k.No).Distinct().ToList();
        if (!nos.Contains(Session.CurrentKioskNo)) nos.Add(Session.CurrentKioskNo);
        var kioskItems = new List<ComboItem> { new(0, "Tüm KIOSK'lar") };
        kioskItems.AddRange(nos.OrderBy(n => n).Select(n => new ComboItem(n, $"#{n}")));
        KioskCombo.ItemsSource = kioskItems;
        KioskCombo.SelectedValue = 0;

        ScopeCombo.ItemsSource = new[]
        {
            new ComboItem(0, "Bugün (anlık)"),
            new ComboItem(1, "Son Z'den beri"),
            new ComboItem(2, "Tarih aralığı")
        };
        ScopeCombo.SelectedValue = 0;

        FromBox.SelectedDate = DateTime.Today.AddDays(-1);
        ToBox.SelectedDate = DateTime.Today;

        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy  HH:mm:ss");
        _refresh.Tick += (_, _) => Load();

        Loaded += (_, _) =>
        {
            _ready = true;
            _clock.Start(); _refresh.Start();
            ClockText.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy  HH:mm:ss");
            StartPulse();
            Load();
        };
        Closed += (_, _) => { _clock.Stop(); _refresh.Stop(); };
    }

    private void StartPulse()
    {
        var anim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromSeconds(0.9))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        LiveDot.BeginAnimation(OpacityProperty, anim);
    }

    private void Load()
    {
        if (!_ready) return;
        try
        {
            int? branchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;
            int? kioskNo = KioskCombo.SelectedValue is int k && k > 0 ? k : null;
            int scope = ScopeCombo.SelectedValue is int s ? s : 0;

            DateTime from = DateTime.Today, to = DateTime.Today;
            DateTime? since = null;
            string info;
            if (scope == 1)
            {
                var lastZ = _reports.GetLastZTakenAt(branchId, kioskNo);
                if (lastZ is DateTime z)
                {
                    since = z;
                    info = $"Kapsam: Son Z'den beri ({z.ToLocalTime():dd.MM.yyyy HH:mm} sonrası)";
                }
                else { from = to = DateTime.Today; info = "Kapsam: Henüz Z alınmamış → bugünün tahsilatı gösteriliyor."; }
            }
            else if (scope == 2)
            {
                from = FromBox.SelectedDate ?? DateTime.Today;
                to = ToBox.SelectedDate ?? DateTime.Today;
                if (to < from) (from, to) = (to, from);
                info = $"Kapsam: {from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
            }
            else { from = to = DateTime.Today; info = "Kapsam: Bugün (anlık)"; }

            string scopeName = (BranchCombo.SelectedItem as ComboItem)?.Text + " · " + (KioskCombo.SelectedItem as ComboItem)?.Text;
            ScopeInfoText.Text = $"{scopeName}  ·  {info}";

            var snap = _reports.GetKioskMoney(branchId, kioskNo, from, to, since);
            NakitText.Text = $"{snap.Nakit:N2} ₺";
            KartText.Text = $"{snap.KrediKarti:N2} ₺";
            CekText.Text = $"{snap.YemekCeki:N2} ₺";
            AcikText.Text = $"{snap.AcikHesap:N2} ₺";
            TahsilatText.Text = $"{snap.Tahsilat:N2} ₺";
            FisCountText.Text = $"{snap.FisCount} fiş";
            GenelToplamText.Text = snap.AcikHesap != 0 ? $"Açık hesap dahil genel: {snap.Total:N2} ₺" : "";

            // Renklendirme: tahsilat durumuna göre toplam kart rengi
            TotalCard.Background = snap.Tahsilat <= 0
                ? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))            // gri — para yok
                : snap.Tahsilat < 1000
                    ? new SolidColorBrush(Color.FromRgb(0x0E, 0xA5, 0xE9))        // mavi — düşük
                    : new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));       // yeşil — yüksek

            LastRefreshText.Text = $"Son yenileme: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { LastRefreshText.Text = "Hata: " + ex.Message; }
    }

    private void Scope_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RangePanel != null)
            RangePanel.Visibility = ScopeCombo.SelectedValue is int s && s == 2 ? Visibility.Visible : Visibility.Collapsed;
        Load();
    }

    private void Filter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Load();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
