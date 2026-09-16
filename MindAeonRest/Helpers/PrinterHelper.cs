using System.Printing;

namespace MindAeonRest.Helpers;

/// <summary>Windows'ta tanımlı yazıcıların listesi (reyon yazıcı ataması için).</summary>
public static class PrinterHelper
{
    public static List<string> GetInstalled()
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queues = server.GetPrintQueues();
            return queues.Select(q => q.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
