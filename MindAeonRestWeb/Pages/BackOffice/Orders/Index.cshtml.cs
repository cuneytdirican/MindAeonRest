using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Orders;

/// <summary>Sipariş İzleme (Adisyon Geçmişi) — WPF OrderListWindow birebir karşılığı.
/// Tarih aralığı (varsayılan bugün) + durum (Tümü/Açık/Kapandı/İptal) filtresi;
/// GetOrders(branch, from, to) → satırlar; her satır Detay linki. Salt-okunur izleme.
/// Gate: SysAdmin/Patron.</summary>
public class IndexModel : PageModel
{
    private readonly OrderService _orders = new();

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    // -1 = Tümü; 0/1/2 = OrderStatus (Açık/Kapandı/İptal)
    [BindProperty(SupportsGet = true)] public int Status { get; set; } = -1;

    public List<Order> Items { get; private set; } = new();

    public List<(int Value, string Text)> StatusOptions { get; } = new()
    {
        (-1, "Tümü"),
        ((int)OrderStatus.Acik, "Açık"),
        ((int)OrderStatus.Kapandi, "Kapandı"),
        ((int)OrderStatus.Iptal, "İptal"),
    };

    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        From ??= DateTime.Today;
        To ??= DateTime.Today;
        var from = From.Value;
        var to = To.Value;
        if (to < from) (from, to) = (to, from);

        var list = _orders.GetOrders(Session.CurrentBranch?.Id ?? 0, from, to);
        if (Status >= 0)
            list = list.Where(o => (int)o.Status == Status).ToList();
        Items = list;
        return Page();
    }

    /// <summary>CSV dışa aktarım — ekrandaki sipariş kolonlarını aynı GetOrders sorgusu + durum filtresi
    /// ile üretir. UTF-8 BOM + ';' ayraç (Türkçe Excel). Filtreler querystring ile taşınır.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();

        From ??= DateTime.Today;
        To ??= DateTime.Today;
        var from = From.Value;
        var to = To.Value;
        if (to < from) (from, to) = (to, from);

        var list = _orders.GetOrders(Session.CurrentBranch?.Id ?? 0, from, to);
        if (Status >= 0)
            list = list.Where(o => (int)o.Status == Status).ToList();

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Kod", "Masa", "Müşteri", "Açılış", "Kapanış", "Tutar", "Durum");
        foreach (var o in list)
            Line(o.Code, o.TableCodes, o.CustomerName,
                 o.OpenedAt.ToString("dd.MM.yyyy HH:mm"),
                 o.ClosedAt?.ToString("dd.MM.yyyy HH:mm") ?? "-",
                 o.Total.ToString("N2"), o.StatusText);

        return CsvFile(sb.ToString(), $"siparisler-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }

    // ── CSV yardımcıları (';' ayraç, tırnak-kaçış, UTF-8 BOM) ──
    private static string Esc(string? s) => MindAeonRest.Helpers.CsvHelper.Field(s);

    private FileContentResult CsvFile(string csv, string fileName)
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes(csv);
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
