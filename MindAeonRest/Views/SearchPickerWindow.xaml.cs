using System.Windows;
using System.Windows.Input;

namespace MindAeonRest.Views;

/// <summary>Binlerce kayıt için Google-tarzı arama-seç penceresi.
/// Item: Id + görünen Name + aranabilir tüm metin (Search=kod+ad+diğer alanlar). Seçilen Id'yi döndürür.</summary>
public partial class SearchPickerWindow : Window
{
    public record Item(int Id, string Name, string Search = "");

    private readonly List<(Item it, string norm)> _all;
    public int? SelectedId { get; private set; }

    public SearchPickerWindow(string title, IEnumerable<Item> items)
    {
        InitializeComponent();
        TitleText.Text = title;
        SearchBox.Tag = "Ara… (kelimeler herhangi bir alanda — ör. \"çiko 50 gr\")";
        _all = items.Select(i => (i, Norm($"{i.Name} {i.Search}"))).ToList();
        Apply("");
        Loaded += (_, _) => SearchBox.Focus();
    }

    // Türkçe I/ı/İ/i ve aksanları katlar (ör. "AI"→"ai", "İÇECEK"→"icecek")
    private static string Norm(string s) => s.ToLowerInvariant().Replace("i̇", "i").Replace('İ', 'i').Replace('ı', 'i').Replace('î', 'i');

    private void Apply(string q)
    {
        IEnumerable<(Item it, string norm)> r = _all;
        var tokens = (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Select(Norm).ToArray();
        if (tokens.Length > 0)
            r = _all.Where(x => tokens.All(t => x.norm.Contains(t, StringComparison.Ordinal)));   // her kelime bir yerde (AND)
        var list = r.Take(500).Select(x => x.it).ToList();
        ResultList.ItemsSource = list;
        if (list.Count > 0) ResultList.SelectedIndex = 0;
        CountText.Text = $"{list.Count} kayıt" + (_all.Count > list.Count ? $" / {_all.Count}" : "");
    }

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => Apply(SearchBox.Text);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not Item it) return;
        SelectedId = it.Id;
        DialogResult = true;
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e) { SearchBox.Focus(); VirtualKeyboardWindow.ShowFor(SearchBox); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
