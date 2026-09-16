using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Helpers;

/// <summary>Evrak A4 çıktısı (ön izleme + yazdır). FlowDocument → PrintDialog (tüm sürücüler).</summary>
public static class DocumentReceipt
{
    public static void Print(Document doc, IReadOnlyList<DocumentLine> lines, string companyName, string? branchName)
    {
        var fd = Build(doc, lines, companyName, branchName);
        var pd = new PrintDialog();
        if (pd.ShowDialog() != true) return;
        fd.PageHeight = pd.PrintableAreaHeight;
        fd.PageWidth = pd.PrintableAreaWidth;
        fd.ColumnWidth = pd.PrintableAreaWidth;
        IDocumentPaginatorSource idp = fd;
        pd.PrintDocument(idp.DocumentPaginator, doc.FisNo);
    }

    private static FlowDocument Build(Document doc, IReadOnlyList<DocumentLine> lines, string companyName, string? branchName)
    {
        var fd = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, PagePadding = new Thickness(36) };

        // Şirket logosu (renkli) — varsa en üste
        try
        {
            if (Session.CurrentCompany is { } co)
            {
                var (color, _) = new CompanyService().GetLogos(co.Id);
                if (color is { Length: > 0 })
                {
                    var img = new BitmapImage();
                    using var ms = new MemoryStream(color);
                    img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
                    fd.Blocks.Add(new BlockUIContainer(new Image { Source = img, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform }) { Margin = new Thickness(0, 0, 0, 6) });
                }
            }
        }
        catch { /* logo opsiyonel */ }

        // Başlık
        fd.Blocks.Add(new Paragraph(new Run(companyName)) { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0) });
        if (!string.IsNullOrWhiteSpace(branchName))
            fd.Blocks.Add(new Paragraph(new Run(branchName)) { FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6) });
        fd.Blocks.Add(new Paragraph(new Run(doc.DocTypeText)) { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

        // Üst bilgi tablosu (2 sütun)
        var infoRows = new List<(string K, string V)>();
        void Info(string k, string? v) => infoRows.Add((k, v ?? ""));
        Info("Fiş No", doc.FisNo);
        Info("Belge No", doc.BelgeNo);
        Info("Tarih", doc.DateText);
        if (doc.BelgeTarihi is { } bt) Info("Belge Tarihi", bt.ToString("dd.MM.yyyy"));
        if (doc.CustomerName is { Length: > 0 }) Info("Cari", doc.CustomerName);
        if (doc.ProjectCode is { Length: > 0 }) Info("Proje", doc.ProjectCode);
        if (doc.UsdRate is { } u) Info("USD Kuru", u.ToString("N4"));
        if (doc.EurRate is { } er) Info("EUR Kuru", er.ToString("N4"));

        var infoTable = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
        infoTable.Columns.Add(new TableColumn { Width = new GridLength(110) });
        infoTable.Columns.Add(new TableColumn { Width = new GridLength(220) });
        var ig = new TableRowGroup();
        foreach (var (k, v) in infoRows)
        {
            var row = new TableRow();
            row.Cells.Add(Cell(k, bold: true));
            row.Cells.Add(Cell(v));
            ig.Rows.Add(row);
        }
        infoTable.RowGroups.Add(ig);
        fd.Blocks.Add(infoTable);

        // Satır tablosu
        var t = new System.Windows.Documents.Table { CellSpacing = 0 };
        double[] w = { 28, 220, 55, 70, 55, 50, 80, 70, 85 };
        foreach (var cw in w) t.Columns.Add(new TableColumn { Width = new GridLength(cw) });
        var head = new TableRowGroup();
        var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xE1, 0xF5)) };
        foreach (var h in new[] { "#", "Ürün", "Miktar", "Fiyat", "İsk%", "KDV%", "Matrah", "KDV", "Toplam" })
            hr.Cells.Add(Cell(h, bold: true, right: h is "Miktar" or "Fiyat" or "İsk%" or "KDV%" or "Matrah" or "KDV" or "Toplam"));
        head.Rows.Add(hr);
        t.RowGroups.Add(head);

        var body = new TableRowGroup();
        foreach (var l in lines)
        {
            var row = new TableRow();
            row.Cells.Add(Cell(l.LineNo.ToString()));
            row.Cells.Add(Cell(l.ProductName ?? l.ProductId.ToString()));
            row.Cells.Add(Cell(l.Qty.ToString("0.##"), right: true));
            row.Cells.Add(Cell(l.UnitPrice.ToString("N2"), right: true));
            row.Cells.Add(Cell(l.DiscountAmount > 0 ? (l.LineGross > 0 ? (l.DiscountAmount / l.LineGross * 100).ToString("0.#") : "") : "", right: true));
            row.Cells.Add(Cell(l.TaxRate.ToString("0.#"), right: true));
            row.Cells.Add(Cell(l.LineNet.ToString("N2"), right: true));
            row.Cells.Add(Cell(l.LineTax.ToString("N2"), right: true));
            row.Cells.Add(Cell(l.LineTotal.ToString("N2"), right: true));
            body.Rows.Add(row);
        }
        t.RowGroups.Add(body);
        fd.Blocks.Add(t);

        // Toplamlar
        var tot = new Paragraph { Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Right };
        tot.Inlines.Add(new Run($"Matrah: {doc.SubTotal:N2}    İskonto: {doc.DiscountTotal:N2}    KDV: {doc.TaxTotal:N2}\n"));
        tot.Inlines.Add(new Run($"GENEL TOPLAM: {doc.GrandTotal:N2}") { FontSize = 14, FontWeight = FontWeights.Bold });
        fd.Blocks.Add(tot);

        if (!string.IsNullOrWhiteSpace(doc.Note))
            fd.Blocks.Add(new Paragraph(new Run("Not: " + doc.Note)) { FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 14, 0, 0) });

        return fd;
    }

    private static TableCell Cell(string text, bool bold = false, bool right = false) =>
        new(new Paragraph(new Run(text))
        {
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left
        })
        { Padding = new Thickness(4, 2, 4, 2), BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0, 0, 0, 0.5) };
}
