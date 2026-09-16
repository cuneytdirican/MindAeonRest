using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindAeonRest.Views;

/// <summary>Dokunmatik sanal klavye (TR). Odağı çalmaz; verilen hedef TextBox/PasswordBox'a yazar.
/// Gerçek klavyeye gerek kalmadan giriş yapılır.</summary>
public partial class VirtualKeyboardWindow : Window
{
    private static VirtualKeyboardWindow? _instance;
    private readonly Control _target;
    private Control _current = null!;
    private bool _shift;

    private static readonly string[] Rows =
    {
        "1 2 3 4 5 6 7 8 9 0",
        "q w e r t y u ı o p ğ ü",
        "a s d f g h j k l ş i",
        "z x c v b n m ö ç ,"
    };

    public VirtualKeyboardWindow(Control target)
    {
        InitializeComponent();
        _target = target;
        Build();
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 30;
            Top = area.Bottom - ActualHeight - 20;
        };
    }

    /// <summary>Hedef alan için klavyeyi açar (tekil). Hedefin penceresine SAHİP olarak açılır —
    /// modal diyaloglarda (ShowDialog) devre dışı kalmaması için sahip değişince yeniden yaratılır.</summary>
    public static void ShowFor(Control target)
    {
        var owner = Window.GetWindow(target);
        if (_instance is { IsLoaded: true })
        {
            if (ReferenceEquals(_instance.Owner, owner))
            {
                _instance.Retarget(target);
                _instance.Activate();
                return;
            }
            // Sahip (modal bağlam) değişti → kapat, doğru sahiple yeniden aç
            _instance.Close();
            _instance = null;
        }
        _instance = new VirtualKeyboardWindow(target);
        _instance.Closed += (_, _) => _instance = null;
        if (owner != null) _instance.Owner = owner;
        _instance.Show();
    }

    private void Retarget(Control t) => _current = t;

    private void Build()
    {
        _current = _target;
        var fg = (Brush)FindResource("TextBrush");
        var bg = (Brush)FindResource("SurfaceAltBrush");
        Button Key(string text, double w, Action onClick, Brush? back = null)
        {
            var b = new Button
            {
                Content = text, Width = w, Height = 48, Margin = new Thickness(3),
                FontSize = 18, Focusable = false, Cursor = Cursors.Hand,
                Background = back ?? bg, Foreground = fg, BorderThickness = new Thickness(0)
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        foreach (var row in Rows)
        {
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var ch in row.Split(' '))
            {
                string c = ch;
                wrap.Children.Add(Key(c, 48, () => Insert(_shift ? Up(c) : c)));
            }
            KeysHost.Children.Add(wrap);
        }

        var ctrl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var accent = (Brush)FindResource("AccentBrush");
        ctrl.Children.Add(Key("⇧ Shift", 110, () => { _shift = !_shift; }, accent));
        ctrl.Children.Add(Key("Boşluk", 240, () => Insert(" ")));
        ctrl.Children.Add(Key("⌫", 90, Backspace));
        ctrl.Children.Add(Key("Temizle", 110, ClearTarget));
        ctrl.Children.Add(Key("Tamam", 110, Close_Ok, accent));
        KeysHost.Children.Add(ctrl);
    }

    private static string Up(string c) => c.ToUpper(new System.Globalization.CultureInfo("tr-TR"));

    private void Insert(string s)
    {
        if (_current is TextBox tb)
        {
            int i = tb.SelectionStart;
            tb.Text = tb.Text.Remove(i, tb.SelectionLength).Insert(i, s);
            tb.SelectionStart = i + s.Length;
            tb.SelectionLength = 0;
        }
        else if (_current is PasswordBox pb) pb.Password += s;
    }

    private void Backspace()
    {
        if (_current is TextBox tb)
        {
            if (tb.SelectionLength > 0) { int i = tb.SelectionStart; tb.Text = tb.Text.Remove(i, tb.SelectionLength); tb.SelectionStart = i; }
            else if (tb.SelectionStart > 0) { int i = tb.SelectionStart; tb.Text = tb.Text.Remove(i - 1, 1); tb.SelectionStart = i - 1; }
        }
        else if (_current is PasswordBox pb && pb.Password.Length > 0) pb.Password = pb.Password[..^1];
    }

    private void ClearTarget()
    {
        if (_current is TextBox tb) tb.Clear();
        else if (_current is PasswordBox pb) pb.Clear();
    }

    private void Close_Ok() => Close();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
