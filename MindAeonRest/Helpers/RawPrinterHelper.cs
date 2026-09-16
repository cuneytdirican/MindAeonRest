using System.Runtime.InteropServices;

namespace MindAeonRest.Helpers;

/// <summary>Yazıcıya ham (RAW) bayt gönderir — termal kesme ve kasa çekmecesi (ESC/POS) için.</summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DOCINFO { public string pDocName = "MindAeonRest"; public string? pOutputFile; public string pDataType = "RAW"; }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string src, out IntPtr h, IntPtr pd);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartDocPrinter(IntPtr h, int level, DOCINFO di);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr h, byte[] buf, int count, out int written);

    /// <summary>Belirtilen yazıcıya ham bayt gönderir. Başarılıysa true (best-effort).</summary>
    public static bool Send(string printerName, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(printerName) || data.Length == 0) return false;
        IntPtr h = IntPtr.Zero;
        try
        {
            if (!OpenPrinter(printerName, out h, IntPtr.Zero)) return false;
            if (!StartDocPrinter(h, 1, new DOCINFO())) return false;
            StartPagePrinter(h);
            WritePrinter(h, data, data.Length, out _);
            EndPagePrinter(h);
            EndDocPrinter(h);
            return true;
        }
        catch { return false; }
        finally { if (h != IntPtr.Zero) ClosePrinter(h); }
    }

    // ESC/POS komutları
    private static readonly byte[] Cut = { 0x1D, 0x56, 0x42, 0x00 };          // GS V B 0 - tam kesme (besleme ile)
    private static readonly byte[] DrawerKick = { 0x1B, 0x70, 0x00, 0x19, 0xFA }; // ESC p 0 - çekmece tetik

    public static void CutPaper(string printerName) => Send(printerName, Cut);
    public static void OpenDrawer(string printerName) => Send(printerName, DrawerKick);
    public static void CutAndKick(string printerName) => Send(printerName, DrawerKick.Concat(Cut).ToArray());
}
