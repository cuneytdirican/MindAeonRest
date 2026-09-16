using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class OrderDetailWindow : Window
{
    private readonly OrderService _orders = new();

    public OrderDetailWindow(int orderId)
    {
        InitializeComponent();
        var o = _orders.GetById(orderId);
        if (o is null) return;
        TitleText.Text = $"Sipariş · {o.Code}";
        if (_orders.IsZLocked(orderId)) LockBadge.Visibility = Visibility.Visible;

        Info("Masa", o.TableCodes);
        Info("Müşteri", o.CustomerName);
        Info("Açılış", o.OpenedAt.ToString("dd.MM.yyyy HH:mm"));
        Info("Kapanış", o.ClosedAt?.ToString("dd.MM.yyyy HH:mm") ?? "-");
        Info("Durum", o.StatusText);
        Info("Toplam", o.TotalText);

        LinesGrid.ItemsSource = _orders.GetAllLines(orderId);
        FichesGrid.ItemsSource = _orders.GetOrderFiches(orderId);
        if (FichesGrid.Items.Count > 0) FichesGrid.SelectedIndex = 0;
    }

    private void Info(string k, string? v)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
        sp.Children.Add(new TextBlock { Text = k.ToUpper(), FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush") });
        sp.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(v) ? "-" : v, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        InfoPanel.Children.Add(sp);
    }

    private void Fiche_Changed(object sender, SelectionChangedEventArgs e)
    {
        PaymentsGrid.ItemsSource = FichesGrid.SelectedItem is ClFiche cf ? _orders.GetPaymentLines(cf.Id) : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
