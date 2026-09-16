using System.Collections.Generic;
using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// Ücretli ürün opsiyonu (priceDelta) toplamı — saf (DB'siz) hesap. OptionPricing.Delta,
/// OrdersController.AddLine'ın kullandığı GERÇEK üretim koduyla aynıdır (tek kaynak).
/// Fiyat-manipülasyon koruması: yalnız SUNUCUDAN gelen (o ürüne ait) geçerli ID'ler sayılır.
/// </summary>
public class OptionPricingTests
{
    private static List<ProductOption> Opts() => new()
    {
        new ProductOption { Id = 1, ProductId = 100, Name = "Küçük", PriceDelta = 0m },
        new ProductOption { Id = 2, ProductId = 100, Name = "Orta",  PriceDelta = 5m },
        new ProductOption { Id = 3, ProductId = 100, Name = "Büyük", PriceDelta = 10m },
    };

    [Fact]
    public void Delta_SumsSelectedOptions()
    {
        // Orta(+5) + Büyük(+10) = 15
        Assert.Equal(15m, OptionPricing.Delta(Opts(), new[] { 2, 3 }));
    }

    [Fact]
    public void Delta_NullOrEmptySelection_IsZero()
    {
        Assert.Equal(0m, OptionPricing.Delta(Opts(), null));
        Assert.Equal(0m, OptionPricing.Delta(Opts(), new int[0]));
    }

    [Fact]
    public void Delta_ZeroDeltaOption_AddsNothing()
    {
        // Küçük(0) → tutar değişmez
        Assert.Equal(0m, OptionPricing.Delta(Opts(), new[] { 1 }));
    }

    [Fact]
    public void Delta_IgnoresUnknownOrForeignIds()
    {
        // 99 = uydurma/başka ürünün opsiyonu → yok sayılır; yalnız Orta(+5) geçerli
        Assert.Equal(5m, OptionPricing.Delta(Opts(), new[] { 2, 99, 12345 }));
    }

    [Fact]
    public void Delta_DeduplicatesRepeatedIds()
    {
        // Aynı ID iki kez → bir kez sayılır (Büyük +10)
        Assert.Equal(10m, OptionPricing.Delta(Opts(), new[] { 3, 3 }));
    }

    [Fact]
    public void Delta_AllForeignIds_IsZero()
    {
        // Hiçbiri bu ürüne ait değil → fiyat manipülasyonu imkânsız (delta 0)
        Assert.Equal(0m, OptionPricing.Delta(Opts(), new[] { 500, 600 }));
    }
}
