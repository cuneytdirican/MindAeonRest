using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindAeonRest.Views;

public partial class SetupWizardWindow : Window
{
    private readonly List<SetupStepVM> _steps;

    public SetupWizardWindow(List<SetupStepVM> steps)
    {
        InitializeComponent();
        _steps = steps;
        StepsHost.ItemsSource = _steps;
        RefreshAll();
        Activated += (_, _) => RefreshAll();   // adımdan dönünce durumu güncelle
    }

    private void RefreshAll()
    {
        foreach (var s in _steps) s.Refresh();
        int done = _steps.Count(s => s.IsDone);
        ProgressText.Text = $"İlerleme: {done} / {_steps.Count} adım tamamlandı";
        ProgressBarMain.Value = _steps.Count == 0 ? 0 : done * 100.0 / _steps.Count;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SetupStepVM s)
        {
            try { s.OpenAction(); } catch (Exception ex) { Helpers.Dialog.Error(ex.Message, "Hata"); }
            RefreshAll();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

/// <summary>Kurulum sihirbazı adımı: numara, başlık, açıklama, canlı durum (kayıt sayısı), açma aksiyonu.</summary>
public class SetupStepVM : INotifyPropertyChanged
{
    public int No { get; init; }
    public string Icon { get; init; } = "•";
    public string Title { get; init; } = "";
    public string Desc { get; init; } = "";
    public Func<(int count, string text)> StatusFunc { get; init; } = () => (0, "");
    public Action OpenAction { get; init; } = () => { };

    public bool IsDone { get; private set; }
    private string _statusText = "";
    public string StatusText { get => _statusText; private set { _statusText = value; OnCh(nameof(StatusText)); } }
    private string _badgeText = "";
    public string BadgeText { get => _badgeText; private set { _badgeText = value; OnCh(nameof(BadgeText)); } }
    private Brush _badgeBrush = Brushes.Gray;
    public Brush BadgeBrush { get => _badgeBrush; private set { _badgeBrush = value; OnCh(nameof(BadgeBrush)); } }

    private static readonly Brush Done = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly Brush Todo = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

    public void Refresh()
    {
        try
        {
            var (count, text) = StatusFunc();
            IsDone = count > 0;
            StatusText = text;
            BadgeText = IsDone ? "✓" : No.ToString();
            BadgeBrush = IsDone ? Done : Todo;
        }
        catch { StatusText = "—"; BadgeText = No.ToString(); BadgeBrush = Todo; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnCh(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
