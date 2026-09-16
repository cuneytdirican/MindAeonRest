using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.BulkPrice;

/// <summary>
/// Toplu (genel satış) fiyat düzenleme ekranı. WPF ProductBulkPriceWindow ile birebir:
/// tüm ürünler listelenir, her satırda GENEL satış fiyatı düzenlenir, "Toplu Kaydet" ile
/// ProductService.UpsertGeneralSalePrice(productId, unitId, price, taxIncluded, currencyId, userId)
/// çağrılır (boş bırakılan satırlar atlanır → mevcut fiyat değişmez). Genel fiyat şube/liste/müşteri
/// kapsamsızdır; bu yüzden şube/fiyat-listesi filtresi YOKTUR (kapsamlı fiyatlar fiyat matrisi ekranında).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ProductService _products = new();
    private readonly CurrencyService _currencies = new();

    private static readonly CultureInfo Tr = new("tr-TR");

    public List<Row> Items { get; private set; } = new();
    public string DefaultCurrencyCode { get; private set; } = "TRY";

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Load();
        return Page();
    }

    /// <summary>
    /// Toplu kaydet. Her satır için (fiyat kutusu doluysa) WPF SaveAll_Click ile aynı servis/metot:
    /// UpsertGeneralSalePrice. Boş fiyat = atla; geçersiz/negatif fiyat = hata olarak topla; birim yoksa hata.
    /// </summary>
    public IActionResult OnPostSaveAll(int[] productId, string?[] price, bool[] taxIncluded, int[] unitId, int[] currencyId)
    {
        if (!CanAccess) return Forbid();

        // taxIncluded bir <select> olduğundan her satır için MUTLAKA tek değer gelir → dizi hizası garanti.
        int saved = 0, skipped = 0;
        var errors = new List<string>();

        for (int i = 0; i < productId.Length; i++)
        {
            string input = i < price.Length ? (price[i] ?? "") : "";
            if (string.IsNullOrWhiteSpace(input)) { skipped++; continue; }

            if (!Num.TryParseDecimal(input, out var p) || p < 0)
            { errors.Add($"#{productId[i]}: geçersiz fiyat '{input}'"); continue; }

            int uId = i < unitId.Length ? unitId[i] : 0;
            if (uId <= 0) { errors.Add($"#{productId[i]}: birim yok (ürün kartından ana birim ekleyin)"); continue; }

            int curId = i < currencyId.Length ? currencyId[i] : 0;
            bool tax = i < taxIncluded.Length && taxIncluded[i];
            try { _products.UpsertGeneralSalePrice(productId[i], uId, p, tax, curId, Session.CurrentUserId); saved++; }
            catch (Exception ex) { errors.Add($"#{productId[i]}: {ex.Message}"); }
        }

        TempData["BulkPriceResult"] = errors.Count > 0
            ? $"{saved} ürün kaydedildi, {errors.Count} hata: {string.Join(" | ", errors.Take(15))}"
            : $"{saved} ürünün fiyatı kaydedildi. ({skipped} ürün boş bırakıldığı için atlandı.)";

        return RedirectToPage(new { Q });
    }

    private void Load()
    {
        int defCur = _currencies.GetDefaultId() ?? 0;
        DefaultCurrencyCode = _currencies.GetAll().FirstOrDefault(c => c.Id == defCur)?.Code ?? "TRY";

        var prices = _products.GetGeneralSalePrices();

        var rows = new List<Row>();
        foreach (var p in _products.GetAll(includeDeleted: false))
        {
            prices.TryGetValue(p.Id, out var pr);
            rows.Add(new Row
            {
                ProductId = p.Id,
                Code = p.Code,
                Name = p.Name,
                CategoryNames = string.IsNullOrWhiteSpace(p.CategoryNames) ? "(Kategorisiz)" : p.CategoryNames,
                UnitId = pr?.UnitId is int u && u > 0 ? u : (p.MainUnitId ?? 0),
                UnitName = p.MainUnitName,
                CurrencyId = pr?.CurrencyId ?? defCur,
                CurrencyCode = DefaultCurrencyCode,
                TaxIncluded = pr?.IsTaxIncluded ?? true,
                PriceInput = pr is null ? "" : pr.Price.ToString("0.##", Tr)
            });
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r => (r.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                || (r.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Items = rows
            .OrderBy(r => r.CategoryNames, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public class Row
    {
        public int ProductId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? CategoryNames { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public int CurrencyId { get; set; }
        public string? CurrencyCode { get; set; }
        public bool TaxIncluded { get; set; } = true;
        public string PriceInput { get; set; } = "";
    }
}
