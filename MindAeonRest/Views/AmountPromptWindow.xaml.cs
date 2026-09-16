using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

public partial class AmountPromptWindow : Window
{
    public decimal Amount { get; private set; }

    public AmountPromptWindow(string title, string label, decimal initial = 0)
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
        AmountBox.Text = initial.ToString("0.##", CultureInfo.InvariantCulture);
        Loaded += (_, _) => { AmountBox.Focus(); AmountBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) || v < 0)
        { ErrorText.Text = "Geçerli bir tutar girin."; ErrorText.Visibility = Visibility.Visible; return; }
        Amount = v;
        DialogResult = true;
        Close();
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) { AmountBox.Focus(); VirtualKeyboardWindow.ShowFor(AmountBox); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
