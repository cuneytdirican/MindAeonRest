using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MindAeonRest.Models;

namespace MindAeonRest.Helpers;

/// <summary>Hesap fişini WPF görseli olarak kurar (önizleme ve baskıda aynı). ~80mm.</summary>
public static class ReceiptVisual
{
    public const double Width = 300;

    public static FrameworkElement Build(ReceiptData d)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

        if (d.Logo is { Length: > 0 })
            panel.Children.Add(new Image { Source = ToImage(d.Logo), Stretch = Stretch.Uniform, MaxHeight = 70, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });

        panel.Children.Add(Center(d.CompanyName, 15, FontWeights.Bold));
        if (!string.IsNullOrWhiteSpace(d.BranchName)) panel.Children.Add(Center(d.BranchName, 12, FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(d.BranchAddress)) panel.Children.Add(Center(d.BranchAddress!, 10));
        if (!string.IsNullOrWhiteSpace(d.BranchTax)) panel.Children.Add(Center(d.BranchTax!, 10));
        if (!string.IsNullOrWhiteSpace(d.BranchPhone)) panel.Children.Add(Center("Tel: " + d.BranchPhone, 10));

        panel.Children.Add(Sep());
        panel.Children.Add(KV("Adisyon", d.OrderCode));
        if (!string.IsNullOrWhiteSpace(d.TableCodes)) panel.Children.Add(KV("Masa", d.TableCodes!));
        panel.Children.Add(KV("Tarih", d.DateText));
        panel.Children.Add(KV("Kasiyer", d.CashierName));
        panel.Children.Add(Sep());

        foreach (var it in d.Items)
        {
            var row = new DockPanel { Margin = new Thickness(it.IsComponent ? 12 : 0, 1, 0, 1) };
            var left = new TextBlock { Text = it.IsComponent ? $"  └ {it.Name}" : $"{it.QtyText} {it.Name}", FontSize = it.IsComponent ? 11 : 12, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            var right = new TextBlock { Text = it.AmountText, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right); row.Children.Add(left);
            panel.Children.Add(row);
        }

        panel.Children.Add(Sep());
        foreach (var k in d.Kdv)
            panel.Children.Add(KV(k.RateText, $"Matrah {k.MatrahText}  KDV {k.TaxText}", 10));

        panel.Children.Add(Sep());
        var total = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var tr = new TextBlock { Text = d.TotalText, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
        DockPanel.SetDock(tr, Dock.Right);
        total.Children.Add(tr);
        total.Children.Add(new TextBlock { Text = "TOPLAM", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        panel.Children.Add(total);

        // Ödeme dökümü — yalnız DOLUYSA (ödeme-sonrası fiş). Borç önizlemesinde Payments boştur → hiçbir şey eklenmez.
        if (d.Payments is { Count: > 0 })
        {
            panel.Children.Add(Sep());
            panel.Children.Add(new TextBlock { Text = "ÖDEME", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, Margin = new Thickness(0, 0, 0, 2) });
            foreach (var p in d.Payments)
                panel.Children.Add(KV(p.Label, p.AmountText, 12));
            if (d.Paid is not null) panel.Children.Add(KV("Ödenen", d.PaidText, 12));
            if (d.Change is > 0.005m) panel.Children.Add(KV("Para Üstü", d.ChangeText, 12));
        }

        if (!string.IsNullOrWhiteSpace(d.Footer))
        {
            panel.Children.Add(Sep());
            panel.Children.Add(Center(d.Footer!, 10));
        }

        // Tüm fişlerin en alt satırı: yazılım markası
        panel.Children.Add(Center("MindAeonRest by Mobilteg", 9, FontWeights.SemiBold));

        return new Border { Width = Width, Background = Brushes.White, Child = panel };
    }

    /// <summary>Mutfak/bar fişi görseli — fiyatsız, büyük okunur ürün+miktar+not.</summary>
    public static FrameworkElement BuildKitchen(KitchenTicket t)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        panel.Children.Add(Center("🍳 MUTFAK FİŞİ", 16, FontWeights.Bold));
        panel.Children.Add(Center(t.ReyonName, 15, FontWeights.Bold));
        panel.Children.Add(Sep());
        panel.Children.Add(KV("Adisyon", t.OrderCode, 13));
        if (!string.IsNullOrWhiteSpace(t.TableCodes)) panel.Children.Add(KV("Masa", t.TableCodes!, 13));
        if (!string.IsNullOrWhiteSpace(t.WaiterName)) panel.Children.Add(KV("Garson", t.WaiterName!, 12));
        panel.Children.Add(Sep());

        foreach (var it in t.Lines)
        {
            if (it.IsComponent)
                panel.Children.Add(new TextBlock { Text = $"   └ {it.Name}", FontSize = 13, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap });
            else
                panel.Children.Add(new TextBlock { Text = $"{it.QtyText} {it.Name}", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrWhiteSpace(it.Note))
                panel.Children.Add(new TextBlock { Text = $"      » {it.Note}", FontSize = 13, FontStyle = FontStyles.Italic, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap });
        }

        panel.Children.Add(Sep());
        panel.Children.Add(Center("MindAeonRest by Mobilteg", 9, FontWeights.SemiBold));
        return new Border { Width = Width, Background = Brushes.White, Child = panel };
    }

    /// <summary>Mutfak fişini reyonun yazıcısına basar (yazıcı yoksa/sürücü yoksa sessizce geçer).</summary>
    public static bool PrintKitchen(KitchenTicket t)
    {
        if (!t.HasPrinter) return false;
        try { PrintVisual(t.PrinterName!, BuildKitchen(t)); return true; }
        catch { return false; }
    }

    private static TextBlock Center(string t, double size, FontWeight? w = null) => new()
    { Text = t, FontSize = size, FontWeight = w ?? FontWeights.Normal, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private static DockPanel KV(string k, string v, double size = 11)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var val = new TextBlock { Text = v, FontSize = size, Foreground = Brushes.Black, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(val, Dock.Right);
        dp.Children.Add(val);
        dp.Children.Add(new TextBlock { Text = k, FontSize = size, Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold });
        return dp;
    }

    private static Border Sep() => new() { Height = 1, Background = Brushes.Black, Opacity = 0.35, Margin = new Thickness(0, 5, 0, 5) };

    private static BitmapImage? ToImage(byte[] data)
    {
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(data);
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
            return img;
        }
        catch { return null; }
    }

    /// <summary>Bir görseli belirtilen yazıcıya basar + termal kesme (mutfak fişi vb.).</summary>
    public static void PrintVisual(string printerName, FrameworkElement visual)
    {
        visual.Measure(new Size(Width, double.PositiveInfinity));
        visual.Arrange(new Rect(0, 0, Width, visual.DesiredSize.Height));
        visual.UpdateLayout();
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueue(printerName);
        var dlg = new System.Windows.Controls.PrintDialog { PrintQueue = queue };
        dlg.PrintVisual(visual, "Mutfak Fişi");
        RawPrinterHelper.CutPaper(printerName);
    }

    /// <summary>Belirtilen yazıcıya WPF üzerinden basar (tüm sürücülerle çalışır) + termal kesme/çekmece.</summary>
    public static void Print(string printerName, ReceiptData data, bool openDrawer)
    {
        var visual = Build(data);
        visual.Measure(new Size(Width, double.PositiveInfinity));
        visual.Arrange(new Rect(0, 0, Width, visual.DesiredSize.Height));
        visual.UpdateLayout();

        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueue(printerName);
        var dlg = new System.Windows.Controls.PrintDialog { PrintQueue = queue };
        dlg.PrintVisual(visual, "Hesap Fişi");

        // Termal kesme + (istenirse) çekmece — best-effort
        if (openDrawer) RawPrinterHelper.CutAndKick(printerName);
        else RawPrinterHelper.CutPaper(printerName);
    }
}
