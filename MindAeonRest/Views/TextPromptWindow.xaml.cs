using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

/// <summary>Basit metin girişi + hızlı chip ön-tanımları (alerji/not). Value boşsa "Kaldır" temizler.</summary>
public partial class TextPromptWindow : Window
{
    public string? Value { get; private set; }

    public TextPromptWindow(string title, string? placeholder = null, string? initial = null, string[]? chips = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        InputBox.Text = initial ?? "";
        if (placeholder is not null) InputBox.Tag = placeholder;
        if (chips is { Length: > 0 }) ChipHost.ItemsSource = chips;
        Loaded += (_, _) => InputBox.Focus();
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string s) InputBox.Text = s;
    }

    private void Save_Click(object sender, RoutedEventArgs e) { Value = InputBox.Text; DialogResult = true; Close(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { Value = ""; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_Drag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
