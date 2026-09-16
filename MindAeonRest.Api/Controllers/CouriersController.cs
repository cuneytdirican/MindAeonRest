using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Kurye listesi (paket/gel-al sipariş atama için). Tanım/CRUD web back-office'te (Feature.Delivery);
/// burada yalnız POS'un kurye atarken kullanacağı aktif kurye listesi expose edilir.</summary>
[Route("api/couriers")]
[Authorize(Roles = "SysAdmin,Kasiyer,Garson")]
public class CouriersController : PosControllerBase
{
    private readonly CourierService _couriers = new();

    /// <summary>Atanabilir (aktif, silinmemiş) kuryeler.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<CourierDto>> List() =>
        Ok(_couriers.GetAll(false).Where(c => c.IsActive)
            .Select(c => new CourierDto(c.Id, c.Code, c.Name, c.Phone)));
}
