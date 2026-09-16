using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReservationTagEditWindow : Window
{
    public record ColorOption(string Hex, string Name);

    private static readonly List<ColorOption> Palette = new()
    {
        new("#E91E63", "Pembe"), new("#3F51B5", "Lacivert"), new("#FF9800", "Turuncu"),
        new("#9C27B0", "Mor"), new("#4CAF50", "Yeşil"), new("#F44336", "Kırmızı"),
        new("#00BCD4", "Turkuaz"), new("#FFC107", "Sarı"), new("#607D8B", "Gri")
    };

    private readonly ReservationTagService _service = new();
    private readonly ReservationTag _tag;

    public int? SavedId => _tag.Id == 0 ? null : _tag.Id;

    public ReservationTagEditWindow(ReservationTag? existing = null)
    {
        InitializeComponent();
        _tag = existing ?? new ReservationTag();
        ColorCombo.ItemsSource = Palette;
        ColorCombo.SelectedValue = existing?.Color ?? "#E91E63";

        if (existing is not null)
        {
            TitleText.Text = $"Etiket · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Etiket adını giriniz."); return; }

        _tag.Code = CodeBox.Text;
        _tag.Name = NameBox.Text.Trim();
        _tag.Color = ColorCombo.SelectedValue as string;
        _tag.IsActive = ActiveCheck.IsChecked == true;
        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_tag, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
