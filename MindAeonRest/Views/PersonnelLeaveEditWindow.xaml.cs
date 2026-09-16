using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class PersonnelLeaveEditWindow : Window
{
    private readonly PersonnelService _service = new();
    private readonly PersonnelLeave _leave;

    public PersonnelLeaveEditWindow(int personnelId, PersonnelLeave? existing = null)
    {
        InitializeComponent();
        _leave = existing ?? new PersonnelLeave { PersonnelId = personnelId, StartDate = DateTime.Today, EndDate = DateTime.Today };

        TypeCombo.ItemsSource = Enum.GetValues<LeaveType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();
        TypeCombo.SelectedValue = (int)_leave.LeaveType;
        StartBox.SelectedDate = _leave.StartDate;
        EndBox.SelectedDate = _leave.EndDate;
        NoteBox.Text = _leave.Note;
        if (existing is not null) TitleText.Text = $"İzin · {existing.RangeText}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (StartBox.SelectedDate is null || EndBox.SelectedDate is null) { ShowError("Başlangıç ve bitiş tarihi seçiniz."); return; }
        _leave.LeaveType = (LeaveType)(TypeCombo.SelectedValue ?? 0);
        _leave.StartDate = StartBox.SelectedDate.Value;
        _leave.EndDate = EndBox.SelectedDate.Value;
        _leave.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
        try { SaveButton.IsEnabled = false; _service.SaveLeave(_leave, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
