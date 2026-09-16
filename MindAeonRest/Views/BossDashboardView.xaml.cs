using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class BossDashboardView : UserControl
{
    private readonly BossDashboardService _svc = new();
    private bool _ready;
    private readonly DispatcherTimer _autoTimer = new();

    public BossDashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!_ready) { Init(); _ready = true; Refresh(); } };
        _autoTimer.Tick += (_, _) => { if (IsVisible) Refresh(); };   // yalnız açık/görünürken yenile
        Unloaded += (_, _) => _autoTimer.Stop();
    }

    private void Init()
    {
        var items = new List<ComboItem>();
        bool admin = Session.IsSysAdmin;
        if (admin) items.Add(new ComboItem(0, "🏢 Tüm Şubeler"));
        var co = Session.CurrentCompany;
        if (co != null)
            foreach (var b in new BranchService().GetByCompany(co.Id))
                items.Add(new ComboItem(b.Id, b.Name));
        BranchCombo.ItemsSource = items;
        BranchCombo.SelectedValue = admin ? 0 : (Session.CurrentBranch?.Id ?? items.FirstOrDefault()?.Value ?? 0);

        FromDate.SelectedDate = DateTime.Today;
        ToDate.SelectedDate = DateTime.Today;

        // Oto-yenile aralığı: varsayılan 1 dk (açıkken)
        AutoRefreshCombo.ItemsSource = new List<ComboItem>
        {
            new(1, "1 dakika"), new(5, "5 dakika"), new(10, "10 dakika"), new(0, "Kapalı")
        };
        AutoRefreshCombo.SelectedValue = 1;
        ApplyAutoRefresh();
    }

    private void ApplyAutoRefresh()
    {
        _autoTimer.Stop();
        int min = AutoRefreshCombo.SelectedValue is int m ? m : 1;
        if (min > 0)
        {
            _autoTimer.Interval = TimeSpan.FromMinutes(min);
            _autoTimer.Start();
            AutoRefreshInfo.Text = $"her {min} dk · son: {DateTime.Now:HH:mm:ss}";
        }
        else AutoRefreshInfo.Text = "kapalı";
    }

    private void AutoRefresh_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) ApplyAutoRefresh(); }

    public void Refresh()
    {
        if (!_ready) return;
        try
        {
            var from = FromDate.SelectedDate ?? DateTime.Today;
            var to = ToDate.SelectedDate ?? DateTime.Today;
            int branch = BranchCombo.SelectedValue as int? ?? 0;
            var d = _svc.Get(from, to, branch);

            RevenueText.Text = d.RevenueText;
            FisText.Text = d.FisCount.ToString();
            AvgText.Text = d.AvgText;
            DiscText.Text = d.DiscountText;
            RefundText.Text = d.RefundText;
            CancelText.Text = d.CancelCount.ToString();
            OpenText.Text = d.OpenTables.ToString();
            NakitText.Text = d.NakitText;
            KartText.Text = d.KartText;
            DigerText.Text = d.DigerText;

            // Yükümlülükler (parasal açık takibi) — dolaşımdaki hediye çeki + toplam puan (₺)
            try { GiftLiabText.Text = $"₺{new GiftCardService().GetTotalLiability():N2}"; } catch { GiftLiabText.Text = "₺0,00"; }
            try { LoyaltyLiabText.Text = $"₺{new LoyaltyService().GetTotalLiability():N2}"; } catch { LoyaltyLiabText.Text = "₺0,00"; }

            // Menü mühendisliği özeti (katkı marjı + sınıf dağılımı) — FAZ 3a
            try
            {
                var me = new MenuEngineeringService().GetMenuEngineering(from, to, branch);
                MenuMarginText.Text = $"₺{me.TotalMargin:N2}";
                MenuClassText.Text = $"{me.StarCount} / {me.PlowhorseCount} / {me.PuzzleCount} / {me.DogCount}";
            }
            catch { MenuMarginText.Text = "₺0,00"; MenuClassText.Text = "-"; }

            BranchHost.ItemsSource = d.Branches;
            BranchCard.Visibility = d.Branches.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            HourHost.ItemsSource = d.Hourly.Where(h => h.Value > 0).ToList();
            DailyHost.ItemsSource = d.Daily;
            TopProductsHost.ItemsSource = d.TopProducts;
            TopTablesHost.ItemsSource = d.TopTables;
            TopZonesHost.ItemsSource = d.TopZones;
            CancelledHost.ItemsSource = d.Cancelled;
            RefundedHost.ItemsSource = d.Refunded;
            WaitersHost.ItemsSource = d.Waiters;
            CashiersHost.ItemsSource = d.Cashiers;
            CampaignSalesHost.ItemsSource = d.CampaignSales;
            ActiveCampaignsHost.ItemsSource = d.ActiveCampaigns;
            NoCampaignText.Visibility = d.ActiveCampaigns.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            if (AutoRefreshCombo?.SelectedValue is int min && min > 0)
                AutoRefreshInfo.Text = $"her {min} dk · son: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Dashboard"); }
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Today_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = DateTime.Today; ToDate.SelectedDate = DateTime.Today; Refresh(); }
    private void Week_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = DateTime.Today.AddDays(-6); ToDate.SelectedDate = DateTime.Today; Refresh(); }
    private void Month_Click(object sender, RoutedEventArgs e) { FromDate.SelectedDate = DateTime.Today.AddDays(-29); ToDate.SelectedDate = DateTime.Today; Refresh(); }
    private void Branch_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
}

/// <summary>"#RRGGBB" -> Brush (dashboard çubuk renkleri).</summary>
public class StringToBrushConverter : IValueConverter
{
    private static readonly BrushConverter Bc = new();
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        try { return value is string s && s.Length > 0 ? (Brush)Bc.ConvertFromString(s)! : Brushes.Gray; }
        catch { return Brushes.Gray; }
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
