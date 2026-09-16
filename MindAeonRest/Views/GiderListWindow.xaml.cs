using System.Windows;
using System.Windows.Input;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class GiderListWindow : Window
{
    private readonly ExpenseService _service = new();

    public GiderListWindow()
    {
        InitializeComponent();
        DateFrom.SelectedDate = DateTime.Today.AddMonths(-1);
        DateTo.SelectedDate = DateTime.Today;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        var list = _service.GetList(DateFrom.SelectedDate, DateTo.SelectedDate);
        Grid.ItemsSource = list;
        TotalText.Text = $"{list.Count} gider · Toplam: {list.Sum(x => x.Amount):N2}";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var w = new GiderEditWindow { Owner = this };
        if (w.ShowDialog() == true) Load();
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
