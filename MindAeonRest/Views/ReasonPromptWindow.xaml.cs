using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MindAeonRest.Views;

public partial class ReasonPromptWindow : Window
{
    public string Reason { get; private set; } = "";

    private static readonly string[] Presets =
    {
        "Müşteri vazgeçti", "Yanlış giriş", "Stok yok", "Mutfak hatası", "Geç kaldı", "Fiyat itirazı"
    };

    public ReasonPromptWindow(string title = "İptal Sebebi", string label = "SEBEP SEÇİN VEYA YAZIN")
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
        foreach (var p in Presets)
        {
            var b = new Button { Content = p, Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
            b.Click += (_, _) => { ReasonBox.Text = p; ReasonBox.Focus(); ReasonBox.CaretIndex = p.Length; };
            ChipPanel.Children.Add(b);
        }
        Loaded += (_, _) => { ReasonBox.Focus(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Reason = ReasonBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) { ReasonBox.Focus(); VirtualKeyboardWindow.ShowFor(ReasonBox); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
