using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Açık hesap (cari) seçimi için müşteri ARAMA. Binlerce müşteride tüm listeyi göndermek yerine
/// sunucuda kod/ad/telefon içinde arayıp en fazla 50 sonuç döndürür (custom lookup). Perakende "PA" hariç.</summary>
[Route("api/customers")]
[Authorize(Roles = "SysAdmin,Kasiyer,Garson")]
public class CustomersController : PosControllerBase
{
    /// <summary>q verilirse kod/ad/telefon içinde arar (Türkçe-duyarsız), en çok 50 sonuç. q boşsa ilk 50.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<CustomerDto>> List([FromQuery] string? q)
    {
        var all = new CustomerService().GetAll().Where(c => c.Code != "PA");
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            all = all.Where(c =>
                (c.Name ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.Code ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.Phone ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return Ok(all.Take(50).Select(c => new CustomerDto(c.Id, c.Code, c.Name)));
    }
}
