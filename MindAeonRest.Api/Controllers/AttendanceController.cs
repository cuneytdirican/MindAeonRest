using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>
/// FAZ 4c — Personel mesai (time-clock) uçları: giriş/çıkış + açık mesai sorgu, POS/MAUI'den.
/// Branch JWT bağlamından (BranchId()), KioskNo JWT'den (Session.CurrentKioskNo) çözülür — istemci geçmez.
/// Personel listesi (picker için) yalnız aktif personeller. Puantaj/labor-cost raporu BackOffice'te (web).
/// </summary>
[Route("api/attendance")]
[Authorize]
public class AttendanceController : PosControllerBase
{
    private readonly AttendanceService _svc = new();
    private readonly PersonnelService _personnel = new();

    /// <summary>Bu şubenin aktif personelleri (mesai giriş/çıkış picker'ı için).</summary>
    [HttpGet("personnel")]
    public ActionResult<IEnumerable<object>> Personnel()
    {
        int branch = BranchId();
        var list = _personnel.GetAll()
            .Where(p => p.IsActive && (p.BranchId is null || p.BranchId == branch))
            .Select(p => new { p.Id, p.Code, p.FullName });
        return Ok(list);
    }

    /// <summary>Personelin AÇIK (çıkışsız) mesaisi; yoksa null.</summary>
    [HttpGet("open")]
    public ActionResult<Attendance> Open([FromQuery] int personnelId)
        => Ok(_svc.GetOpen(personnelId));

    /// <summary>Mesai girişi. Açık mesai zaten varsa 400.</summary>
    [HttpPost("clock-in")]
    public ActionResult<Attendance> ClockIn([FromBody] ClockRequest req)
    {
        try { return Ok(_svc.ClockIn(req.PersonnelId, BranchId(), Session.CurrentKioskNo, req.Note, UserId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Mesai çıkışı (personelin açık mesaisini kapatır). Açık mesai yoksa 400.</summary>
    [HttpPost("clock-out")]
    public ActionResult<Attendance> ClockOut([FromBody] ClockRequest req)
    {
        try { return Ok(_svc.ClockOut(req.PersonnelId, req.Note, UserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

/// <summary>Mesai giriş/çıkış isteği — personel + opsiyonel not (branch/kiosk sunucuda çözülür).</summary>
public record ClockRequest(int PersonnelId, string? Note);
