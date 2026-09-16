using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Documents;

/// <summary>Depo transfer teslim alımı — WPF TransferReceiveWindow birebir karşılığı.
/// Satır bazlı "Bu Teslim" miktarı ile DocumentPostingService.ReceiveTransfer çağrılır;
/// tam/kısmi teslim → Kapandı / Kısmen Kapandı. Yalnız Onaylandı veya Kısmen Kapandı transferde çalışır.</summary>
public class ReceiveModel : PageModel
{
    private readonly DocumentService _docs = new();
    private readonly DocumentPostingService _posting = new();

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public List<ReceiveLine> Lines { get; set; } = new();

    public Document? Doc { get; private set; }
    public string? Error { get; private set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    /// <summary>Bu belge teslim alınabilir mi? (DepoTransfer + Onaylandı/Kısmen Kapandı).</summary>
    public static bool CanReceive(Document d) =>
        d.DocType == DocType.DepoTransfer && d.Status is DocStatus.Onaylandi or DocStatus.KismenKapandi;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Doc = _docs.GetById(Id);
        if (Doc is null) return RedirectToPage("Index", new { type = (int)DocType.DepoTransfer });
        if (!CanReceive(Doc))
        {
            TempData["DocError"] = "Bu evrak teslim alınamaz (yalnız işlenmiş transfer).";
            return RedirectToPage("Index", new { type = (int)DocType.DepoTransfer });
        }
        Error = TempData["DocError"] as string;
        LoadLines();
        return Page();
    }

    public IActionResult OnPostReceive()
    {
        if (!CanAccess) return Forbid();
        Doc = _docs.GetById(Id);
        if (Doc is null) return RedirectToPage("Index", new { type = (int)DocType.DepoTransfer });
        if (!CanReceive(Doc))
        {
            TempData["DocError"] = "Bu evrak teslim alınamaz (yalnız işlenmiş transfer).";
            return RedirectToPage("Index", new { type = (int)DocType.DepoTransfer });
        }

        var receipts = (Lines ?? new())
            .Where(l => l.LineId > 0 && l.ThisReceipt > 0)
            .ToDictionary(l => l.LineId, l => l.ThisReceipt);
        if (receipts.Count == 0)
        {
            Error = "En az bir satıra teslim miktarı girin.";
            LoadLines();
            return Page();
        }

        try
        {
            _posting.ReceiveTransfer(Id, receipts, Session.CurrentUserId);
            TempData["DocMessage"] = "Teslim alındı.";
            return RedirectToPage("Index", new { type = (int)DocType.DepoTransfer });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LoadLines();
            return Page();
        }
    }

    private void LoadLines() => Lines = _docs.GetLines(Id).Select(l => new ReceiveLine
    {
        LineId = l.Id,
        ProductName = l.ProductName ?? l.ProductCode,
        Qty = l.Qty,
        AlreadyReceived = l.ReceivedQty,
        ThisReceipt = Math.Max(0, l.Qty - l.ReceivedQty)   // otomatik kalan (WPF birebir)
    }).ToList();

    public class ReceiveLine
    {
        public int LineId { get; set; }
        public string? ProductName { get; set; }
        public decimal Qty { get; set; }
        public decimal AlreadyReceived { get; set; }
        public decimal ThisReceipt { get; set; }
        public decimal Remaining => Qty - AlreadyReceived;
    }
}
