using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using MindAeonRest.Models;

namespace MindAeonRest.Helpers;

/// <summary>Rapor tablolarını CSV / Excel (.xlsx) / PDF (yazdır) olarak dışa aktarır.</summary>
public static class Exporter
{
    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Bir hücreyi kolon biçimine göre metne çevirir (gösterim + export ortak).</summary>
    public static string Format(object? v, ReportColumn c) => Cell(v, c);

    private static string Cell(object? v, ReportColumn c)
    {
        if (v is null) return "";
        if (c.Numeric && v is IFormattable f) return f.ToString(c.Effective, Tr);
        if (!c.Numeric && !string.IsNullOrEmpty(c.Format) && v is IFormattable f2) return f2.ToString(c.Format, Tr);
        return v.ToString() ?? "";
    }

    // ---- CSV ----
    public static void SaveCsv(ReportTable t)
    {
        var path = AskPath(t.Title, "CSV dosyası (*.csv)|*.csv");
        if (path is null) return;
        WriteCsv(t, path);
        Dialog.Success($"CSV kaydedildi:\n{path}");
    }

    public static void WriteCsv(ReportTable t, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", t.Columns.Select(c => Esc(c.Header))));
        foreach (var r in t.Rows)
            sb.AppendLine(string.Join(";", t.Columns.Select((c, i) => Esc(Cell(i < r.Length ? r[i] : null, c)))));
        if (t.Footer is { } ft)
            sb.AppendLine(string.Join(";", t.Columns.Select((c, i) => Esc(Cell(i < ft.Length ? ft[i] : null, c)))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM → Excel TR uyumu
    }

    // BULGU 11: CSV/formül enjeksiyonu nötrleştirme + ayraç kaçışı ortak yardımcıda (CsvHelper.Field).
    private static string Esc(string s) => CsvHelper.Field(s);

    // ---- Excel ----
    public static void SaveExcel(ReportTable t)
    {
        var path = AskPath(t.Title, "Excel dosyası (*.xlsx)|*.xlsx");
        if (path is null) return;
        WriteExcel(t, path);
        Dialog.Success($"Excel kaydedildi:\n{path}");
    }

    public static void WriteExcel(ReportTable t, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Rapor");
        int row = 1;
        ws.Cell(row, 1).Value = t.Title; ws.Cell(row, 1).Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.FontSize = 14;
        row++;
        if (!string.IsNullOrWhiteSpace(t.Subtitle)) { ws.Cell(row, 1).Value = t.Subtitle; row++; }
        row++;
        int headerRow = row;
        for (int c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = t.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEC, 0xE1, 0xF5);
        }
        row++;
        foreach (var r in t.Rows)
        {
            for (int c = 0; c < t.Columns.Count; c++)
            {
                var col = t.Columns[c];
                var v = c < r.Length ? r[c] : null;
                var cell = ws.Cell(row, c + 1);
                if (col.Numeric && v is IConvertible && v is not string)
                {
                    cell.Value = Convert.ToDouble(v, Tr);
                    cell.Style.NumberFormat.Format = "#,##0.00";
                }
                else cell.Value = Cell(v, col);
            }
            row++;
        }
        if (t.Footer is { } ft)
        {
            for (int c = 0; c < t.Columns.Count; c++)
            {
                var col = t.Columns[c];
                var v = c < ft.Length ? ft[c] : null;
                var cell = ws.Cell(row, c + 1);
                cell.Style.Font.Bold = true;
                cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                if (col.Numeric && v is IConvertible && v is not string)
                { cell.Value = Convert.ToDouble(v, Tr); cell.Style.NumberFormat.Format = "#,##0.00"; }
                else cell.Value = Cell(v, col);
            }
        }
        ws.Range(headerRow, 1, headerRow, t.Columns.Count).SetAutoFilter();
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    // ---- PDF (yazdır / Microsoft Print to PDF) ----
    public static void PrintPdf(ReportTable t)
    {
        var doc = BuildFlowDocument(t);
        var pd = new System.Windows.Controls.PrintDialog();
        if (pd.ShowDialog() != true) return;
        doc.PageHeight = pd.PrintableAreaHeight;
        doc.PageWidth = pd.PrintableAreaWidth;
        doc.ColumnWidth = pd.PrintableAreaWidth;
        IDocumentPaginatorSource idp = doc;
        pd.PrintDocument(idp.DocumentPaginator, t.Title);
        Dialog.Info("PDF için yazıcı seçiminde 'Microsoft Print to PDF' kullanabilirsiniz.", "PDF / Yazdır");
    }

    private static FlowDocument BuildFlowDocument(ReportTable t)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 10, PagePadding = new Thickness(28) };
        doc.Blocks.Add(new Paragraph(new Run(t.Title)) { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        if (!string.IsNullOrWhiteSpace(t.Subtitle))
            doc.Blocks.Add(new Paragraph(new Run(t.Subtitle)) { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) });

        var table = new System.Windows.Documents.Table { CellSpacing = 0 };
        foreach (var c in t.Columns) table.Columns.Add(new TableColumn { Width = new GridLength(c.Width) });
        var head = new TableRowGroup();
        var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xE1, 0xF5)) };
        foreach (var c in t.Columns)
            hr.Cells.Add(new TableCell(new Paragraph(new Run(c.Header)) { FontWeight = FontWeights.Bold, TextAlignment = c.Numeric ? TextAlignment.Right : TextAlignment.Left }) { Padding = new Thickness(4, 3, 4, 3) });
        head.Rows.Add(hr);
        table.RowGroups.Add(head);

        var body = new TableRowGroup();
        foreach (var r in t.Rows)
        {
            var row = new TableRow();
            for (int i = 0; i < t.Columns.Count; i++)
            {
                var col = t.Columns[i];
                row.Cells.Add(new TableCell(new Paragraph(new Run(Cell(i < r.Length ? r[i] : null, col))) { TextAlignment = col.Numeric ? TextAlignment.Right : TextAlignment.Left }) { Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0, 0, 0, 0.5) });
            }
            body.Rows.Add(row);
        }
        if (t.Footer is { } ft)
        {
            var row = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xEE, 0xFA)) };
            for (int i = 0; i < t.Columns.Count; i++)
            {
                var col = t.Columns[i];
                row.Cells.Add(new TableCell(new Paragraph(new Run(Cell(i < ft.Length ? ft[i] : null, col))) { FontWeight = FontWeights.Bold, TextAlignment = col.Numeric ? TextAlignment.Right : TextAlignment.Left }) { Padding = new Thickness(4, 3, 4, 3), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 0) });
            }
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        doc.Blocks.Add(table);
        return doc;
    }

    private static string? AskPath(string title, string filter)
    {
        var safe = string.Concat(title.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog { Title = "Dışa Aktar", Filter = filter, FileName = $"{safe}_{DateTime.Now:yyyyMMdd_HHmm}" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
