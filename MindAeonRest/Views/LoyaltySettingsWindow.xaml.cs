using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Puan/sadakat programı ayarları: aktiflik, kazanım oranı (₺ başına puan), harcama oranı (1 puanın ₺ karşılığı).</summary>
public partial class LoyaltySettingsWindow : Window
{
    private readonly LoyaltyService _service = new();
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public LoyaltySettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        try
        {
            var s = _service.GetSettings();
            ActiveBox.IsChecked = s.IsActive;
            EarnBox.Text = s.EarnRate.ToString("0.####", Tr);
            RedeemBox.Text = s.RedeemRate.ToString("0.####", Tr);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (!decimal.TryParse((EarnBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out var earn) || earn < 0)
        { ShowError("Kazanım oranı geçersiz (negatif olamaz)."); return; }
        if (!decimal.TryParse((RedeemBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out var redeem) || redeem <= 0)
        { ShowError("Harcama oranı (1 puanın ₺ karşılığı) 0'dan büyük olmalı."); return; }

        try
        {
            SaveButton.IsEnabled = false;
            _service.SaveSettings(new LoyaltySettings { IsActive = ActiveBox.IsChecked == true, EarnRate = earn, RedeemRate = redeem }, Session.CurrentUserId);
            Dialog.Success("Sadakat ayarları kaydedildi.", "Sadakat");
            DialogResult = true; Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
