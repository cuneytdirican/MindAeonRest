using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CampaignWindow : Window
{
    private readonly CampaignService _service = new();

    public CampaignWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        var list = _service.GetAll(includeDeleted: false);
        Grid.ItemsSource = list;
        InfoText.Text = $"{list.Count} kampanya.";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (new CampaignEditWindow { Owner = this }.ShowDialog() == true) Load();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Campaign sel) { Dialog.Info("Önce bir kampanya seçin.", "Kampanya"); return; }
        var full = _service.GetById(sel.Id);
        if (full is null) return;
        if (new CampaignEditWindow(full) { Owner = this }.ShowDialog() == true) Load();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Campaign sel) { Dialog.Info("Önce bir kampanya seçin.", "Kampanya"); return; }
        _service.SetActive(sel.Id, !sel.IsActive, Session.CurrentUserId);
        Load();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Campaign sel) { Dialog.Info("Önce bir kampanya seçin.", "Kampanya"); return; }
        if (!Dialog.Confirm($"'{sel.Name}' kampanyası silinsin mi?", "Kampanya Sil")) return;
        _service.Delete(sel.Id, Session.CurrentUserId);
        Load();
    }

    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
