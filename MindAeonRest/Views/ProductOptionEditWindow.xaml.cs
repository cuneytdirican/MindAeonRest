using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

public partial class ProductOptionEditWindow : Window
{
    public ProductOption? Result { get; private set; }

    public ProductOptionEditWindow(ProductOption? existing = null)
    {
        InitializeComponent();
        if (existing is not null)
        {
            GroupBox.Text = existing.GroupName;
            NameBox.Text = existing.Name;
            DeltaBox.Text = existing.PriceDelta.ToString("0.##", CultureInfo.InvariantCulture);
            DefaultCheck.IsChecked = existing.IsDefault;
            Result = existing;
        }
        Loaded += (_, _) => GroupBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string grp = GroupBox.Text.Trim(), name = NameBox.Text.Trim();
        if (grp.Length == 0) { Err("Grup adı giriniz (ör. Boyut)."); return; }
        if (name.Length == 0) { Err("Seçenek adı giriniz (ör. Büyük)."); return; }
        if (!decimal.TryParse(DeltaBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var delta))
        { Err("Fiyat farkı geçerli bir sayı olmalı (0 olabilir)."); return; }

        Result = new ProductOption
        {
            Id = Result?.Id ?? 0,
            GroupName = grp,
            Name = name,
            PriceDelta = delta,
            IsDefault = DefaultCheck.IsChecked == true,
            SortOrder = Result?.SortOrder ?? 0
        };
        DialogResult = true;
    }

    private void Err(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
