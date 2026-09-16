using System.Collections;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MindAeonRest.Views;

/// <summary>
/// Yeniden kullanılabilir "seç-veya-ekle" kontrolü: ComboBox + "+" butonu.
/// Configure(loader, adder) ile beslenir. "+" yeni kayıt ekler ve onu seçer.
/// Kural: entity'si olan her seçim alanında bu kontrol kullanılır (KIOSK/Mutfak hariç).
/// </summary>
public partial class EntityPicker : UserControl
{
    private Func<IEnumerable>? _loader;
    private Func<int?>? _adder;
    private bool _suppress;

    public EntityPicker()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? SelectionChanged;

    public int? SelectedValue
    {
        get => Combo.SelectedValue as int?;
        set => Combo.SelectedValue = value;
    }

    public string PlaceholderText { set => Combo.Tag = value; }

    /// <summary>Veri yükleyici ve "+" ekleyici delegelerini bağlar; listeyi doldurur.</summary>
    public void Configure(Func<IEnumerable> loader, Func<int?> adder, int? selected = null)
    {
        _loader = loader;
        _adder = adder;
        Reload(selected);
    }

    /// <summary>Yalnızca ekleyiciyi günceller (kademeli senaryolarda üst seçim değişince).</summary>
    public void SetAdder(Func<int?>? adder) => _adder = adder;

    public void Reload(int? selected = null)
    {
        _suppress = true;
        Combo.ItemsSource = _loader?.Invoke();
        Combo.SelectedValue = selected;
        _suppress = false;
    }

    public void Clear()
    {
        _suppress = true;
        Combo.ItemsSource = null;
        Combo.SelectedValue = null;
        _suppress = false;
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        SelectionChanged?.Invoke(this, e);
    }

    /// <summary>İsteğe bağlı: arama penceresi başlığı.</summary>
    public string SearchTitle { get; set; } = "Ara ve Seç";

    private void Search_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Combo öğelerinden (Id, Name) çiftleri (reflection — tüm EntityPicker öğeleri Id+Name taşır)
        var items = new List<SearchPickerWindow.Item>();
        foreach (var obj in Combo.Items)
        {
            if (obj is null) continue;
            var t = obj.GetType();
            if (t.GetProperty("Id")?.GetValue(obj) is not int id) continue;
            string name = t.GetProperty("Name")?.GetValue(obj)?.ToString() ?? obj.ToString() ?? id.ToString();
            // Aranabilir metin: TÜM string özellikleri birleştir (kod, ad, telefon, vergi no, açıklama…)
            var parts = t.GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.CanRead)
                .Select(p => p.GetValue(obj) as string)
                .Where(s => !string.IsNullOrWhiteSpace(s));
            items.Add(new SearchPickerWindow.Item(id, name, string.Join(" ", parts)));
        }
        if (items.Count == 0) return;
        var w = new SearchPickerWindow(SearchTitle, items) { Owner = System.Windows.Window.GetWindow(this) };
        if (w.ShowDialog() == true && w.SelectedId is int sel)
            Combo.SelectedValue = sel;   // SelectionChanged tetiklenir → bağımlı yüklemeler çalışır
    }

    private void Add_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_adder is null) return;
        int? newId = _adder.Invoke();
        if (newId.HasValue)
        {
            Reload(newId.Value);   // yeniden yükle ve yeni kaydı seç
            // kademeli senaryolar için seçimi manuel bildir
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(
                Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        }
    }
}
