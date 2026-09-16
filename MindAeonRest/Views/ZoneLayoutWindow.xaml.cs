using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ZoneLayoutWindow : Window
{
    private readonly ZoneService _zoneService = new();
    private readonly TableService _tableService = new();
    private readonly ObservableCollection<TableItemVM> _items = new();
    private readonly List<int> _deletedIds = new();
    private readonly Dictionary<int, int> _zoneBranch = new();   // zoneId -> branchId
    private int? _zoneId;
    private int _branchId;

    private TableItemVM? _selected;
    private TableItemVM? _drag;
    private Point _dragStart;
    private double _dragStartX, _dragStartY;
    private bool _suppress;

    public ZoneLayoutWindow()
    {
        InitializeComponent();
        TableHost.ItemsSource = _items;
        ShapeCombo.ItemsSource = new List<ComboItem> { new(0, "Kare"), new(1, "Yuvarlak") };
        var zones = _zoneService.GetAll();
        foreach (var z in zones) _zoneBranch[z.Id] = z.BranchId;
        ZoneCombo.ItemsSource = zones.Select(z => new ComboItem(z.Id, z.Display)).ToList();
    }

    private void Zone_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ZoneCombo.SelectedValue is not int zid) return;
        _zoneId = zid;
        _branchId = _zoneBranch.TryGetValue(zid, out var b) ? b : 0;
        _items.Clear();
        _deletedIds.Clear();
        foreach (var t in _tableService.GetByZone(zid))
            _items.Add(TableItemVM.From(t));
        Select(null);
    }

    private void AddSquare_Click(object sender, RoutedEventArgs e) => AddTable(TableShape.Kare);
    private void AddRound_Click(object sender, RoutedEventArgs e) => AddTable(TableShape.Yuvarlak);

    private void AddTable(TableShape shape)
    {
        if (_zoneId is null) { Dialog.Info("Önce lokasyon seçin.", "Uyarı"); return; }
        var vm = new TableItemVM
        {
            Id = 0,
            Code = NextCode(),
            Shape = shape,
            Seats = 4,
            X = 40 + (_items.Count % 8) * 30,
            Y = 40 + (_items.Count % 8) * 20,
            Width = 80,
            Height = 80
        };
        _items.Add(vm);
        Select(vm);
    }

    /// <summary>�?ube genelinde artan, tekil masa kodu (MASA{n}).</summary>
    private string NextCode()
    {
        int n = _tableService.NextTableNumberForBranch(_branchId);
        while (_items.Any(i => i.Code == $"MASA{n}")) n++;
        return $"MASA{n}";
    }

    private void DeleteTable_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { Dialog.Info("Önce bir masa seçin.", "Uyarı"); return; }
        if (_selected.Id > 0) _deletedIds.Add(_selected.Id);
        _items.Remove(_selected);
        Select(null);
    }

    // ===== Seçim + özellik paneli =====
    private void Select(TableItemVM? vm)
    {
        foreach (var i in _items) i.IsSelected = false;
        _selected = vm;
        if (vm is not null) vm.IsSelected = true;

        _suppress = true;
        PropsPanel.IsEnabled = vm is not null;
        NoSelText.Visibility = vm is null ? Visibility.Visible : Visibility.Collapsed;
        CodeBox.Text = vm?.Code ?? "";
        NameBox.Text = vm?.Name ?? "";
        ShapeCombo.SelectedValue = vm is null ? null : (int)vm.Shape;
        SeatsBox.Text = vm?.Seats.ToString() ?? "";
        WidthBox.Text = vm?.Width.ToString("0", CultureInfo.InvariantCulture) ?? "";
        HeightBox.Text = vm?.Height.ToString("0", CultureInfo.InvariantCulture) ?? "";
        _suppress = false;
    }

    private void CodeBox_Changed(object sender, TextChangedEventArgs e) { if (!_suppress && _selected != null) _selected.Code = CodeBox.Text; }
    private void NameBox_Changed(object sender, TextChangedEventArgs e) { if (!_suppress && _selected != null) _selected.Name = NameBox.Text; }
    private void Shape_Changed(object sender, SelectionChangedEventArgs e) { if (!_suppress && _selected != null && ShapeCombo.SelectedValue is int s) _selected.Shape = (TableShape)s; }
    private void Seats_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;
        if (int.TryParse(SeatsBox.Text, out var n) && n >= 0 && n <= 40) _selected.Seats = n;
    }
    private void Size_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _selected is null) return;
        if (double.TryParse(WidthBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var w) && w >= 20) _selected.Width = w;
        if (double.TryParse(HeightBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var h) && h >= 20) _selected.Height = h;
    }

    // ===== Sürükle-bırak =====
    private void Table_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TableItemVM vm } fe)
        {
            Select(vm);
            _drag = vm;
            _dragStart = e.GetPosition(TableHost);
            _dragStartX = vm.X;
            _dragStartY = vm.Y;
            fe.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Table_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(TableHost);
        _drag.X = Math.Max(0, _dragStartX + (pos.X - _dragStart.X));
        _drag.Y = Math.Max(0, _dragStartY + (pos.Y - _dragStart.Y));
    }

    private void Table_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
        _drag = null;
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e) => Select(null);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_zoneId is null) { Dialog.Info("Önce lokasyon seçin.", "Uyarı"); return; }
        try
        {
            var tables = _items.Select(i => i.ToModel(_zoneId.Value, _branchId)).ToList();
            _tableService.SaveLayout(_zoneId.Value, _branchId, tables, _deletedIds, Session.CurrentUserId);
            _deletedIds.Clear();
            _items.Clear();
            foreach (var t in _tableService.GetByZone(_zoneId.Value)) _items.Add(TableItemVM.From(t));
            Select(null);
            Dialog.Info("Yerleşim planı kaydedildi.", "Bilgi");
        }
        catch (Exception ex)
        {
            Dialog.Error(ex.Message, "Hata");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}

