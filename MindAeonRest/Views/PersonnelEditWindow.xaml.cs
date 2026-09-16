using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PersonnelEditWindow : Window
{
    private readonly PersonnelService _service = new();
    private readonly Personnel _person;

    public PersonnelEditWindow(Personnel? existing = null)
    {
        InitializeComponent();
        _person = existing ?? new Personnel();

        var branches = new List<ComboItem> { new(0, "(Yok)") };
        if (Session.CurrentCompany is { } co)
            branches.AddRange(new BranchService().GetByCompany(co.Id).Select(b => new ComboItem(b.Id, b.Name)));
        BranchCombo.ItemsSource = branches;
        BranchCombo.SelectedValue = _person.BranchId ?? 0;

        if (existing is not null)
        {
            TitleText.Text = $"Personel · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.FullName;
            TitleBox.Text = existing.Title;
            DepBox.Text = existing.Department;
            TcBox.Text = existing.NationalId;
            PhoneBox.Text = existing.Phone;
            HireBox.SelectedDate = existing.HireDate;
            SalaryBox.Text = existing.Salary.ToString("0.##", CultureInfo.InvariantCulture);
            ActiveCheck.IsChecked = existing.IsActive;
            LoadLeaves();
        }
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void LoadLeaves()
    {
        if (_person.Id == 0) { LeavesGrid.ItemsSource = null; return; }
        LeavesGrid.ItemsSource = _service.GetLeaves(_person.Id);
    }

    private bool SavePerson()
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return false; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Personel adını giriniz."); return false; }
        decimal salary = 0;
        if (!string.IsNullOrWhiteSpace(SalaryBox.Text) && !decimal.TryParse(SalaryBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out salary))
        { ShowError("Maaş geçerli bir sayı olmalı."); return false; }

        _person.Code = CodeBox.Text;
        _person.FullName = NameBox.Text.Trim();
        _person.Title = Empty(TitleBox.Text);
        _person.Department = Empty(DepBox.Text);
        _person.NationalId = Empty(TcBox.Text);
        _person.Phone = Empty(PhoneBox.Text);
        _person.BranchId = BranchCombo.SelectedValue is int b && b > 0 ? b : null;
        _person.HireDate = HireBox.SelectedDate;
        _person.Salary = salary;
        _person.IsActive = ActiveCheck.IsChecked == true;
        try { _service.Save(_person, Session.CurrentUserId); return true; }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }

    /// <summary>Kayıttan sonra personelin Id'si (EntityPicker "+" ekleme için).</summary>
    public int? SavedId => _person.Id > 0 ? _person.Id : null;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SavePerson()) return;
        DialogResult = true;
        Close();
    }

    private void AddLeave_Click(object sender, RoutedEventArgs e)
    {
        if (_person.Id == 0)
        {
            if (!Dialog.Confirm("İzin eklemek için personel önce kaydedilecek. Devam edilsin mi?", "Personel Kaydı")) return;
            if (!SavePerson()) return;
        }
        var w = new PersonnelLeaveEditWindow(_person.Id) { Owner = this };
        if (w.ShowDialog() == true) LoadLeaves();
    }

    private void LeavesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LeavesGrid.SelectedItem is not PersonnelLeave l) return;
        var w = new PersonnelLeaveEditWindow(_person.Id, l) { Owner = this };
        if (w.ShowDialog() == true) LoadLeaves();
    }

    private void DeleteLeave_Click(object sender, RoutedEventArgs e)
    {
        if (LeavesGrid.SelectedItem is not PersonnelLeave l) { Dialog.Info("Silinecek izni seçin.", "İzin"); return; }
        if (!Dialog.Confirm($"{l.TypeText} ({l.RangeText}) silinsin mi?", "İzin Sil")) return;
        _service.DeleteLeave(l.Id, Session.CurrentUserId);
        LoadLeaves();
    }

    private static string? Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    // Kapat: personel zaten kaydedildiyse (izin eklemek için) listeyi tazelemek üzere true döner
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = _person.Id != 0; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
