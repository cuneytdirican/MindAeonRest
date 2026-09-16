using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[Route("api/tables")]
public class TablesController : PosControllerBase
{
    private readonly ZoneService _zones = new();
    private readonly TableBoardService _board = new();

    /// <summary>Şubenin lokasyonları (zone).</summary>
    [HttpGet("zones")]
    public ActionResult<IEnumerable<object>> Zones()
    {
        int b = BranchId();
        return Ok(_zones.GetAll().Where(z => z.BranchId == b)
            .Select(z => new { z.Id, z.Name, z.BranchId }));
    }

    /// <summary>Lokasyonun masa panosu (canlı durum + açık tutar + garson).</summary>
    [HttpGet("board")]
    public ActionResult<IEnumerable<object>> Board([FromQuery] int zoneId)
    {
        int b = BranchId();
        // IDOR: yalnız çağıranın şubesindeki lokasyonun panosu döndürülür.
        if (!_zones.GetAll().Any(z => z.Id == zoneId && z.BranchId == b)) return NotFound();
        return Ok(_board.GetBoard(zoneId).Select(t => new
        {
            t.TableId, t.Code, t.Name, t.Display, t.Seats,
            Status = (int)t.Status, t.OrderId, t.Amount, t.WaiterName, t.OpenedAt
        }));
    }
}
