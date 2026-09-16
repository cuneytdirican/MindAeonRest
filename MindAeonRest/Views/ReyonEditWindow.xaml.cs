using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReyonEditWindow : Window
{
    private const string NoPrinter = "(yazıcı yok - sadece ekran)";
    private readonly ReyonService _service = new();
    private readonly ZoneService _zoneService = new();
    private readonly Reyon _reyon;

    public ReyonEditWindow(Reyon? existing = null)
    {
        InitializeComponent();
        _reyon = existing ?? new Reyon();
        ZonePicker.Configure(() => _zoneService.GetAll(), AddZone, existing?.ZoneId);

        // Yazıcı listesi
        var printers = new List<string> { NoPrinter };
        printers.AddRange(PrinterHelper.GetInstalled());
        if (!string.IsNullOrWhiteSpace(existing?.PrinterName) && !printers.Contains(existing!.PrinterName!))
            printers.Add(existing.PrinterName!);   // kaldırılmış olsa da göster
        PrinterCombo.ItemsSource = printers;
        PrinterCombo.SelectedItem = string.IsNullOrWhiteSpace(existing?.PrinterName) ? NoPrinter : existing!.PrinterName;

        if (existing is not null)
        {
            TitleText.Text = $"Reyon · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
        }
        StationCombo.SelectedIndex = (int)((existing?.StationType) ?? StationType.Mutfak);
        Loaded += (_, _) => CodeBox.Focus();
    }

    private int? AddZone()
    {
        var w = new ZoneEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Reyon adını giriniz."); return; }
        if (ZonePicker.SelectedValue is not int zoneId) { ShowError("Lokasyon (zone) seçiniz."); return; }

        string? printer = PrinterCombo.SelectedItem as string;
        if (printer == NoPrinter) printer = null;

        _reyon.Code = CodeBox.Text;
        _reyon.Name = NameBox.Text.Trim();
        _reyon.ZoneId = zoneId;
        _reyon.PrinterName = printer;
        _reyon.StationType = (StationType)Math.Max(0, StationCombo.SelectedIndex);
        _reyon.IsActive = ActiveCheck.IsChecked == true;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_reyon, Session.CurrentUserId);
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
