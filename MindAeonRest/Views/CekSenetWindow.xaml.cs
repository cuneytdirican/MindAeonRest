using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CekSenetWindow : Window
{
    private readonly CheckNoteService _service = new();

    public CekSenetWindow()
    {
        InitializeComponent();
        DirFilter.ItemsSource = new List<ComboItem> { new(-1, "Tümü"), new((int)CheckDirection.Alinan, "Alınan"), new((int)CheckDirection.Verilen, "Verilen") };
        DirFilter.SelectedIndex = 0;
        StatusCombo.ItemsSource = Enum.GetValues<CheckStatus>().Select(s => new ComboItem((int)s, s.ToTr())).ToList();
        StatusCombo.SelectedIndex = 0;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        CheckDirection? dir = DirFilter.SelectedValue is int v && v >= 0 ? (CheckDirection)v : null;
        Grid.ItemsSource = _service.GetAll(dir);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var w = new CekSenetEditWindow { Owner = this };
        if (w.ShowDialog() == true) Load();
    }

    private void ApplyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not CheckNote n) { Dialog.Info("Önce bir kayıt seçin.", "Bilgi"); return; }
        if (StatusCombo.SelectedValue is not int s) return;
        var status = (CheckStatus)s;
        if (status == CheckStatus.Karsiliksiz && !Dialog.Confirm($"{n.KindText} karşılıksız olarak işaretlenecek ve cari hareketi ters çevrilecek. Onaylıyor musunuz?", "Karşılıksız")) return;
        try { _service.ChangeStatus(n.Id, status, Session.CurrentUserId); Load(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void DirFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Load(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
