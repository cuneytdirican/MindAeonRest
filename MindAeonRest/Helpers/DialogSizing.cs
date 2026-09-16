using System;
using System.Collections.Generic;
using System.Windows;

namespace MindAeonRest.Helpers;

/// <summary>Form/düzenleme pencerelerini açılırken ekranın büyük kısmına yayar + ortalar.
/// Amaç: kullanıcı her şeyi tek bakışta görsün, mümkün olduğunca AZ dikey scroll. Küçük picker/onay
/// pencereleri (QuickAdd, klavye, mesaj kutusu) etkilenmez. App.OnStartup'ta global Loaded ile bağlanır.</summary>
public static class DialogSizing
{
    // EditWindow dışında büyütülecek form/liste pencereleri
    private static readonly HashSet<string> Extra = new(StringComparer.Ordinal)
    {
        "ProductBulkPriceWindow", "QuickProductWindow", "ProductPriceMatrixWindow", "CampaignWindow", "DocumentConvertWindow", "TransferReceiveWindow", "CompanySetupWindow",
        "SystemDefinitionsWindow", "DocumentListWindow", "OrderListWindow", "GiderListWindow",
        "CekSenetWindow", "BankaWindow", "KasaWindow", "ReservationWindow", "CsvImportWindow",
        "PriceTransferWindow", "OrderDetailWindow", "HesapFisiWindow", "ReportWindow", "RaporWindow",
        "IadeWindow", "DeliverySettingsWindow", "CekVadeWindow", "FicheReportWindow", "ZReportListWindow",
        "ExtractWindow", "KioskDashboardWindow", "ReyonRouteMatrixWindow", "MasaRaporWindow"
    };

    private static bool ShouldEnlarge(string typeName) =>
        typeName.EndsWith("EditWindow", StringComparison.Ordinal) || Extra.Contains(typeName);

    public static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w) return;
        if (w.WindowState == WindowState.Maximized) return;      // zaten tam ekran (ör. DocumentEditWindow)

        var wa = SystemParameters.WorkArea;

        if (ShouldEnlarge(w.GetType().Name))
        {
            bool wide = w.ActualWidth >= 900 || w.Width >= 900;   // geniş tasarlanmış pencere daha da geniş açılır
            double width = wide ? Math.Min(wa.Width * 0.90, 1700) : Math.Min(wa.Width * 0.62, 1100);
            double height = wa.Height * 0.92;
            width = Math.Min(width, wa.Width - 16);
            height = Math.Min(height, wa.Height - 16);

            w.Width = width;
            w.Height = height;
            w.Left = wa.Left + (wa.Width - width) / 2;            // ekran ortası (büyük boyutla)
            w.Top = wa.Top + (wa.Height - height) / 2;
            return;
        }

        // GENEL: küçük dialog/popup'lar (İskontolar, picker, onay vb.) — içerik mevcut yüksekliğe
        // sığmıyorsa yüksekliği İÇERİĞE göre BÜYÜT (yalnız büyüt) → alttaki tüm form elemanları görünür.
        GrowToContent(w, wa);
    }

    /// <summary>İçerik mevcut yüksekliğe sığmıyorsa pencereyi içeriğe göre büyütür (grow-only), ekranda tutar.</summary>
    private static void GrowToContent(Window w, Rect wa)
    {
        if (w.Content is not FrameworkElement root) return;
        try
        {
            w.UpdateLayout();
            double availW = root.ActualWidth > 0 ? root.ActualWidth : (double.IsNaN(w.Width) ? w.ActualWidth : w.Width);
            if (availW <= 0) return;
            root.Measure(new Size(availW, double.PositiveInfinity));
            double needed = root.DesiredSize.Height;
            double chrome = Math.Max(0, w.ActualHeight - root.ActualHeight);   // kenarsız pencerede ~0
            double target = needed + chrome + 6;                              // küçük güvenlik payı
            double maxH = wa.Height - 16;

            if (target > w.ActualHeight + 1)
            {
                double newH = Math.Min(target, maxH);
                w.Height = newH;
                double top = double.IsNaN(w.Top) ? wa.Top + (wa.Height - newH) / 2 : w.Top;
                if (top + newH > wa.Bottom) top = wa.Bottom - newH;
                if (top < wa.Top) top = wa.Top;
                w.Top = top;
            }
        }
        catch { /* ölçüm/yerleşim hatası olsa bile pencere açılmaya devam etsin */ }
    }
}
