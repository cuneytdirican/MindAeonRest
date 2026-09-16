using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class OrderListWindow : Window
{
    private readonly OrderService _orders = new();
    private readonly int _branchId;

    public OrderListWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        StatusCombo.ItemsSource = new List<ComboItem> { new(-1, "Tümü"), new(0, "Açık"), new(1, "Kapandı"), new(9, "İptal") };
        StatusCombo.SelectedIndex = 0;
        DateFrom.SelectedDate = DateTime.Today.AddDays(-7);
        DateTo.SelectedDate = DateTime.Today;
        Loaded += (_, _) => Load();
    }

    private Order? Selected => Grid.SelectedItem as Order;

    private void Load()
    {
        var list = _orders.GetOrders(_branchId, DateFrom.SelectedDate ?? DateTime.Today, DateTo.SelectedDate ?? DateTime.Today);
        if (StatusCombo.SelectedValue is int s && s >= 0)
            list = list.Where(o => (int)o.Status == s).ToList();
        Grid.ItemsSource = list;
        CountText.Text = $"{list.Count} sipariş";
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } o) { Dialog.Info("Bir sipariş seçin.", "Bilgi"); return; }
        new OrderDetailWindow(o.Id) { Owner = this }.ShowDialog();
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
