using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[Route("api/menu")]
public class MenuController : PosControllerBase
{
    private readonly PosCatalogService _catalog = new();
    private readonly ProductService _products = new();
    private readonly ProductCategoryService _cats = new();

    /// <summary>Menü kategorileri (resim ayrı uçtan).</summary>
    [HttpGet("categories")]
    public ActionResult<IEnumerable<CategoryDto>> Categories() =>
        Ok(_catalog.GetCategories().Select(c => new CategoryDto(c.Id, c.Name, c.Image is { Length: > 0 })));

    /// <summary>Şubeye göre satış ürünleri (fiyat + KDV; +/- için fiyatı taşır).</summary>
    [HttpGet("products")]
    public ActionResult<IEnumerable<MenuProductDto>> Products([FromQuery] int? priceListId)
    {
        var list = _catalog.GetProducts(BranchId(), priceListId).Select(p => new MenuProductDto(
            p.Id, p.Code, p.Name, p.Description, p.Price, p.TaxRate, p.IsSalesStopped, p.IsMenu,
            p.Image is { Length: > 0 }, p.CategoryIds.ToList()));
        return Ok(list);
    }

    /// <summary>Ürün varyant/seçenekleri (GroupName ile gruplu; POS her gruptan bir seçim sorar).</summary>
    [HttpGet("products/{id:int}/options")]
    public ActionResult<IEnumerable<object>> Options(int id) =>
        Ok(_products.GetOptions(id).Select(o => new { id = o.Id, groupName = o.GroupName, name = o.Name, priceDelta = o.PriceDelta, isDefault = o.IsDefault }));

    /// <summary>Ürün ana görseli (varsa).</summary>
    [HttpGet("products/{id:int}/image")]
    [ResponseCache(Duration = 3600)]
    public IActionResult Image(int id)
    {
        var img = _products.GetImages(id).FirstOrDefault();
        if (img?.Image is not { Length: > 0 }) return NotFound();
        return File(img.Image, "image/png");
    }

    /// <summary>Menü kategorisi görseli (varsa).</summary>
    [HttpGet("categories/{id:int}/image")]
    [ResponseCache(Duration = 3600)]
    public IActionResult CategoryImage(int id)
    {
        var img = _cats.GetImage(id);
        if (img is not { Length: > 0 }) return NotFound();
        return File(img, "image/png");
    }
}