/// <summary>Plan tasarımcısında bir sandalyenin konumu (container koordinatı).</summary>
public class ChairVM
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Tasarımcıda canlı bağlanan masa öğesi (masa + etrafındaki sandalyeler).</summary>
public class TableItemVM : INotifyPropertyChanged
{
    private const double M = 18, ChairSize = 13, Offset = 10;

    public int Id { get; set; }
    private string _code = "";
    private string? _name;
    private TableShape _shape;
    private int _seats = 4;
    private double _x, _y, _w = 80, _h = 80;
    private bool _selected;

    public ObservableCollection<ChairVM> Chairs { get; } = new();
    public double TableMargin => M;
    public double ContainerWidth => _w + 2 * M;
    public double ContainerHeight => _h + 2 * M;
    public string SeatsText => $"{_seats} kişi";

    public string Code { get => _code; set { _code = value; On(nameof(Code)); } }
    public string? Name { get => _name; set { _name = value; On(nameof(Name)); } }
    public TableShape Shape { get => _shape; set { _shape = value; On(nameof(Shape)); RecomputeChairs(); } }
    public int Seats { get => _seats; set { _seats = value; On(nameof(Seats)); On(nameof(SeatsText)); RecomputeChairs(); } }
    public double X { get => _x; set { _x = value; On(nameof(X)); } }
    public double Y { get => _y; set { _y = value; On(nameof(Y)); } }
    public double Width { get => _w; set { _w = value; On(nameof(Width)); On(nameof(ContainerWidth)); RecomputeChairs(); } }
    public double Height { get => _h; set { _h = value; On(nameof(Height)); On(nameof(ContainerHeight)); RecomputeChairs(); } }
    public bool IsSelected { get => _selected; set { _selected = value; On(nameof(IsSelected)); } }

    // Rezervasyon ekranı için: 0=boş, 1=dolu (rezerve), 2=yeni seçim
    private int _boardState;
    public int BoardState { get => _boardState; set { _boardState = value; On(nameof(BoardState)); } }

    private void RecomputeChairs()
    {
        Chairs.Clear();
        int n = _seats;
        if (n <= 0) return;
        double left = M, top = M, w = _w, h = _h;

        if (_shape == TableShape.Yuvarlak)
        {
            double cx = left + w / 2, cy = top + h / 2, r = Math.Max(w, h) / 2 + Offset;
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
        }
        else
        {
            int per = n / 4, rem = n % 4;
            int[] c = { per, per, per, per };
            for (int i = 0; i < rem; i++) c[i]++;   // top, bottom, left, right
            for (int k = 1; k <= c[0]; k++) Add(left + w * k / (c[0] + 1), top - Offset);
            for (int k = 1; k <= c[1]; k++) Add(left + w * k / (c[1] + 1), top + h + Offset);
            for (int k = 1; k <= c[2]; k++) Add(left - Offset, top + h * k / (c[2] + 1));
            for (int k = 1; k <= c[3]; k++) Add(left + w + Offset, top + h * k / (c[3] + 1));
        }

        void Add(double x, double y) => Chairs.Add(new ChairVM { X = x - ChairSize / 2, Y = y - ChairSize / 2 });
    }

    public static TableItemVM From(Table t)
    {
        var vm = new TableItemVM
        {
            Id = t.Id, Code = t.Code, Name = t.Name, Shape = t.Shape, Seats = t.Seats,
            X = t.X, Y = t.Y, Width = t.Width <= 0 ? 80 : t.Width, Height = t.Height <= 0 ? 80 : t.Height
        };
        vm.RecomputeChairs();
        return vm;
    }

    public Table ToModel(int zoneId, int branchId) => new()
    {
        Id = Id, ZoneId = zoneId, BranchId = branchId, Code = Code, Name = Name, Shape = Shape, Seats = Seats,
        X = X, Y = Y, Width = Width, Height = Height, IsActive = true
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void On(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
