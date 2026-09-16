using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

public partial class ComboChoiceWindow : Window
{
    public int? SelectedValue { get; private set; }

    public ComboChoiceWindow(string title, string label, IEnumerable<ComboItem> items)
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
        Combo.ItemsSource = items.ToList();
        if (Combo.Items.Count > 0) Combo.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Combo.SelectedValue is not int v) { return; }
        SelectedValue = v;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
