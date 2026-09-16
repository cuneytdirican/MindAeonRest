using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;

namespace MindAeonRest.Views;

public partial class KasaIslemWindow : Window
{
    private readonly bool _transfer;

    public decimal Amount { get; private set; }
    public string? Note { get; private set; }
    public int? TargetId { get; private set; }

    public KasaIslemWindow(string title, bool transfer, IEnumerable<ComboItem>? targets = null)
    {
        InitializeComponent();
        _transfer = transfer;
        TitleText.Text = title;
        if (transfer)
        {
            TargetPanel.Visibility = Visibility.Visible;
            var list = targets?.ToList() ?? new List<ComboItem>();
            TargetCombo.ItemsSource = list;
            if (list.Count > 0) TargetCombo.SelectedIndex = 0;
        }
        Loaded += (_, _) => AmountBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
        { ShowError("Geçerli bir tutar girin."); return; }
        if (_transfer && TargetCombo.SelectedValue is not int) { ShowError("Hedef kasa seçin."); return; }

        Amount = amt;
        Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        TargetId = _transfer ? TargetCombo.SelectedValue as int? : null;
        DialogResult = true;
        Close();
    }

    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
