using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.QuickProduct;

/// <summary>
/// Hızlı Ürün Tanımlama — WPF QuickProductWindow'un web karşılığı.
/// Üstte ortak seçimler (Kategori / Birim / Vergi grubu) tüm satırlara uygulanır;
/// altta çok satırlı hızlı giriş (Ad, Barkod, Satış Fiyatı). "Kaydet" her satır için
/// ProductService.Save (Products/Edit'teki tam imza) çağırır; ilişkiler minimal/boş.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ProductService _svc = new();
    private readonly MainUnitService _units = new();
    private readonly TaxGroupService _taxes = new();
    private readonly ProductCategoryService _categories = new();
    private readonly CodeSeriesService _codeSeries = new();

    // WPF QuickProductWindow ile aynı: kod boş satırlar için önerilecek sıradaki seri kodu.
    // View bunu boş kod inputlarına yerleştirmek için kullanır.
    public string? NextCode { get; private set; }

    // --- Üst: tüm satırlara uygulanan ortak seçimler ---
    [BindProperty] public int CategoryId { get; set; }        // 0 = kategorisiz
    [BindProperty] public int MainUnitId { get; set; }        // 0 = birim yok (fiyat girilemez)
    [BindProperty] public int SalesTaxGroupId { get; set; }   // 0 = yok
    [BindProperty] public bool IsTaxIncluded { get; set; } = true;

    // --- Alt: çok satırlı hızlı giriş (indexed POST) ---
    [BindProperty] public List<QuickRow> Rows { get; set; } = new();

    public string? Error { get; private set; }
    public string? Message { get; private set; }

    // Lookuplar
    public List<SelectListItem> CategoryOptions { get; private set; } = new();
    public List<SelectListItem> MainUnitOptions { get; private set; } = new();
    public List<SelectListItem> TaxOptions { get; private set; } = new();

    // Gate: SysAdmin/Patron + Feature.ProductBasic (Products/Edit ile aynı).
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        // Boş formda birkaç hazır satır (kullanıcı JS ile artırıp azaltabilir).
        if (Rows.Count == 0)
            Rows = Enumerable.Range(0, 3).Select(_ => new QuickRow()).ToList();
        RefreshNextCode();
        return Page();
    }

    // WPF NextCode() karşılığı: sıradaki seri kodu (varsa) önerilir.
    private void RefreshNextCode()
    {
        try { NextCode = _codeSeries.PeekNext(CodeSeriesType.Product); }
        catch { NextCode = null; }
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        int defCur = new CurrencyService().GetDefaultId() ?? 0;

        // Sadece adı dolu satırlar kaydedilir; tamamen boş satırlar atlanır.
        var toSave = (Rows ?? new()).Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        if (toSave.Count == 0)
        {
            Error = "Kaydedilecek ürün yok. En az bir satıra ürün adı girin.";
            EnsureMinRows();
            return Page();
        }

        int unitId = MainUnitId;                                 // ortak birim
        int? catId = CategoryId > 0 ? CategoryId : null;
        int? taxId = SalesTaxGroupId > 0 ? SalesTaxGroupId : null;

        int saved = 0;
        for (int i = 0; i < toSave.Count; i++)
        {
            var row = toSave[i];
            string name = row.Name!.Trim();
            string code = string.IsNullOrWhiteSpace(row.Code) ? "" : row.Code.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                Error = $"{i + 1}. satır ({name}): kod zorunludur.";
                EnsureMinRows();
                return Page();
            }

            // Satış/alış fiyatı girildiyse birim zorunlu (WPF davranışı: fiyat için BİRİM şart).
            bool hasSale = row.SalePrice is decimal sp && sp > 0;
            bool hasBuy = row.BuyPrice is decimal bp && bp > 0;
            if ((hasSale || hasBuy) && unitId <= 0)
            {
                Error = $"{i + 1}. satır ({name}): fiyat girmek için üstte BİRİM seçin.";
                EnsureMinRows();
                return Page();
            }
            if (row.SalePrice is decimal neg && neg < 0)
            {
                Error = $"{i + 1}. satır ({name}): satış fiyatı negatif olamaz.";
                EnsureMinRows();
                return Page();
            }
            if (row.BuyPrice is decimal bneg && bneg < 0)
            {
                Error = $"{i + 1}. satır ({name}): alış fiyatı negatif olamaz.";
                EnsureMinRows();
                return Page();
            }

            // --- Ürün başlığı (minimal) ---
            var product = new Product
            {
                Id = 0,
                Code = code,
                Name = name,
                MainUnitId = unitId > 0 ? unitId : null,
                SalesTaxGroupId = taxId,
                TrackingType = MindAeonRest.Models.TrackingType.Yok,
                IsActive = true
            };

            // --- Kategoriler ---
            var categoryIds = catId is int c ? new List<int> { c } : new List<int>();

            // --- Birimler (birim seçiliyse tek ana birim satırı) ---
            var units = unitId > 0
                ? new List<ProductUnit> { new() { UnitId = unitId, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 } }
                : new List<ProductUnit>();

            // --- Fiyatlar (satış + alış; genel/kapsamsız) — WPF xaml.cs:149-150 ile aynı ---
            var prices = new List<ProductPrice>();
            if (hasSale)
                prices.Add(new ProductPrice
                {
                    UnitId = unitId,
                    PriceType = PriceType.Satis,
                    Price = row.SalePrice!.Value,
                    IsTaxIncluded = IsTaxIncluded,
                    CurrencyId = defCur == 0 ? null : defCur,
                    IsActive = true
                });
            if (hasBuy)
                prices.Add(new ProductPrice
                {
                    UnitId = unitId,
                    PriceType = PriceType.Alis,
                    Price = row.BuyPrice!.Value,
                    IsTaxIncluded = IsTaxIncluded,
                    CurrencyId = defCur == 0 ? null : defCur,
                    IsActive = true
                });

            // --- Barkodlar (barkod girildi ve birim varsa) ---
            var barcodes = (!string.IsNullOrWhiteSpace(row.Barcode) && unitId > 0)
                ? new List<ProductBarcode> { new() { UnitId = unitId, Barcode = row.Barcode!.Trim() } }
                : new List<ProductBarcode>();

            // --- Kalan ilişkiler minimal/boş (yeni kayıt) ---
            var images = new List<ProductImage>();
            var descriptions = new List<ProductDescription>();
            var recipe = new List<ProductRecipe>();
            var consume = new List<ProductBranchConsume>();
            var options = new List<ProductOption>();

            try
            {
                _svc.Save(product, categoryIds, images, descriptions, units, barcodes, prices, recipe, consume,
                    Session.CurrentUserId, options);
                saved++;

                // WPF xaml.cs:163-164 davranışı: kaydedilen kod o anki seri önerisiyle eşleşiyorsa
                // seriyi ilerlet. Çok-satır girişte her yeni ürün için PeekNext tekrar okunur; böylece
                // ardışık satırlar sırayla bir sonraki seri koduyla eşleşince seri satır satır ilerler.
                try
                {
                    var peek = _codeSeries.PeekNext(CodeSeriesType.Product);
                    if (!string.IsNullOrWhiteSpace(peek) && string.Equals(code, peek, StringComparison.OrdinalIgnoreCase))
                        _codeSeries.Advance(CodeSeriesType.Product);
                }
                catch { /* seri ilerletme başarısızsa ürün kaydını etkileme */ }
            }
            catch (Exception ex)
            {
                Error = $"{i + 1}. satır ({name}): {ex.Message}";
                // Şimdiye dek kaydedilenler kalır; kalan satırlar formda düzenlenebilir kalsın.
                Rows = toSave.Skip(saved).ToList();
                EnsureMinRows();
                return Page();
            }
        }

        // Hepsi kaydedildi → boş forma dön (sıradaki toplu girişe hazır).
        Message = $"{saved} ürün kaydedildi.";
        Rows = new();
        EnsureMinRows();
        return Page();
    }

    private void EnsureMinRows()
    {
        Rows ??= new();
        while (Rows.Count < 3) Rows.Add(new QuickRow());
        RefreshNextCode();   // Advance sonrası güncel seri önerisini yansıt.
    }

    private void LoadLookups()
    {
        CategoryOptions = new() { new SelectListItem("(Kategorisiz)", "0") };
        CategoryOptions.AddRange(_categories.GetAll()
            .Where(c => c.IsActive)
            .Select(c => new SelectListItem($"{c.Name} ({c.Code})", c.Id.ToString())));

        MainUnitOptions = new() { new SelectListItem("(Birim seçiniz)", "0") };
        MainUnitOptions.AddRange(_units.GetAll()
            .Where(u => u.IsActive)
            .Select(u => new SelectListItem(u.Display, u.Id.ToString())));

        TaxOptions = new() { new SelectListItem("(Yok)", "0") };
        TaxOptions.AddRange(_taxes.GetAll()
            .Where(t => t.IsActive)
            .Select(t => new SelectListItem(t.Display, t.Id.ToString())));
    }

    // ---- Form satır tipi ----
    public class QuickRow
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Barcode { get; set; }
        public decimal? SalePrice { get; set; }
        public decimal? BuyPrice { get; set; }
    }
}
