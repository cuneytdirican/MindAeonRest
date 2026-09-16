using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CodeSeriesEditWindow : Window
{
    private readonly CodeSeriesService _service = new();

    public CodeSeriesEditWindow(CodeSeries? existing = null)
    {
        InitializeComponent();
        TypeCombo.ItemsSource = Enum.GetValues<CodeSeriesType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();

        var branches = new List<ComboItem> { new(0, "(Tüm şubeler — varsayılan)") };
        if (Session.CurrentCompany is { } co)
            branches.AddRange(new BranchService().GetByCompany(co.Id).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branches;
        BranchCombo.SelectedValue = existing?.BranchId ?? 0;

        if (existing is not null)
        {
            TypeCombo.SelectedValue = (int)existing.EntityType;
            PrefixBox.Text = existing.Prefix;
            PaddingBox.Text = existing.Padding.ToString();
            NextBox.Text = existing.NextNumber.ToString();
            ActiveCheck.IsChecked = existing.IsActive;
        }
        else
        {
            TypeCombo.SelectedIndex = 0;
        }
        UpdatePreview();
        Loaded += (_, _) => PrefixBox.Focus();
    }

    private void Field_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdatePreview();
    private void Field_Changed2(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText is null) return;
        int pad = int.TryParse(PaddingBox.Text, out var p) ? Math.Max(1, p) : 6;
        int next = int.TryParse(NextBox.Text, out var n) ? Math.Max(1, n) : 1;
        PreviewText.Text = $"{PrefixBox.Text}{next.ToString().PadLeft(pad, '0')}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (TypeCombo.SelectedValue is not int type) { ShowError("Kart tipi seçiniz."); return; }
        if (!int.TryParse(PaddingBox.Text.Trim(), out var pad) || pad < 1) { ShowError("Basamak en az 1 olmalı."); return; }
        if (!int.TryParse(NextBox.Text.Trim(), out var next) || next < 1) { ShowError("Başlangıç no en az 1 olmalı."); return; }

        try
        {
            _service.Save(new CodeSeries
            {
                EntityType = (CodeSeriesType)type,
                BranchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null,
                Prefix = PrefixBox.Text.Trim(),
                Padding = pad,
                NextNumber = next,
                IsActive = ActiveCheck.IsChecked == true
            }, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
