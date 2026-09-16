using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

/// <summary>POS'ta ürün varyant/seçeneklerini sorar: her GROUP'tan tek seçim. Sonuç: fiyat farkı toplamı + not metni.</summary>
public partial class OptionPickerWindow : Window
{
    private readonly decimal _basePrice;
    private readonly Dictionary<string, ProductOption> _selected = new();
    private readonly Dictionary<string, List<(ProductOption opt, Button btn)>> _buttons = new();

    public decimal TotalDelta => _selected.Values.Sum(o => o.PriceDelta);
    public string NoteText => string.Join(", ", _selected.Select(kv => $"{kv.Key}: {kv.Value.Name}"));

    public OptionPickerWindow(string productName, decimal basePrice, IEnumerable<ProductOption> options)
    {
        InitializeComponent();
        _basePrice = basePrice;
        TitleText.Text = productName;

        foreach (var group in options.GroupBy(o => o.GroupName))
        {
            GroupsHost.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PosTextBrush"),
                Margin = new Thickness(0, 12, 0, 6)
            });
            var wrap = new WrapPanel();
            var pairs = new List<(ProductOption, Button)>();
            foreach (var opt in group)
            {
                var btn = new Button
                {
                    Content = opt.Display,
                    Style = (Style)FindResource("PosLineToolBtnBig"),
                    MinWidth = 120, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 0, 14, 0)
                };
                var captured = opt;
                btn.Click += (_, _) => Select(group.Key, captured);
                wrap.Children.Add(btn);
                pairs.Add((opt, btn));
            }
            _buttons[group.Key] = pairs;
            GroupsHost.Children.Add(wrap);

            // Varsayılan ya da ilk seçeneği seç
            var def = group.FirstOrDefault(o => o.IsDefault) ?? group.First();
            Select(group.Key, def);
        }
        UpdateTotal();
    }

    private void Select(string groupName, ProductOption opt)
    {
        _selected[groupName] = opt;
        var accent = (Brush)FindResource("PosAccentBrush");
        var idle = (Brush)FindResource("PosSubtleBrush");
        var idleText = (Brush)FindResource("PosTextBrush");
        foreach (var (o, btn) in _buttons[groupName])
        {
            bool sel = ReferenceEquals(o, opt);
            btn.Background = sel ? accent : idle;
            btn.Foreground = sel ? Brushes.White : idleText;
        }
        UpdateTotal();
    }

    private void UpdateTotal() => TotalText.Text = $"₺{_basePrice + TotalDelta:N2}";

    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
