using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class TaxGroupEditWindow : Window
{
    private readonly TaxGroupService _service = new();
    private readonly TaxGroup _group;
    private readonly ObservableCollection<TaxGroupRate> _rates = new();

    /// <summary>Kaydedilen grubun Id'si (EntityPicker "+" için).</summary>
    public int? SavedGroupId => _group.Id == 0 ? null : _group.Id;

    public TaxGroupEditWindow(TaxGroup? existing = null)
    {
        InitializeComponent();
        _group = existing ?? new TaxGroup();
        RatesGrid.ItemsSource = _rates;

        if (existing is not null)
        {
            TitleText.Text = $"Vergi Grubu · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
            foreach (var r in _service.GetRates(existing.Id)) _rates.Add(r);
        }

        Loaded += (_, _) => CodeBox.Focus();
    }

    private void AddRate_Click(object sender, RoutedEventArgs e)
    {
        var w = new TaxRateEditWindow { Owner = this };
        if (w.ShowDialog() == true && w.Result is not null)
            _rates.Add(w.Result);
    }

    private void DeleteRate_Click(object sender, RoutedEventArgs e)
    {
        if (RatesGrid.SelectedItem is TaxGroupRate r) _rates.Remove(r);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Grup adını giriniz."); return; }

        _group.Code = CodeBox.Text;
        _group.Name = NameBox.Text.Trim();
        _group.IsActive = ActiveCheck.IsChecked == true;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_group, _rates.ToList(), Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
