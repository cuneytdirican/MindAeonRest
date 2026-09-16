using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>
/// KIOSK (terminal) bazlı vardiya uçları: aç/kapa, açık vardiya sorgu, rapor, liste.
/// Vardiya = kasiyer/admin işi → sınıf bazında yalnız SysAdmin/Kasiyer. KioskNo JWT'den (Session.CurrentKioskNo)
/// gelir; istemci değiştiremez (KIOSKNO sözleşmesi). branch JWT bağlamından (BranchId()) çözülür.
/// </summary>
[Route("api/shifts")]
[Authorize(Roles = "SysAdmin,Kasiyer")]
public class ShiftsController : PosControllerBase
{
    private readonly ShiftService _svc = new();

    /// <summary>Bu terminaldeki (branch+kiosk) AÇIK vardiya; yoksa null.</summary>
    [HttpGet("current")]
    public ActionResult<Shift> Current()
        => Ok(_svc.GetOpenShift(BranchId(), Session.CurrentKioskNo));

    /// <summary>Terminalde yeni vardiya aç (açılış kasası ile). Açık vardiya zaten varsa 400.</summary>
    [HttpPost("open")]
    public ActionResult<Shift> Open([FromBody] OpenShiftRequest req)
    {
        try { return Ok(_svc.OpenShift(BranchId(), Session.CurrentKioskNo, req.OpeningFloat, UserId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Vardiyayı kapat (sayılan nakit + not). Sahiplik: vardiya çağıranın şubesine ait olmalı.</summary>
    [HttpPost("{id:int}/close")]
    public ActionResult Close(int id, [FromBody] CloseShiftRequest req)
    {
        // IDOR: yalnız kendi şubesinin vardiyası kapatılabilir.
        var shift = _svc.ListShifts(BranchId(), null, null).FirstOrDefault(s => s.Id == id);
        if (shift is null || shift.BranchId != BranchId()) return NotFound(new { message = "Vardiya bulunamadı." });
        try { _svc.CloseShift(id, req.CountedCash, req.Note, UserId); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Vardiya raporu (ödeme yöntemi + tahsil eden kullanıcı kırılımı, beklenen kasa).</summary>
    [HttpGet("{id:int}/report")]
    public ActionResult<ShiftReport> Report(int id)
    {
        // IDOR: rapor yalnız kendi şubesinin vardiyası için.
        var shift = _svc.ListShifts(BranchId(), null, null).FirstOrDefault(s => s.Id == id);
        if (shift is null || shift.BranchId != BranchId()) return NotFound(new { message = "Vardiya bulunamadı." });
        try { return Ok(_svc.GetReport(id)); }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
    }

    /// <summary>Şubedeki vardiyalar — (opsiyonel) açılış tarihi aralığı (yerel gün) filtreli.</summary>
    [HttpGet("list")]
    public ActionResult<IEnumerable<Shift>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(_svc.ListShifts(BranchId(), from, to));
}

/// <summary>Vardiya açma isteği — açılış kasası (terminalde duran nakit).</summary>
public record OpenShiftRequest(decimal OpeningFloat);

/// <summary>Vardiya kapama isteği — sayılan nakit + opsiyonel not.</summary>
public record CloseShiftRequest(decimal CountedCash, string? Note);
