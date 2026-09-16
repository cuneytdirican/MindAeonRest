using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class DocumentConvertWindow : Window
{
    private readonly DocumentService _docs = new();
    private readonly DocType _target;
    private readonly DocType _source;

    public List<int> SelectedIds { get; } = new();

    public DocumentConvertWindow(DocType target, DocType source)
    {
        InitializeComponent();
        _target = target;
        _source = source;
        TitleText.Text = $"{source.ToTr()} → {target.ToTr()}";
        SubText.Text = $"{source.ToTr()} seçip {target.ToTr()} oluşturun";
        // Sadece onaylı + henüz bu hedefe dönüştürülmemiş kaynaklar
        var converted = _docs.GetConvertedSourceIds(target);
        var list = _docs.GetList(source).Where(d => d.Status == DocStatus.Onaylandi && !converted.Contains(d.Id)).ToList();
        Grid.ItemsSource = list;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedIds.Clear();
        foreach (var item in Grid.SelectedItems) if (item is Document d) SelectedIds.Add(d.Id);
        if (SelectedIds.Count == 0) { Dialog.Info("En az bir kaynak seçin.", "Bilgi"); return; }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
