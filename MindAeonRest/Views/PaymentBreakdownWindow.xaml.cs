using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Ödeme yöntemi kırılımı raporu: tarih aralığı (+şube) → tahsil edilen para, yönteme göre.
/// Kart-türevleri (banka kartı, temassız, QR, mobil, online) Kredi Kartı altında toplanır.</summary>
public partial class PaymentBreakdownWindow : Window
{
    private readonly ReportService _reports = new();

    public PaymentBreakdownWindow()
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;

        var branchItems = new List<ComboItem> { new(0, "Tüm Şubeler") };
        branchItems.AddRange(new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branchItems;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? 0;

        Loaded += (_, _) =>
        {
            FromBox.SelectedDate = DateTime.Today;
            ToBox.SelectedDate = DateTime.Today;
            Load();
        };
    }

    private void Load()
    {
        try
        {
            var from = FromBox.SelectedDate ?? DateTime.Today;
            var to = ToBox.SelectedDate ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);
            int? branchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;

            var snap = _reports.GetKioskMoney(branchId, null, from, to);

            var rows = new List<Row>
            {
                new("Nakit", snap.Nakit),
                new("Kredi Kartı", snap.KrediKarti),
                new("Yemek Çeki", snap.YemekCeki),
                new("Havale / EFT", snap.Havale),
                new("Hediye Çeki", snap.HediyeCeki),
                new("Puan / Sadakat", snap.PuanSadakat),
                new("Açık Hesap (Cari)", snap.AcikHesap),
            };
            Host.ItemsSource = rows;
            TotalText.Text = $"₺{snap.Total:N2}";
            TahsilatText.Text = $"₺{snap.Tahsilat:N2}";
            FisText.Text = $"Fiş adedi: {snap.FisCount}" + (snap.LastZAt is DateTime z ? $"   ·   Son Z: {z.ToLocalTime():dd.MM.yyyy HH:mm}" : "");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Ödeme Kırılımı"); }
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    private sealed record Row(string Name, decimal Amount)
    {
        public string AmountText => $"₺{Amount:N2}";
    }
}
