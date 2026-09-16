using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReportWindow : Window
{
    private readonly ReportFilterOptions _opts;
    private readonly Func<ReportFilter, ReportTable> _builder;
    private ReportTable? _table;

    public ReportWindow(string title, ReportFilterOptions opts, Func<ReportFilter, ReportTable> builder)
    {
        InitializeComponent();
        _opts = opts;
        _builder = builder;
        TitleText.Text = title;
        SetupFilters();
        Loaded += (_, _) => Render(Build());
    }

    private bool Has(ReportFilterOptions o) => _opts.HasFlag(o);

    private void SetupFilters()
    {
        DateFromPanel.Visibility = DateToPanel.Visibility = Has(ReportFilterOptions.DateRange) ? Visibility.Visible : Visibility.Collapsed;
        CustomerPanel.Visibility = Has(ReportFilterOptions.Customer) ? Visibility.Visible : Visibility.Collapsed;
        BranchPanel.Visibility = Has(ReportFilterOptions.Branch) ? Visibility.Visible : Visibility.Collapsed;
        WarehousePanel.Visibility = Has(ReportFilterOptions.Warehouse) ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = Has(ReportFilterOptions.Search) ? Visibility.Visible : Visibility.Collapsed;

        if (Has(ReportFilterOptions.DateRange))
        {
            DateFrom.SelectedDate = DateTime.Today.AddDays(-30);
            DateTo.SelectedDate = DateTime.Today;
        }
        if (Has(ReportFilterOptions.Customer))
        {
            var items = new List<ComboItem> { new(0, "(Tümü)") };
            items.AddRange(new CustomerService().GetAll().Select(c => new ComboItem(c.Id, c.Name)));
            CustomerCombo.ItemsSource = items;
            CustomerCombo.SelectedIndex = 0;
        }
        if (Has(ReportFilterOptions.Branch))
        {
            var items = new List<ComboItem> { new(0, "(Tümü)") };
            if (Session.CurrentCompany is { } co)
                items.AddRange(new BranchService().GetByCompany(co.Id).Select(b => new ComboItem(b.Id, b.Name)));
            BranchCombo.ItemsSource = items;
            BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? 0;
        }
        if (Has(ReportFilterOptions.Warehouse))
        {
            var items = new List<ComboItem> { new(0, "(Tümü)") };
            items.AddRange(new WarehouseService().GetAll().Select(w => new ComboItem(w.Id, w.Name)));
            WarehouseCombo.ItemsSource = items;
            WarehouseCombo.SelectedIndex = 0;
        }
    }

    private ReportFilter ReadFilter() => new()
    {
        From = Has(ReportFilterOptions.DateRange) ? DateFrom.SelectedDate : null,
        To = Has(ReportFilterOptions.DateRange) ? DateTo.SelectedDate : null,
        CustomerId = Has(ReportFilterOptions.Customer) && CustomerCombo.SelectedValue is int ci && ci > 0 ? ci : null,
        BranchId = Has(ReportFilterOptions.Branch) && BranchCombo.SelectedValue is int bi && bi > 0 ? bi : null,
        WarehouseId = Has(ReportFilterOptions.Warehouse) && WarehouseCombo.SelectedValue is int wi && wi > 0 ? wi : null,
        Search = Has(ReportFilterOptions.Search) ? SearchBox.Text?.Trim() : null
    };

    private ReportTable Build()
    {
        try { return _builder(ReadFilter()); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Rapor Hatası"); return new ReportTable { Title = TitleText.Text }; }
    }

    private void Render(ReportTable table)
    {
        _table = table;
        SubText.Text = table.Subtitle ?? "";

        Grid.Columns.Clear();
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            var col = new DataGridTextColumn
            {
                Header = c.Header,
                Binding = new Binding($"[{i}]"),
                Width = new DataGridLength(c.Width)
            };
            if (c.Numeric)
            {
                var st = new Style(typeof(TextBlock));
                st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                col.ElementStyle = st;
            }
            Grid.Columns.Add(col);
        }

        var display = table.Rows
            .Select(r => table.Columns.Select((c, i) => Exporter.Format(i < r.Length ? r[i] : null, c)).ToArray())
            .ToList();
        Grid.ItemsSource = display;
        CountText.Text = $"{table.Rows.Count} kayıt";

        FooterPanel.Children.Clear();
        if (table.Footer is { } ft)
        {
            FooterBar.Visibility = Visibility.Visible;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (i >= ft.Length || ft[i] is null) continue;
                var c = table.Columns[i];
                string val = Exporter.Format(ft[i], c);
                string text = c.Numeric ? $"{c.Header}: {val}" : val;
                FooterPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 22, 0),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
                });
            }
        }
        else FooterBar.Visibility = Visibility.Collapsed;
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Render(Build());

    private void Csv_Click(object sender, RoutedEventArgs e) { if (_table != null) Exporter.SaveCsv(_table); }
    private void Excel_Click(object sender, RoutedEventArgs e) { if (_table != null) Exporter.SaveExcel(_table); }
    private void Pdf_Click(object sender, RoutedEventArgs e) { if (_table != null) Exporter.PrintPdf(_table); }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
