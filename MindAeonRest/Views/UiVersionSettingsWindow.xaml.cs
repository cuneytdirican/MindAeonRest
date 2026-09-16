using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class UiVersionSettingsWindow : Window
{
    public UiVersionSettingsWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("Ekran sürümü ayarı yalnızca yöneticiler içindir.", "Ekran Sürümü"); Loaded += (_, __) => Close(); return; }
        Load();
    }

    private void Load()
    {
        var s = new CompanyUiSettingsService().Get();
        SelectByTag(AdisyonCombo, s.AdisyonVersion);
        SelectByTag(MutfakCombo, s.MutfakVersion);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = new CompanyUiSettings
            {
                AdisyonVersion = TagOf(AdisyonCombo),
                MutfakVersion = TagOf(MutfakCombo)
            };
            new CompanyUiSettingsService().Save(s, Session.CurrentUserId);
            Dialog.Success("Ekran sürümü kaydedildi. Ekranlar bir sonraki açılışta yeni sürümle gelir.", "Ekran Sürümü");
            DialogResult = true;
            Close();
        }
        catch (System.Exception ex) { Dialog.Error("Kaydedilemedi: " + ex.Message, "Ekran Sürümü"); }
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        var want = UiVersions.Normalize(tag);
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, want, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;   // Varsayılan
    }

    private static string TagOf(ComboBox combo)
        => UiVersions.Normalize((combo.SelectedItem as ComboBoxItem)?.Tag as string);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
