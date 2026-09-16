using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CampaignApplyWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly CampaignService _campaigns = new();
    private readonly int _orderId;
    private readonly int _branchId;

    /// <summary>Kampanya uygulandı/kaldırıldı → adisyon yeniden yüklensin.</summary>
    public bool Changed { get; private set; }

    public CampaignApplyWindow(int orderId, int branchId)
    {
        InitializeComponent();
        _orderId = orderId;
        _branchId = branchId;

        var active = _campaigns.GetActive(branchId, DateTime.Now);
        List.ItemsSource = active;
        EmptyText.Text = active.Count == 0 ? "Şu an geçerli kampanya yok." : $"{active.Count} geçerli kampanya.";

        // Uygulanmış kampanya(lar) — birleştirilebilir kampanyalarla birden çok olabilir
        var appliedIds = _orders.GetAppliedCampaignIds(orderId);
        if (appliedIds.Count > 0)
        {
            var names = appliedIds.Select(id => _campaigns.GetById(id)).Where(x => x != null)
                .Select(x => $"{x!.Name} ({x.ValueText})").ToList();
            AppliedPanel.Visibility = Visibility.Visible;
            AppliedText.Text = string.Join("  +  ", names);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Campaign c) return;
        if (!Dialog.Confirm($"'{c.Name}' kampanyası uygulanacak.\nEtkilenen satırların iskontosu kampanyaya göre ayarlanır.\n\nDevam edilsin mi?", "Kampanya Uygula")) return;
        try { _orders.ApplyCampaign(_orderId, c.Id, _branchId, Session.CurrentUserId); Changed = true; DialogResult = true; Close(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Kampanya"); }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm("Uygulanan kampanya kaldırılsın mı?\n(Etkilenen satırların iskontosu sıfırlanır.)", "Kampanya Kaldır")) return;
        try { _orders.RemoveCampaign(_orderId, Session.CurrentUserId); Changed = true; DialogResult = true; Close(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Kampanya"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = Changed; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
