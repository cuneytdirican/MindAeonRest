using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

/// <summary>
/// Yeniden kullanılabilir hızlı ekleme dialogu (ekrandan çıkmadan entity tanımlama).
/// İki opsiyonel metin alanı + bir oluşturma delegesi alır; yeni Id'yi döndürür.
/// </summary>
public partial class QuickAddWindow : Window
{
    private readonly Func<string, string, int> _onCreate;

    public int? CreatedId { get; private set; }

    /// <param name="label1">İlk alanın etiketi (null ise alan gizlenir, ör. yalnızca İlçe Adı).</param>
    /// <param name="label2">İkinci (zorunlu) alanın etiketi.</param>
    /// <param name="onCreate">(field1, field2) -> yeni Id.</param>
    public QuickAddWindow(string title, string? label1, string label2, Func<string, string, int> onCreate, bool field1Upper = false)
    {
        InitializeComponent();
        TitleText.Text = title;
        _onCreate = onCreate;

        if (label1 is null)
        {
            Field1Panel.Visibility = Visibility.Collapsed;
        }
        else
        {
            Label1.Text = label1;
            if (field1Upper) Box1.CharacterCasing = System.Windows.Controls.CharacterCasing.Upper;
        }
        Label2.Text = label2;

        Loaded += (_, _) => (Field1Panel.Visibility == Visibility.Visible ? Box1 : Box2).Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (string.IsNullOrWhiteSpace(Box2.Text))
        {
            ShowError($"{Label2.Text} alanı zorunludur.");
            return;
        }
        try
        {
            SaveButton.IsEnabled = false;
            CreatedId = _onCreate(Box1.Text.Trim(), Box2.Text.Trim());
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

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
