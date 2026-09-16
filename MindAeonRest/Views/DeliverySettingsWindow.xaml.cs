using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class DeliverySettingsWindow : Window
{
    private readonly DeliveryPlatformService _service = new();
    private readonly BranchService _branchService = new();
    private List<DeliveryPlatformSetting> _rows = new();

    public DeliverySettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            int companyId = Session.CurrentCompany?.Id ?? 0;
            BranchCombo.ItemsSource = _branchService.GetByCompany(companyId);
            BranchCombo.SelectedValue = Session.CurrentBranch?.Id;
            if (BranchCombo.SelectedItem is null && BranchCombo.Items.Count > 0) BranchCombo.SelectedIndex = 0;
        };
    }

    private int BranchId => (BranchCombo.SelectedValue as int?) ?? 0;

    private void Branch_Changed(object sender, SelectionChangedEventArgs e) => Load();

    private void Load()
    {
        if (BranchId == 0) return;
        _rows = _service.GetByBranch(BranchId);
        Grid.ItemsSource = _rows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            foreach (var row in _rows) _service.Save(row, Session.CurrentUserId);
            Dialog.Info("Å?ube paket/yemek sitesi ayarlarÄ± kaydedildi.", "Bilgi");
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
