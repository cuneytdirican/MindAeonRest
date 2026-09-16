using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PriceTransferWindow : Window
{
    public PriceTransferWindow()
    {
        InitializeComponent();
        if (!Session.IsSysAdmin) { Dialog.Warn("Fiyat aktarımı yalnızca yöneticiler içindir.", "Yetki"); Loaded += (_, __) => Close(); return; }
        var branches = Session.CurrentCompany is { } co
            ? new BranchService().GetByCompany(co.Id).Select(b => new ComboItem(b.Id, b.Name)).ToList()
            : new System.Collections.Generic.List<ComboItem>();
        SourceCombo.ItemsSource = branches;
        TargetCombo.ItemsSource = branches.ToList();
        if (branches.Count > 0) SourceCombo.SelectedIndex = 0;
        if (branches.Count > 1) TargetCombo.SelectedIndex = 1;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (SourceCombo.SelectedValue is not int src || TargetCombo.SelectedValue is not int tgt)
        { Err("Kaynak ve hedef şube seçin."); return; }
        if (src == tgt) { Err("Kaynak ve hedef şube aynı olamaz."); return; }
        if (!Dialog.Confirm("Hedef şubenin mevcut şube-özel fiyatları silinip kaynaktan kopyalanacak. Onaylıyor musunuz?", "Fiyat Aktarımı")) return;
        try
        {
            OkButton.IsEnabled = false;
            int n = new PriceTransferService().Transfer(src, tgt, Session.CurrentUserId);
            Dialog.Success($"{n} fiyat satırı aktarıldı.");
            DialogResult = true; Close();
        }
        catch (Exception ex) { Err(ex.Message); }
        finally { OkButton.IsEnabled = true; }
    }

    private void Err(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
