using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Views;

public partial class TaxRateEditWindow : Window
{
    public TaxGroupRate? Result { get; private set; }

    public TaxRateEditWindow()
    {
        InitializeComponent();
        TypeCombo.ItemsSource = Enum.GetValues<VatType>()
            .Select(v => new VatTypeOption((int)v, v.ToTr())).ToList();
        TypeCombo.SelectedIndex = 1; // Satış varsayılan
        int year = DateTime.Today.Year;
        StartBox.Text = DateHelper.ToText(new DateTime(year, 1, 1));   // yılın ilk günü
        EndBox.Text = DateHelper.ToText(new DateTime(year, 12, 31));   // yılın son günü
        Loaded += (_, _) => RateBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (TypeCombo.SelectedValue is not int type) { ShowError("Tip seçiniz."); return; }
        if (!decimal.TryParse(RateBox.Text.Trim().Replace('.', ','), NumberStyles.Any, new CultureInfo("tr-TR"), out var rate) || rate < 0)
        {
            ShowError("Geçerli bir oran giriniz (ör. 20).");
            return;
        }
        if (!DateHelper.TryParse(StartBox.Text, out var start))
        {
            ShowError("Başlangıç tarihi gg.aa.yyyy biçiminde olmalı.");
            return;
        }
        DateTime? end = null;
        if (!string.IsNullOrWhiteSpace(EndBox.Text))
        {
            if (!DateHelper.TryParse(EndBox.Text, out var e2)) { ShowError("Bitiş tarihi gg.aa.yyyy biçiminde olmalı."); return; }
            if (e2 < start) { ShowError("Bitiş tarihi başlangıçtan önce olamaz."); return; }
            end = e2;
        }

        Result = new TaxGroupRate { VatType = (VatType)type, Rate = rate, StartDate = start, EndDate = end };
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

public record VatTypeOption(int Value, string Text);
