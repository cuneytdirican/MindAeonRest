using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[Route("api/kitchen")]
[Authorize(Roles = "SysAdmin,Mutfak,Garson,Kasiyer")]   // mutfak + servis personeli; salt operasyonel
public class KitchenController(PosNotifier notifier) : PosControllerBase
{
    private readonly KdsService _kds = new();
    private readonly OrderService _orders = new();

    /// <summary>Mutfağa düşen bekleyen/hazırlanıyor/hazır kalemler (reyonId=0 → tümü). includeDone=true → son teslimler (KDS V2 Tamamlandı sütunu).</summary>
    [HttpGet("tickets")]
    public ActionResult<IEnumerable<KdsLine>> Tickets([FromQuery] int reyonId = 0, [FromQuery] bool includeDone = false) =>
        Ok(_kds.GetTickets(BranchId(), reyonId, includeDone));

    private bool OwnsLine(int lineId) => _orders.GetLineBranch(lineId) == BranchId();

    /// <summary>Kalemi HAZIRLANIYOR işaretle (Başlat) — KDS V2 kanban.</summary>
    [HttpPost("lines/{lineId:int}/start")]
    public async Task<IActionResult> Start(int lineId)
    {
        if (!OwnsLine(lineId)) return NotFound();
        _orders.SetLineStatus(lineId, OrderLineStatus.Hazirlaniyor, UserId);
        await notifier.TableChanged(CompanyCode, BranchId(), new { lineId });
        return Ok();
    }

    /// <summary>Kalemi HAZIR işaretle → garson ekranına anlık bildirim.</summary>
    [HttpPost("lines/{lineId:int}/ready")]
    public async Task<IActionResult> Ready(int lineId)
    {
        if (!OwnsLine(lineId)) return NotFound();
        _orders.SetLineStatus(lineId, OrderLineStatus.Hazir, UserId);
        await notifier.KitchenTicketReady(CompanyCode, BranchId(), new { lineId });
        // HEDEFLİ alarm: kalemi ısmarlayan garsona (sipariş CreatedBy) "hazır" push'u. Garson istemci ownerUserId'yi
        // kendi userId'siyle karşılaştırıp belirginleştirir. Mevcut TicketReady tazele davranışı KORUNUR (yukarıda).
        var info = _orders.GetLineWaiterInfo(lineId);
        await notifier.WaiterReady(CompanyCode, BranchId(), info?.OwnerUserId ?? 0,
            new { lineId, ownerUserId = info?.OwnerUserId ?? 0, tableCode = info?.TableCode, productName = info?.ProductName });
        return Ok();
    }

    /// <summary>Garson teslim aldı → kalemi TESLİM işaretle.</summary>
    [HttpPost("lines/{lineId:int}/delivered")]
    public async Task<IActionResult> Delivered(int lineId)
    {
        if (!OwnsLine(lineId)) return NotFound();
        _orders.SetLineStatus(lineId, OrderLineStatus.Teslim, UserId);
        await notifier.TableChanged(CompanyCode, BranchId(), new { lineId });
        return Ok();
    }
}
