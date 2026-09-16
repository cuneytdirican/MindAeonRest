using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class NoteWindow : Window
{
    public string? ResultNote { get; private set; }

    public NoteWindow(int productId, string? current, string productName)
    {
        InitializeComponent();
        TitleText.Text = $"Açıklama / Not · {productName}";
        QuickHost.ItemsSource = new ProductService().GetDescriptions(productId).Where(d => d.IsActive).ToList();
        NoteBox.Text = current ?? "";
        Loaded += (_, _) => NoteBox.Focus();
    }

    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string text) return;
        var cur = NoteBox.Text.TrimEnd();
        if (cur.Length > 0 && !cur.EndsWith(",") && !cur.EndsWith(";")) cur += ", ";
        else if (cur.Length > 0) cur += " ";
        NoteBox.Text = cur + text;
        NoteBox.CaretIndex = NoteBox.Text.Length;
        NoteBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultNote = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) { NoteBox.Focus(); VirtualKeyboardWindow.ShowFor(NoteBox); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
