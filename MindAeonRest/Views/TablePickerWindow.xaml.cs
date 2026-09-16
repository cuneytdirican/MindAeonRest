using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

/// <summary>Basit masa seçici dialog (boş/dolu filtreli liste). Garson Terminali V2 masa işlemleri (Taşı/Böl/Birleştir/Kişi Aktar) için.</summary>
public partial class TablePickerWindow : Window
{
    /// <summary>Seçilen masa.</summary>
    public TablePickItem? Selected { get; private set; }

    public TablePickerWindow(string title, string subtitle, IEnumerable<TablePickItem> items, string emptyMessage)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = subtitle;
        var list = new List<TablePickItem>(items);
        TableHost.ItemsSource = list;
        if (list.Count == 0)
        {
            EmptyText.Text = emptyMessage;
            EmptyText.Visibility = Visibility.Visible;
        }
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TablePickItem it) return;
        Selected = it;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

/// <summary>Masa seçici satırı. Boş masada OrderId null; dolu masada hedef adisyon id'si.</summary>
public class TablePickItem
{
    public int TableId { get; set; }
    public int? OrderId { get; set; }
    public string ZoneText { get; set; } = "";
    public string TableName { get; set; } = "";
    public string SubText { get; set; } = "";
    public string BorderCol { get; set; } = "#22808080";
    public bool HasSub => !string.IsNullOrWhiteSpace(SubText);
}
