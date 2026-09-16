using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class HesapFisiWindow : Window
{
    private readonly ReceiptData _data;

    public HesapFisiWindow(Order order) : this(order, false) { }

    /// <param name="includeSettled">Ödeme tamamlandıktan sonra ödenmiş satırları da içeren "ödendi" fişi için true.</param>
    public HesapFisiWindow(Order order, bool includeSettled)
        : this(order, new ReceiptService().BuildAccountReceipt(order, includeSettled)) { }

    /// <summary>Hazır fiş verisiyle (settle-öncesi snapshot) açar — canlı yeniden okuma yapılmaz.
    /// <paramref name="order"/> yalnız yazıcı seçimi (kiosk/zon) için kullanılır.</summary>
    public HesapFisiWindow(Order order, ReceiptData data)
    {
        InitializeComponent();
        _data = data;
        PreviewHost.Child = ReceiptVisual.Build(_data);

        var printers = PrinterHelper.GetInstalled();
        PrinterCombo.ItemsSource = printers;
        // Fiş yazıcısı bu KIOSK terminalinin tanımından gelir (Session.CurrentKioskId). Eski lokasyon yazıcısı yedek.
        string? kioskPrinter = Session.CurrentKioskId is int kid
            ? new KioskService().GetAll().FirstOrDefault(k => k.Id == kid)?.ReceiptPrinterName
            : null;
        string? zonePrinter = new ZoneService().GetAll().FirstOrDefault(z => z.Id == order.ZoneId)?.ReceiptPrinterName;
        string? wanted = !string.IsNullOrWhiteSpace(kioskPrinter) ? kioskPrinter : zonePrinter;
        PrinterCombo.SelectedItem = (!string.IsNullOrWhiteSpace(wanted) && printers.Contains(wanted))
            ? wanted
            : printers.FirstOrDefault();
    }

    private string? SelectedPrinter => PrinterCombo.SelectedItem as string;

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPrinter is null) { Dialog.Info("Yazıcı seçin.", "Uyarı"); return; }
        try
        {
            ReceiptVisual.Print(SelectedPrinter, _data, DrawerCheck.IsChecked == true);
            Dialog.Info("Hesap fişi yazdırıldı.", "Bilgi");
            DialogResult = true;
            Close();
        }
        catch (Exception ex) { Dialog.Error("Yazdırma hatası: " + ex.Message, "Hata"); }
    }

    private void Drawer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPrinter is null) return;
        RawPrinterHelper.OpenDrawer(SelectedPrinter);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
