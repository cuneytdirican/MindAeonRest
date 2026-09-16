using System.Collections.Generic;
using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// BULGU 5: kısmi/tam iade e-Faturaya yansımalı. BuildFromLines SAF (DB'siz) olduğundan efektif miktar
/// (Qty - RefundedQty) + oranlı iskonto + matrah/KDV hesabı doğrudan test edilir.
/// </summary>
public class EInvoiceRefundTests
{
    private static OrderLine Line(decimal qty, decimal unit, decimal tax, decimal refunded = 0, decimal discount = 0) => new()
    {
        ProductName = "Ürün",
        Qty = qty,
        UnitPrice = unit,
        TaxRate = tax,
        RefundedQty = refunded,
        DiscountAmount = discount,
        SettledClFicheId = 1,               // ödenmiş (faturalanabilir)
        LineStatus = OrderLineStatus.Teslim // iptal değil
    };

    [Fact]
    public void NoRefund_UsesFullQtyAndTotal()
    {
        var req = EInvoiceService.BuildFromLines(new List<OrderLine> { Line(3, 100m, 10m) });
        Assert.Single(req.Lines);
        Assert.Equal(3m, req.Lines[0].Qty);
        Assert.Equal(300m, req.Lines[0].LineTotal);
        Assert.Equal(300m, req.GrandTotal);
    }

    [Fact]
    public void PartialRefund_DeductsRefundedQtyFromLineAndTotals()
    {
        // Qty 3, 1 iade → efektif 2 adet × 100 = 200. Matrah 200/1.1 = 181,82; KDV 18,18.
        var req = EInvoiceService.BuildFromLines(new List<OrderLine> { Line(3, 100m, 10m, refunded: 1) });
        Assert.Single(req.Lines);
        Assert.Equal(2m, req.Lines[0].Qty);
        Assert.Equal(200m, req.Lines[0].LineTotal);
        Assert.Equal(200m, req.GrandTotal);
        Assert.Equal(181.82m, req.MatrahTotal);
        Assert.Equal(18.18m, req.TaxTotal);
    }

    [Fact]
    public void FullRefund_ExcludesLine()
    {
        var req = EInvoiceService.BuildFromLines(new List<OrderLine> { Line(2, 50m, 10m, refunded: 2) });
        Assert.Empty(req.Lines);
        Assert.Equal(0m, req.GrandTotal);
    }

    [Fact]
    public void PartialRefund_ProratesDiscount()
    {
        // Qty 4, iskonto 40 (tam), 1 iade → efektif 3 adet; oranlı iskonto = 40 × 3/4 = 30.
        // Efektif tutar = 3×100 − 30 = 270.
        var req = EInvoiceService.BuildFromLines(new List<OrderLine> { Line(4, 100m, 20m, refunded: 1, discount: 40m) });
        Assert.Single(req.Lines);
        Assert.Equal(3m, req.Lines[0].Qty);
        Assert.Equal(270m, req.Lines[0].LineTotal);
        Assert.Equal(270m, req.GrandTotal);
    }
}
