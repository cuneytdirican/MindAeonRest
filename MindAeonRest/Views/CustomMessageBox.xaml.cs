using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindAeonRest.Views;

public enum DialogIcon { Info, Warn, Error, Question, Success }
public enum DialogButtons { Ok, OkCancel, YesNo, YesNoCancel }
public enum DialogResultType { Ok, Yes, No, Cancel }

public partial class CustomMessageBox : Window
{
    public DialogResultType Result { get; private set; } = DialogResultType.Cancel;

    public CustomMessageBox(string message, string title, DialogIcon icon, DialogButtons buttons)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ApplyIcon(icon);
        BuildButtons(buttons);
    }

    private void ApplyIcon(DialogIcon icon)
    {
        (string glyph, Color color) = icon switch
        {
            DialogIcon.Warn => ("!", Color.FromRgb(0xE9, 0xA2, 0x3B)),
            DialogIcon.Error => ("✕", Color.FromRgb(0xF4, 0x43, 0x36)),
            DialogIcon.Question => ("?", Color.FromRgb(0xE9, 0x1E, 0x63)),
            DialogIcon.Success => ("✓", Color.FromRgb(0x17, 0xB2, 0x6A)),
            _ => ("i", Color.FromRgb(0x33, 0x80, 0xE0))
        };
        IconText.Text = glyph;
        IconCircle.Background = new SolidColorBrush(color);
    }

    private void BuildButtons(DialogButtons buttons)
    {
        switch (buttons)
        {
            case DialogButtons.OkCancel:
                Add("Vazgeç", DialogResultType.Cancel, false);
                Add("Tamam", DialogResultType.Ok, true);
                break;
            case DialogButtons.YesNo:
                Add("Hayır", DialogResultType.No, false);
                Add("Evet", DialogResultType.Yes, true);
                break;
            case DialogButtons.YesNoCancel:
                Add("Vazgeç", DialogResultType.Cancel, false);
                Add("Hayır", DialogResultType.No, false);
                Add("Evet", DialogResultType.Yes, true);
                break;
            default:
                Add("Tamam", DialogResultType.Ok, true);
                break;
        }
    }

    private void Add(string text, DialogResultType result, bool primary)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton")
        };
        btn.Click += (_, _) => { Result = result; DialogResult = result is DialogResultType.Ok or DialogResultType.Yes; Close(); };
        ButtonPanel.Children.Add(btn);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
