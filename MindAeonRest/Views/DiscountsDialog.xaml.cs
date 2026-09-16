using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MindAeonRest.Views;

public partial class DiscountsDialog : Window
{
    private readonly TextBox[] _boxes = new TextBox[10];
    public decimal[] Values { get; private set; } = new decimal[10];

    public DiscountsDialog(decimal[] current)
    {
        InitializeComponent();
        for (int i = 0; i < 10; i++)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
            panel.Children.Add(new TextBlock { Text = $"İskonto {i + 1} %", FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"), Margin = new Thickness(2, 0, 0, 4) });
            var tb = new TextBox { Style = (Style)FindResource("InputTextBox"), Text = (i < current.Length ? current[i] : 0).ToString("0.##", CultureInfo.InvariantCulture) };
            _boxes[i] = tb;
            panel.Children.Add(tb);
            Host.Children.Add(panel);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 10; i++)
            Values[i] = decimal.TryParse(_boxes[i].Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
