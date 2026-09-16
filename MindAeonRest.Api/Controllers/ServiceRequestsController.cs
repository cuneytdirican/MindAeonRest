using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[Route("api/service-requests")]
public class ServiceRequestsController(PosNotifier notifier) : PosControllerBase
{
    private readonly ServiceRequestService _svc = new();
    private readonly OrderService _orders = new();

    /// <summary>Şubedeki bekleyen servis istekleri (type ile filtrelenebilir: 0=Çağrı,1=Temizlik,2=Hesap).</summary>
    [HttpGet]
    public ActionResult<IEnumerable<object>> Active([FromQuery] int? type = null)
    {
        var t = type.HasValue ? (ServiceRequestType?)type.Value : null;
        return Ok(_svc.GetActive(BranchId(), t).Select(Project));
    }

    /// <summary>Yeni servis isteği (masaya çağrı / masa temizliği / hesap).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequestDto dto)
    {
        int b = BranchId();
        // IDOR: verilen masa/sipariş başka şubeye ait olamaz.
        if (dto.TableId is int t && _orders.GetTableBranch(t) != b) return NotFound();
        if (dto.OrderId is int o && _orders.GetOrderBranch(o) != b) return NotFound();
        var req = new ServiceRequest
        {
            BranchId = BranchId(),
            TableId = dto.TableId,
            ZoneId = dto.ZoneId,
            OrderId = dto.OrderId,
            Type = (ServiceRequestType)dto.Type,
            Note = dto.Note
        };
        var saved = _svc.Create(req, UserId);
        await notifier.ServiceChanged(CompanyCode, BranchId());
        return Ok(new { saved.Id });
    }

    /// <summary>İsteği tamamlandı işaretle.</summary>
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        if (_svc.GetBranch(id) != BranchId()) return NotFound();
        _svc.Complete(id, UserId);
        await notifier.ServiceChanged(CompanyCode, BranchId());
        return Ok();
    }

    private static object Project(ServiceRequest s) => new
    {
        s.Id, s.TableId, s.TableCode, s.ZoneId, s.ZoneName, s.OrderId,
        Type = (int)s.Type, s.TypeText, Status = (int)s.Status, s.Note, s.ElapsedText
    };
}

public record CreateServiceRequestDto(int? TableId, int? ZoneId, int? OrderId, int Type, string? Note);
