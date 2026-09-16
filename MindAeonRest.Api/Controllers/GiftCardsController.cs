using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Hediye çeki: satış (yükleme), bakiye sorgu (kod ile), liste, iptal, ekstre.</summary>
[Route("api/giftcards")]
[Authorize(Roles = "SysAdmin,Kasiyer,Garson")]   // bakiye sorgu/liste tüm servis personeli; satış/iptal aşağıda daraltıldı
public class GiftCardsController : PosControllerBase
{
    private readonly GiftCardService _svc = new();

    /// <summary>Tüm hediye çekleri (yönetim listesi).</summary>
    [HttpGet]
    public ActionResult<IEnumerable<GiftCard>> List() => Ok(_svc.GetAll());

    /// <summary>Koda göre çek (ödeme ekranında bakiye doğrulama).</summary>
    [HttpGet("by-code/{code}")]
    public ActionResult<GiftCard> ByCode(string code)
    {
        var g = _svc.GetByCode(code);
        if (g is null) return NotFound(new { message = "Hediye çeki bulunamadı." });
        if (g.BranchId != BranchId() && !User.IsInRole("SysAdmin")) return NotFound(new { message = "Hediye çeki bulunamadı." });
        return Ok(g);
    }

    /// <summary>Yeni hediye çeki üret + bakiye yükle (satış). Satış bedeli ayrı tahsil edilir.</summary>
    [HttpPost]
    [Authorize(Roles = "SysAdmin,Kasiyer")]
    public ActionResult<GiftCard> Issue([FromBody] GiftCardIssueRequest req)
    {
        try { return Ok(_svc.Issue(req.Amount, req.CurrencyId, req.CustomerId, req.ExpiresUtc, req.Code, req.Note, (Models.PaymentKind)req.SaleKind, req.SaleInstrumentId, UserId)); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Çek hareketleri (ekstre).</summary>
    [HttpGet("{id:int}/movements")]
    public ActionResult<IEnumerable<GiftCardMovement>> Movements(int id)
    {
        var g = _svc.GetById(id);
        if (g is null) return NotFound();
        if (g.BranchId != BranchId() && !User.IsInRole("SysAdmin")) return NotFound();
        return Ok(_svc.GetMovements(id));
    }

    /// <summary>Çeki iptal et (kalan bakiye sıfırlanır).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "SysAdmin,Kasiyer")]
    public IActionResult Cancel(int id)
    {
        var g = _svc.GetById(id);
        if (g is null) return NotFound();
        if (g.BranchId != BranchId() && !User.IsInRole("SysAdmin")) return NotFound();
        try { _svc.Cancel(id, UserId); return Ok(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
