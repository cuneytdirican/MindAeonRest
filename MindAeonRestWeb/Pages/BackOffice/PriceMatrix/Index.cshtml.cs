using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.PriceMatrix;

/// <summary>
/// Şube · Fiyat Listesi Matrisi. WPF ProductPriceMatrixWindow ile aynı servis çağrılarını kullanır:
/// okuma <see cref="ProductService.GetScopedPrices"/> (kapsamlı fiyat satırları), satır güncelleme
/// <see cref="ProductService.UpdatePriceById"/>, yeni kapsamlı satır <see cref="ProductService.AddScopedPrice"/>.
/// Eksenler <see cref="BranchService.GetByCompany"/> ve <see cref="PriceListService.GetAll"/> ile gelir.
///
/// WPF penceresi kapsamlı fiyatları düz bir liste olarak gösterir; web sürümü bunları
/// ürün(satır) × eksen(sütun) matrisine PIVOT eder. Her hücre PriceType=Satış, seçilen eksene
/// (şube VEYA fiyat listesi) ait GENEL para birimli fiyat satırıdır. Boş hücreye fiyat yazılıp
/// kaydedilirse yeni bir kapsamlı satır oluşur; dolu hücre düzenlenirse ilgili satır güncellenir.
/// Genel (kapsamsız) satış fiyatı Toplu Fiyat ekranında düzenlenir; burada sadece kapsamlı fiyatlar.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ProductService _products = new();
    private readonly BranchService _branchSvc = new();
    private readonly PriceListService _priceListSvc = new();
    private readonly CurrencyService _currencies = new();

    private static readonly CultureInfo Tr = new("tr-TR");

    public const string AxisBranch = "branch";
    public const string AxisList = "list";

    [BindProperty(SupportsGet = true)] public string Axis { get; set; } = AxisBranch;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<Col> Columns { get; private set; } = new();
    public List<Row> Rows { get; private set; } = new();
    public int DefaultCurrencyId { get; private set; }
    public string DefaultCurrencyCode { get; private set; } = "TRY";
    public string? Message { get; private set; }

    /// <summary>Kapsamlı fiyat ekle formu için döviz listesi (WPF ProductPriceEditWindow DÖVİZ).</summary>
    public List<Currency> Currencies { get; private set; } = new();
    /// <summary>Kapsamlı fiyat ekle formunda ürün seçimi (birim + ana birim ile).</summary>
    public List<ProductOption> ProductOptions { get; private set; } = new();

    private bool IsListAxis => string.Equals(Axis, AxisList, StringComparison.OrdinalIgnoreCase);

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        if (TempData["PriceMatrixResult"] is string msg) Message = msg;
        Load();
        return Page();
    }

    /// <summary>
    /// Değişen hücreleri kaydeder. Her hücre kendi kimliğini (priceId, productId, unitId, colId) taşır:
    /// priceId&gt;0 ise <see cref="ProductService.UpdatePriceById"/>, aksi halde (ve fiyat girildiyse)
    /// <see cref="ProductService.AddScopedPrice"/>. Boş bırakılan yeni hücre atlanır (mevcut fiyat bozulmaz).
    /// </summary>
    public IActionResult OnPostSaveAll(int[] priceId, int[] productId, int[] unitId, int[] colId,
        string?[] price, string?[] tax, string?[] orig)
    {
        if (!CanAccess) return Forbid();

        int defCur = _currencies.GetDefaultId() ?? 0;
        int saved = 0, added = 0, skipped = 0;
        var errors = new List<string>();

        int n = productId.Length;
        for (int i = 0; i < n; i++)
        {
            string input = i < price.Length ? (price[i] ?? "").Trim() : "";
            string original = i < orig.Length ? (orig[i] ?? "").Trim() : "";
            int pid = i < priceId.Length ? priceId[i] : 0;
            int uId = i < unitId.Length ? unitId[i] : 0;
            int col = i < colId.Length ? colId[i] : 0;
            bool taxIncluded = i < tax.Length && !string.IsNullOrEmpty(tax[i]) && tax[i] == "1";

            // Değişmemiş hücreyi atla (gereksiz UPDATE/INSERT yok).
            if (input == original) { continue; }

            if (input.Length == 0)
            {
                // Yeni hücre boş → atla. (Var olan satırı boşaltarak "silme" burada desteklenmez;
                // WPF silme akışı ayrı bir 🗑 butonudur, matris hücresi silmez.)
                if (pid <= 0) { skipped++; continue; }
                errors.Add($"#{productId[i]}: fiyatı boşaltmak için kayıt silme kullanın.");
                continue;
            }

            if (!Num.TryParseDecimal(input, out var p) || p < 0)
            { errors.Add($"#{productId[i]}: geçersiz fiyat '{input}'"); continue; }

            if (pid > 0)
            {
                try { _products.UpdatePriceById(pid, p, taxIncluded, Session.CurrentUserId); saved++; }
                catch (Exception ex) { errors.Add($"#{productId[i]}: {ex.Message}"); }
                continue;
            }

            // Yeni kapsamlı satır. Birim zorunlu (WPF AddScopedPrice de birim ister).
            if (uId <= 0) { errors.Add($"#{productId[i]}: birim yok (ürün kartından ana birim ekleyin)"); continue; }
            if (col <= 0) { errors.Add($"#{productId[i]}: eksen (şube/liste) seçili değil."); continue; }

            var np = new ProductPrice
            {
                UnitId = uId,
                PriceType = PriceType.Satis,
                Price = p,
                IsTaxIncluded = taxIncluded,
                CurrencyId = defCur == 0 ? null : defCur,
                BranchId = IsListAxis ? null : col,
                PriceListId = IsListAxis ? col : null
            };
            try { _products.AddScopedPrice(productId[i], np, Session.CurrentUserId); added++; }
            catch (Exception ex) { errors.Add($"#{productId[i]}: {ex.Message}"); }
        }

        TempData["PriceMatrixResult"] = errors.Count > 0
            ? $"{saved} güncellendi, {added} eklendi, {errors.Count} hata: {string.Join(" | ", errors.Take(15))}"
            : $"{saved} fiyat güncellendi, {added} yeni fiyat eklendi. ({skipped} boş hücre atlandı.)";

        return RedirectToPage(new { Axis, Q });
    }

    /// <summary>
    /// Satır/hücre bazında kapsamlı fiyatı siler (WPF ProductPriceMatrixWindow 🗑 Delete_Click ile aynı:
    /// <see cref="ProductService.DeletePriceById"/> soft-delete). Sadece dolu (priceId&gt;0) hücrede anlamlı.
    /// </summary>
    public IActionResult OnPostDelete(int priceId)
    {
        if (!CanAccess) return Forbid();
        if (priceId <= 0)
        {
            TempData["PriceMatrixResult"] = "Silinecek fiyat satırı yok (hücre zaten boş).";
            return RedirectToPage(new { Axis, Q });
        }
        try
        {
            _products.DeletePriceById(priceId, Session.CurrentUserId);
            TempData["PriceMatrixResult"] = "Kapsamlı fiyat satırı silindi.";
        }
        catch (Exception ex) { TempData["PriceMatrixResult"] = $"Silme hatası: {ex.Message}"; }
        return RedirectToPage(new { Axis, Q });
    }

    /// <summary>
    /// Kapsamlı fiyat ekler — WPF ProductPriceEditWindow'un web karşılığı: Başlangıç/Bitiş (tarih+saat),
    /// fiyat tipi (Satış/Alış), döviz, liste fiyatı, KDV dahil, ürün+birim ve eksen kapsamı (şube VEYA
    /// fiyat listesi). Hepsi <see cref="ProductService.AddScopedPrice"/>'a geçilir.
    /// NOT: Core'daki AddScopedPrice INSERT'ü CustomerId/CustomerGroupId'yi NULL sabitler; müşteri/grup
    /// kapsamı bu servisle kalıcı olmadığından burada sunulmaz (Core değiştirilmeden desteklenemez).
    /// </summary>
    public IActionResult OnPostAddScoped(int productId, int unitId, int colId, string priceType,
        string? price, int? currencyId, string? listPrice, bool taxIncluded, string? start, string? end)
    {
        if (!CanAccess) return Forbid();

        var errors = new List<string>();
        if (productId <= 0) errors.Add("Ürün seçin.");
        if (unitId <= 0) errors.Add("Birim seçin (ürün kartından ana birim ekleyin).");
        if (colId <= 0) errors.Add(IsListAxis ? "Fiyat listesi seçin." : "Şube seçin.");

        PriceType type = string.Equals(priceType, "Alis", StringComparison.OrdinalIgnoreCase)
            ? PriceType.Alis : PriceType.Satis;

        if (!Num.TryParseDecimal(price, out var p) || p < 0)
            errors.Add($"Geçersiz fiyat '{price}'.");

        decimal? lp = null;
        if (!string.IsNullOrWhiteSpace(listPrice))
        {
            if (!Num.TryParseDecimal(listPrice, out var lpv) || lpv < 0)
                errors.Add($"Geçersiz liste fiyatı '{listPrice}'.");
            else lp = lpv;
        }

        DateTime? startDt = ParseDateTime(start, "Başlangıç", errors);
        DateTime? endDt = ParseDateTime(end, "Bitiş", errors);
        if (startDt.HasValue && endDt.HasValue && endDt < startDt)
            errors.Add("Bitiş, başlangıçtan önce olamaz.");

        int cur = currencyId is int cid && cid > 0 ? cid : (_currencies.GetDefaultId() ?? 0);

        if (errors.Count > 0)
        {
            TempData["PriceMatrixResult"] = "Kapsamlı fiyat eklenemedi: " + string.Join(" | ", errors);
            return RedirectToPage(new { Axis, Q });
        }

        var np = new ProductPrice
        {
            UnitId = unitId,
            PriceType = type,
            Price = p,
            ListPrice = lp,
            IsTaxIncluded = taxIncluded,
            CurrencyId = cur == 0 ? null : cur,
            BranchId = IsListAxis ? null : colId,
            PriceListId = IsListAxis ? colId : null,
            StartDateTime = startDt,
            EndDateTime = endDt
        };
        try
        {
            _products.AddScopedPrice(productId, np, Session.CurrentUserId);
            TempData["PriceMatrixResult"] = $"Kapsamlı {type.ToTr()} fiyatı eklendi.";
        }
        catch (Exception ex) { TempData["PriceMatrixResult"] = $"Ekleme hatası: {ex.Message}"; }
        return RedirectToPage(new { Axis, Q });
    }

    /// <summary>datetime-local (yyyy-MM-ddTHH:mm) veya tr biçimini kabul eden hoşgörülü ayrıştırıcı.</summary>
    private static DateTime? ParseDateTime(string? text, string label, List<string> errors)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)) return v;
        if (DateTime.TryParse(text, Tr, DateTimeStyles.None, out v)) return v;
        errors.Add($"{label} tarih-saat biçimi geçersiz.");
        return null;
    }

    private void Load()
    {
        DefaultCurrencyId = _currencies.GetDefaultId() ?? 0;
        Currencies = _currencies.GetAll();
        DefaultCurrencyCode = Currencies.FirstOrDefault(c => c.Id == DefaultCurrencyId)?.Code ?? "TRY";

        // --- Sütunlar (eksen) ---
        if (IsListAxis)
        {
            Columns = _priceListSvc.GetAll(includeDeleted: false)
                .Select(l => new Col { Id = l.Id, Title = l.Name, Code = l.Code })
                .ToList();
        }
        else
        {
            int companyId = Session.CurrentCompany?.Id ?? 0;
            Columns = (companyId == 0 ? new List<CompanyBranch>() : _branchSvc.GetByCompany(companyId))
                .Select(b => new Col { Id = b.Id, Title = b.Name, Code = b.Code })
                .ToList();
        }

        // --- Kapsamlı fiyatları PIVOT için indeksle ---
        // Anahtar: (ProductId, ColumnId). Sadece PriceType=Satış, seçilen eksene ait,
        // diğer eksenin boş (null) olduğu satırlar (temiz tek-eksen hücreleri).
        var scoped = _products.GetScopedPrices()
            .Where(sp => sp.PriceType == PriceType.Satis);

        var cells = new Dictionary<(int Product, int Col), ProductPrice>();
        foreach (var sp in scoped)
        {
            int? col;
            bool otherAxisEmpty;
            if (IsListAxis) { col = sp.PriceListId; otherAxisEmpty = sp.BranchId is null; }
            else { col = sp.BranchId; otherAxisEmpty = sp.PriceListId is null; }

            if (col is not int c || !otherAxisEmpty) continue;
            var key = (sp.ProductId, c);
            // Aynı hücre için birden çok satır varsa ilkini (GetScopedPrices sıralı) koru.
            cells.TryAdd(key, sp);
        }

        // --- Satırlar (ürünler) + kapsamlı ekle formu için ürün seçenekleri ---
        var rows = new List<Row>();
        var options = new List<ProductOption>();
        foreach (var prod in _products.GetAll(includeDeleted: false))
        {
            options.Add(new ProductOption
            {
                Id = prod.Id,
                Label = $"{prod.Code} · {prod.Name}",
                MainUnitId = prod.MainUnitId ?? 0,
                MainUnitName = prod.MainUnitName
            });
            var row = new Row
            {
                ProductId = prod.Id,
                Code = prod.Code,
                Name = prod.Name,
                CategoryNames = string.IsNullOrWhiteSpace(prod.CategoryNames) ? "(Kategorisiz)" : prod.CategoryNames,
                MainUnitId = prod.MainUnitId ?? 0,
                MainUnitName = prod.MainUnitName
            };
            foreach (var col in Columns)
            {
                cells.TryGetValue((prod.Id, col.Id), out var sp);
                row.Cells.Add(new Cell
                {
                    ColId = col.Id,
                    PriceId = sp?.Id ?? 0,
                    // Var olan satır kendi birimini korur; yeni hücre ürünün ana birimini kullanır.
                    UnitId = sp?.UnitId ?? (prod.MainUnitId ?? 0),
                    TaxIncluded = sp?.IsTaxIncluded ?? true,
                    PriceInput = sp is null ? "" : sp.Price.ToString("0.##", Tr)
                });
            }
            rows.Add(row);
        }
        ProductOptions = options.OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r => (r.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                || (r.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Rows = rows
            .OrderBy(r => r.CategoryNames, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public class Col
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public class Row
    {
        public int ProductId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? CategoryNames { get; set; }
        public int MainUnitId { get; set; }
        public string? MainUnitName { get; set; }
        public List<Cell> Cells { get; set; } = new();
    }

    public class Cell
    {
        public int ColId { get; set; }
        public int PriceId { get; set; }
        public int UnitId { get; set; }
        public bool TaxIncluded { get; set; } = true;
        public string PriceInput { get; set; } = "";
    }

    /// <summary>Kapsamlı fiyat ekle formunda ürün seçeneği (ana birim otomatik).</summary>
    public class ProductOption
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public int MainUnitId { get; set; }
        public string? MainUnitName { get; set; }
    }
}
