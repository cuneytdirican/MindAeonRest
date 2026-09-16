using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>
/// FAZ 4b Stok Zekâsı — mobil sayım ucu. Barkod/kod ile ürün çözümü + sayım evrağı (DocType.Sayim)
/// oluşturma. Sayım kesinleştirme (post=true) sistem stoğu ile sayılan fark kadar düzeltme üretir
/// (DocumentPostingService.PostCount). Yönetici/Kasiyer erişir; şube JWT bağlamından zorlanır (IDOR koruması).
/// </summary>
[Authorize(Roles = "SysAdmin,Kasiyer")]
[Route("api/stock")]
public class StockController : PosControllerBase
{
    private readonly ProductService _products = new();

    /// <summary>Yalnız SysAdmin şube-aşırı sorgulayabilir; diğer roller için istek şubesi yok sayılır → oturum şubesi.</summary>
    private bool CanCrossBranch => Session.IsSysAdmin;

    /// <summary>İstenen şubeyi yetkiye göre çözer: SysAdmin verdiği branchId'yi (yoksa oturum şubesi) kullanır;
    /// diğer roller HER ZAMAN oturum şubesine zorlanır (şube-aşırı IDOR engellenir — ReportsController deseni).</summary>
    private int ScopedBranch(int? q) => CanCrossBranch ? (q ?? Session.CurrentBranch?.Id ?? 0) : (Session.CurrentBranch?.Id ?? 0);

    /// <summary>Sayım için seçilebilir depolar (bağlam şubesinin aktif depoları; şube JWT'den zorlanır).</summary>
    [HttpGet("warehouses")]
    public ActionResult<object> Warehouses([FromQuery] int? branchId)
    {
        int b = ScopedBranch(branchId);
        var list = new WarehouseService().GetAll()
            .Where(w => w.IsActive && (b <= 0 || w.BranchId == b))
            .Select(w => new { id = w.Id, name = w.Display, branchId = w.BranchId });
        return Ok(list);
    }

    /// <summary>Okutulan barkodu/ürün kodunu ürüne çözer.</summary>
    [HttpGet("product-by-barcode/{code}")]
    public ActionResult<object> ProductByBarcode(string code)
    {
        var p = _products.ResolveByBarcodeOrCode(Uri.UnescapeDataString(code ?? ""));
        if (p is null) return NotFound(new { message = "Ürün bulunamadı." });
        return Ok(new { productId = p.Value.Id, code = p.Value.Code, name = p.Value.Name });
    }

    /// <summary>Sayım listesini bir Sayım evrağı olarak kaydeder; post=true ise kesinleştirir (stok düzeltir).</summary>
    [HttpPost("count")]
    public ActionResult<object> SubmitCount([FromBody] StockCountRequest req)
    {
        // Şube JWT oturumundan ZORLANIR: SysAdmin dışında istek gövdesindeki BranchId yok sayılır (şube-aşırı IDOR koruması).
        int branch = ScopedBranch(req.BranchId);
        if (branch <= 0) return BadRequest(new { message = "Şube gerekli." });
        if (req.WarehouseId <= 0) return BadRequest(new { message = "Depo gerekli." });

        // Depo, çözülen şubeye AİT olmalı; başka şubenin deposuna yazım engellenir (PosControllerBase deseni).
        var wh = new WarehouseService().GetAll().FirstOrDefault(w => w.Id == req.WarehouseId);
        if (wh is null || wh.BranchId != branch)
            return StatusCode(403, new { message = "Depo bu şubeye ait değil." });

        var lines = (req.Lines ?? new())
            .Where(l => l.ProductId > 0)
            .Select(l => new DocumentLine { ProductId = l.ProductId, Qty = l.CountedQty })
            .ToList();
        if (lines.Count == 0) return BadRequest(new { message = "En az bir sayım satırı gerekli." });

        var doc = new Document
        {
            DocType = DocType.Sayim,
            BranchId = branch,
            WarehouseId = req.WarehouseId,
            DocDate = DateTime.Now,
            Status = DocStatus.Taslak,
            Note = "Mobil sayım"
        };
        try
        {
            new DocumentService().Save(doc, lines, UserId);
            if (req.Post) new DocumentPostingService().PostCount(doc.Id, UserId);
            return Ok(new { documentId = doc.Id, fisNo = doc.FisNo, posted = req.Post });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public record StockCountLine(int ProductId, decimal CountedQty);
public record StockCountRequest(int? BranchId, int WarehouseId, List<StockCountLine>? Lines, bool Post = false);
