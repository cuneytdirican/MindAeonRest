using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class BranchEditWindow : Window
{
    private readonly BranchService _branchService = new();
    private readonly int _companyId;
    private readonly CompanyBranch? _existing;

    public CompanyBranch? SavedBranch { get; private set; }

    public BranchEditWindow(int companyId, string companyName, CompanyBranch? existing = null)
    {
        InitializeComponent();
        _companyId = companyId;
        _existing = existing;

        PopulateDayStartBoxes();

        if (existing is null)
        {
            TitleText.Text = "Yeni Şube";
            SubtitleText.Text = $"{companyName} için yeni şube ekleyin.";
            Location.Initialize();
            SetDayStart(0);
        }
        else
        {
            TitleText.Text = $"Şube Düzenle · {existing.Code}";
            SubtitleText.Text = companyName;
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            PhoneBox.Text = existing.Phone;
            TaxNumberBox.Text = existing.TaxNumber;
            TaxOfficeBox.Text = existing.TaxOffice;
            AddressBox.Text = existing.Address;
            ActiveCheck.IsChecked = existing.IsActive;
            SetDayStart(existing.DayStartMinutes);
            Location.Initialize(existing.CountryId, existing.CityId, existing.DistrictId);
        }

        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir şube kodu giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Şube adını giriniz."); return; }

        var branch = _existing ?? new CompanyBranch();
        branch.CompanyId = _companyId;
        branch.Code = CodeBox.Text;
        branch.Name = NameBox.Text.Trim();
        branch.Phone = PhoneBox.Text;
        branch.TaxNumber = TaxNumberBox.Text;
        branch.TaxOffice = TaxOfficeBox.Text;
        branch.Address = AddressBox.Text;
        branch.IsActive = ActiveCheck.IsChecked == true;
        branch.DayStartMinutes = ReadDayStartMinutes();
        branch.CountryId = Location.CountryId;
        branch.CityId = Location.CityId;
        branch.DistrictId = Location.DistrictId;

        try
        {
            SaveButton.IsEnabled = false;
            if (_existing is null)
                SavedBranch = _branchService.Create(branch, Session.CurrentUserId);
            else
            {
                _branchService.Update(branch, Session.CurrentUserId);
                SavedBranch = branch;
            }
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void PopulateDayStartBoxes()
    {
        for (int h = 0; h < 24; h++)
            DayStartHourBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = h.ToString("00"), Tag = h });
        foreach (int m in new[] { 0, 15, 30, 45 })
            DayStartMinuteBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = m.ToString("00"), Tag = m });
    }

    private void SetDayStart(int totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        int hour = (totalMinutes / 60) % 24;
        int minute = totalMinutes % 60;
        // dakika listesi 15'lik adımlar; en yakın geçerli adıma yuvarla
        int snapped = (minute / 15) * 15;

        SelectByTag(DayStartHourBox, hour);
        SelectByTag(DayStartMinuteBox, snapped);
    }

    private int ReadDayStartMinutes()
    {
        int hour = TagOf(DayStartHourBox);
        int minute = TagOf(DayStartMinuteBox);
        return hour * 60 + minute;
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox box, int tag)
    {
        foreach (var obj in box.Items)
            if (obj is System.Windows.Controls.ComboBoxItem item && item.Tag is int t && t == tag)
            {
                box.SelectedItem = item;
                return;
            }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static int TagOf(System.Windows.Controls.ComboBox box)
        => box.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is int t ? t : 0;

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
