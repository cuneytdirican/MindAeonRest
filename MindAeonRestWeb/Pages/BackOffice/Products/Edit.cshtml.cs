using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Products;

public class EditModel : PageModel
{
    private readonly ProductService _svc = new();
    private readonly MainUnitService _units = new();
    private readonly TaxGroupService _taxes = new();
    private readonly ProductCategoryService _categories = new();
    private readonly CodeSeriesService _codeSeries = new();

    // --- Başlık ---
    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public int MainUnitId { get; set; }             // 0 = seçilmedi (opsiyonel)
    [BindProperty] public int SalesTaxGroupId { get; set; }        // 0 = yok
    [BindProperty] public int PurchaseTaxGroupId { get; set; }     // 0 = yok
    [BindProperty] public int ReturnTaxGroupId { get; set; }       // 0 = yok
    [BindProperty] public int TrackingType { get; set; } = (int)MindAeonRest.Models.TrackingType.Yok;
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public bool IsSpicy { get; set; }

    // --- Kategoriler (çoklu checkbox) ---
    [BindProperty] public List<int> CategoryIds { get; set; } = new();

    // --- Fiyatlar / Birimler / Barkodlar (indexed alt-tablolar) ---
    [BindProperty] public List<PriceInput> Prices { get; set; } = new();
    [BindProperty] public List<UnitInput> UnitRows { get; set; } = new();
    [BindProperty] public List<BarcodeInput> Barcodes { get; set; } = new();

    // --- Zengin sekmeler (WPF paritesi) ---
    [BindProperty] public List<OptionInput> Options { get; set; } = new();
    [BindProperty] public List<RecipeInput> Recipe { get; set; } = new();
    [BindProperty] public List<DescriptionInput> Descriptions { get; set; } = new();
    [BindProperty] public List<ConsumeInput> Consume { get; set; } = new();

    // --- Resimler (mevcutlar hidden ile korunur; yeni yüklemeler IFormFile) ---
    [BindProperty] public List<ImageInput> Images { get; set; } = new();     // mevcut resimler (Id + IsMain + Remove)
    [BindProperty] public int MainImageId { get; set; }                       // seçili ana resim (mevcut resim Id'si; 0 = yeni yüklenen ilk)
    [BindProperty] public bool NewImageIsMain { get; set; }                   // yeni yüklenen ilk resim ana mı

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Lookuplar
    public List<SelectListItem> MainUnitOptions { get; private set; } = new();
    public List<SelectListItem> TaxOptions { get; private set; } = new();
    public List<SelectListItem> UnitOptionsForRows { get; private set; } = new();   // fiyat/birim/barkod satırları için (0 = seçiniz yok)
    public List<CategoryPick> CategoryPicks { get; private set; } = new();
    public List<SelectListItem> ComponentOptions { get; private set; } = new();     // reçete bileşen ürünleri
    public List<BranchPick> BranchPicks { get; private set; } = new();              // şube tüketim depoları
    public List<WarehousePick> WarehousePicks { get; private set; } = new();        // depolar (şube filtreli JS)
    public int DefaultCurrencyId { get; private set; }

    public static IReadOnlyList<(int Value, string Text)> PriceTypeOptions { get; } =
        Enum.GetValues<PriceType>().Select(v => ((int)v, v.ToTr())).ToList();

    public static IReadOnlyList<(int Value, string Text)> TrackingOptions { get; } =
        Enum.GetValues<MindAeonRest.Models.TrackingType>().Select(v => ((int)v, v.ToTr())).ToList();

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (id is int i && i > 0)
        {
            var p = _svc.GetById(i);
            if (p is null) return RedirectToPage("Index");

            Id = p.Id;
            Code = p.Code;
            Name = p.Name;
            Description = p.Description;
            MainUnitId = p.MainUnitId ?? 0;
            SalesTaxGroupId = p.SalesTaxGroupId ?? 0;
            PurchaseTaxGroupId = p.PurchaseTaxGroupId ?? 0;
            ReturnTaxGroupId = p.ReturnTaxGroupId ?? 0;
            TrackingType = (int)p.TrackingType;
            IsActive = p.IsActive;
            IsSpicy = p.IsSpicy;
            CategoryIds = p.CategoryIds.ToList();

            Prices = _svc.GetPrices(p.Id).Select(pr => new PriceInput
            {
                PriceType = (int)pr.PriceType,
                UnitId = pr.UnitId,
                Price = pr.Price,
                ListPrice = pr.ListPrice,
                IsTaxIncluded = pr.IsTaxIncluded,
                BranchName = pr.BranchName,        // salt gösterim: bu form yalnız genel fiyat düzenler
                Scoped = pr.BranchId is not null || pr.CustomerId is not null || pr.CustomerGroupId is not null || pr.PriceListId is not null,
                // Kapsamlı (şube/liste/müşteri) fiyatların TÜM alanlarını KORU (pass-through, matris ekranı bunları yönetir)
                CurrencyId = pr.CurrencyId,
                BranchId = pr.BranchId,
                CustomerId = pr.CustomerId,
                CustomerGroupId = pr.CustomerGroupId,
                PriceListId = pr.PriceListId,
                StartDateTime = pr.StartDateTime,
                EndDateTime = pr.EndDateTime,
                IsActive = pr.IsActive
            }).ToList();

            UnitRows = _svc.GetUnits(p.Id).Select(u => new UnitInput
            {
                UnitId = u.UnitId,
                IsMainUnit = u.IsMainUnit,
                ConversionFactor1 = u.ConversionFactor1,
                ConversionFactor2 = u.ConversionFactor2,
                Width = u.Width,
                Length = u.Length,
                Height = u.Height,
                Weight = u.Weight
            }).ToList();

            Barcodes = _svc.GetBarcodes(p.Id).Select(b => new BarcodeInput
            {
                UnitId = b.UnitId,
                Barcode = b.Barcode
            }).ToList();

            Options = _svc.GetOptions(p.Id).Select(o => new OptionInput
            {
                GroupName = o.GroupName,
                Name = o.Name,
                PriceDelta = o.PriceDelta,
                IsDefault = o.IsDefault,
                SortOrder = o.SortOrder
            }).ToList();

            Recipe = _svc.GetRecipe(p.Id).Select(r => new RecipeInput
            {
                ComponentProductId = r.ComponentProductId,
                Quantity = r.Quantity,
                UnitId = r.UnitId ?? 0
            }).ToList();

            Descriptions = _svc.GetDescriptions(p.Id).Select(d => new DescriptionInput
            {
                Description = d.Description,
                SortOrder = d.SortOrder,
                IsActive = d.IsActive
            }).ToList();

            Consume = _svc.GetConsumeSettings(p.Id).Select(c => new ConsumeInput
            {
                BranchId = c.BranchId,
                BranchName = c.BranchName,
                ConsumeWarehouseId = c.ConsumeWarehouseId ?? 0
            }).ToList();

            var imgs = _svc.GetImages(p.Id);
            Images = imgs.Select(im => new ImageInput { Id = im.Id, IsMain = im.IsMain }).ToList();
            MainImageId = imgs.FirstOrDefault(im => im.IsMain)?.Id ?? (imgs.Count > 0 ? imgs[0].Id : 0);
        }
        else
        {
            DefaultCurrencyId = new CurrencyService().GetDefaultId() ?? 0;
            // Yeni üründe otomatik kod serisinden sıradaki kodu öner (kullanıcı değiştirebilir).
            if (string.IsNullOrWhiteSpace(Code))
            {
                var nextCode = _codeSeries.PeekNext(CodeSeriesType.Product);
                if (nextCode is not null) Code = nextCode;
            }
            // Yeni üründe tüm şubeleri tüketim deposu ataması için listele.
            Consume = _svc.GetConsumeSettings(0).Select(c => new ConsumeInput
            {
                BranchId = c.BranchId,
                BranchName = c.BranchName,
                ConsumeWarehouseId = c.ConsumeWarehouseId ?? 0
            }).ToList();
        }
        return Page();
    }

    public IActionResult OnPost(int? id, List<IFormFile>? imageFiles)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }

        // Boş satırları at.
        Prices = (Prices ?? new()).Where(p => !p.IsEmpty).ToList();
        UnitRows = (UnitRows ?? new()).Where(u => u.UnitId > 0).ToList();
        Barcodes = (Barcodes ?? new()).Where(b => !string.IsNullOrWhiteSpace(b.Barcode)).ToList();
        CategoryIds = (CategoryIds ?? new()).Where(c => c > 0).Distinct().ToList();
        Options = (Options ?? new()).Where(o => !string.IsNullOrWhiteSpace(o.GroupName) && !string.IsNullOrWhiteSpace(o.Name)).ToList();
        Recipe = (Recipe ?? new()).Where(r => r.ComponentProductId > 0 && r.Quantity > 0).ToList();
        Descriptions = (Descriptions ?? new()).Where(d => !string.IsNullOrWhiteSpace(d.Description)).ToList();
        Consume = (Consume ?? new()).ToList();

        int defCur = new CurrencyService().GetDefaultId() ?? 0;

        // Fiyat satırlarını ProductPrice'a dönüştür. Kapsamsız (bu formun düzenlediği) satırlar için
        // birim zorunlu; kapsamlı satırlar (matristen gelen) AYNEN korunur.
        var prices = new List<ProductPrice>();
        for (int i = 0; i < Prices.Count; i++)
        {
            var row = Prices[i];
            if (row.Scoped)
            {
                // Pass-through: matris/şube/müşteri kapsamlı fiyat — hiç dokunma, olduğu gibi geri yaz.
                prices.Add(new ProductPrice
                {
                    UnitId = row.UnitId,
                    PriceType = (PriceType)row.PriceType,
                    Price = row.Price,
                    ListPrice = row.ListPrice,
                    IsTaxIncluded = row.IsTaxIncluded,
                    CurrencyId = row.CurrencyId,
                    BranchId = row.BranchId,
                    CustomerId = row.CustomerId,
                    CustomerGroupId = row.CustomerGroupId,
                    PriceListId = row.PriceListId,
                    StartDateTime = row.StartDateTime,
                    EndDateTime = row.EndDateTime,
                    IsActive = row.IsActive
                });
                continue;
            }

            if (row.UnitId <= 0)
            {
                Error = $"Fiyat {i + 1}. satır: birim seçiniz.";
                return Page();
            }
            if (row.Price < 0)
            {
                Error = $"Fiyat {i + 1}. satır: fiyat negatif olamaz.";
                return Page();
            }
            prices.Add(new ProductPrice
            {
                UnitId = row.UnitId,
                PriceType = (PriceType)row.PriceType,
                Price = row.Price,
                ListPrice = row.ListPrice,
                IsTaxIncluded = row.IsTaxIncluded,
                CurrencyId = defCur == 0 ? null : defCur,
                IsActive = true
                // BranchId/Customer*/PriceListId/StartDateTime/EndDateTime = null → genel fiyat
            });
        }

        var units = new List<ProductUnit>();
        foreach (var u in UnitRows)
        {
            units.Add(new ProductUnit
            {
                UnitId = u.UnitId,
                IsMainUnit = u.IsMainUnit,
                ConversionFactor1 = u.ConversionFactor1 <= 0 ? 1 : u.ConversionFactor1,
                ConversionFactor2 = u.ConversionFactor2 <= 0 ? 1 : u.ConversionFactor2,
                Width = u.Width,
                Length = u.Length,
                Height = u.Height,
                Weight = u.Weight
            });
        }

        var barcodes = Barcodes.Select(b => new ProductBarcode
        {
            UnitId = b.UnitId > 0 ? b.UnitId : MainUnitId,
            Barcode = b.Barcode.Trim()
        }).ToList();

        // --- Seçenekler (varyant) ---
        var options = new List<ProductOption>();
        for (int i = 0; i < Options.Count; i++)
        {
            var o = Options[i];
            options.Add(new ProductOption
            {
                GroupName = o.GroupName.Trim(),
                Name = o.Name.Trim(),
                PriceDelta = o.PriceDelta,
                IsDefault = o.IsDefault,
                SortOrder = o.SortOrder == 0 ? i : o.SortOrder
            });
        }

        // --- Reçete / menü bileşenleri ---
        var recipe = Recipe.Select(r => new ProductRecipe
        {
            ComponentProductId = r.ComponentProductId,
            Quantity = r.Quantity,
            UnitId = r.UnitId == 0 ? null : r.UnitId
        }).ToList();

        // --- Açıklamalar ---
        var descriptions = new List<ProductDescription>();
        for (int i = 0; i < Descriptions.Count; i++)
        {
            var d = Descriptions[i];
            descriptions.Add(new ProductDescription
            {
                Description = d.Description.Trim(),
                SortOrder = d.SortOrder == 0 ? i : d.SortOrder,
                IsActive = d.IsActive
            });
        }

        // --- Şube tüketim depoları ---
        // TÜM şube satırlarını gönder (WPF paritesi): SaveConsume upsert eder; @w null ise
        // mevcut atama temizlenir (0 → null), atanmışsa güncellenir/eklenir.
        var consume = Consume.Where(c => c.BranchId > 0).Select(c => new ProductBranchConsume
        {
            BranchId = c.BranchId,
            BranchName = c.BranchName,
            ConsumeWarehouseId = c.ConsumeWarehouseId == 0 ? null : c.ConsumeWarehouseId
        }).ToList();

        // --- Resimler ---
        // Mevcut resimler: "Remove" işaretli olanları at; kalanları byte'larıyla yeniden gönder (SaveFull DELETE+INSERT eder).
        var images = new List<ProductImage>();
        if (Id > 0)
        {
            var existing = _svc.GetImages(Id);
            var keep = (Images ?? new()).Where(im => !im.Remove).ToList();
            foreach (var im in keep)
            {
                var src = existing.FirstOrDefault(e => e.Id == im.Id);
                if (src is null) continue;
                images.Add(new ProductImage
                {
                    Image = src.Image,
                    IsMain = im.Id == MainImageId,
                    SortOrder = src.SortOrder
                });
            }
        }
        // Yeni yüklenen dosyalar.
        if (imageFiles is not null)
        {
            foreach (var f in imageFiles.Where(f => f is { Length: > 0 }))
            {
                using var ms = new MemoryStream();
                f.CopyTo(ms);
                images.Add(new ProductImage { Image = ms.ToArray(), IsMain = false, SortOrder = images.Count });
            }
        }
        // Ana resim garantisi: hiç ana yoksa ilkini işaretle; yeni resim ana istendiyse ve mevcut ana yoksa uygula.
        if (images.Count > 0 && !images.Any(im => im.IsMain))
        {
            if (NewImageIsMain)
                images.First(im => im.Image.Length > 0).IsMain = true;   // pratikte ilk yeni
            else
                images[0].IsMain = true;
        }

        var product = new Product
        {
            Id = Id,
            Code = Code,
            Name = Name,
            Description = Description,
            MainUnitId = MainUnitId == 0 ? null : MainUnitId,
            SalesTaxGroupId = SalesTaxGroupId == 0 ? null : SalesTaxGroupId,
            PurchaseTaxGroupId = PurchaseTaxGroupId == 0 ? null : PurchaseTaxGroupId,
            ReturnTaxGroupId = ReturnTaxGroupId == 0 ? null : ReturnTaxGroupId,
            TrackingType = Enum.IsDefined(typeof(MindAeonRest.Models.TrackingType), TrackingType)
                ? (MindAeonRest.Models.TrackingType)TrackingType : MindAeonRest.Models.TrackingType.Yok,
            IsActive = IsActive,
            IsSpicy = IsSpicy
        };

        // Yeni kayıtta, önerilen kod serisinin sıradaki koduysa kayıt sonrası seriyi ilerlet (WPF paritesi).
        bool wasNew = product.Id == 0;
        string? seriesCode = wasNew ? _codeSeries.PeekNext(CodeSeriesType.Product) : null;

        try
        {
            // Save = "SaveFull" (Core imzası). Artık tüm ilişkiler formdan toplanıyor.
            _svc.Save(product, CategoryIds, images, descriptions, units, barcodes, prices, recipe, consume,
                Session.CurrentUserId, options);
            // Kod seriden geldiyse seriyi ilerlet (Save, product.Code'u normalize eder).
            if (wasNew && seriesCode is not null && CodeHelper.Normalize(seriesCode) == product.Code)
                _codeSeries.Advance(CodeSeriesType.Product);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    // ---- Mevcut ürün resmini binary olarak servis et (önizleme) ----
    // Rota: /BackOffice/Products/Edit/{id}?handler=Image&imageId=...
    public IActionResult OnGetImage(int? id, int imageId)
    {
        if (!CanAccess) return Forbid();
        if (id is not int pid || pid <= 0) return NotFound();
        var img = _svc.GetImages(pid).FirstOrDefault(i => i.Id == imageId);
        return img is { Image.Length: > 0 } ? File(img.Image, "image/png") : NotFound();
    }

    // ---- "+" hızlı ekleme (AJAX; ilgili Service.Save → JSON döner) ----
    public IActionResult OnPostQuickUnit(string code, string name)
    {
        if (!CanAccess) return Forbid();
        try
        {
            int newId = _units.Create(code ?? "", name ?? "", Session.CurrentUserId);
            var u = _units.GetAll().FirstOrDefault(x => x.Id == newId);
            return new JsonResult(new { ok = true, id = newId, text = u?.Display ?? name });
        }
        catch (Exception ex) { return new JsonResult(new { ok = false, error = ex.Message }); }
    }

    public IActionResult OnPostQuickTax(string code, string name)
    {
        if (!CanAccess) return Forbid();
        try
        {
            var g = _taxes.Save(new TaxGroup { Code = code ?? "", Name = name ?? "", IsActive = true },
                Array.Empty<TaxGroupRate>(), Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = g.Id, text = g.Display });
        }
        catch (Exception ex) { return new JsonResult(new { ok = false, error = ex.Message }); }
    }

    public IActionResult OnPostQuickCategory(string code, string name)
    {
        if (!CanAccess) return Forbid();
        try
        {
            var c = _categories.Save(new ProductCategory { Code = code ?? "", Name = name ?? "", IsActive = true }, Session.CurrentUserId);
            return new JsonResult(new { ok = true, id = c.Id, name = c.Name, code = c.Code });
        }
        catch (Exception ex) { return new JsonResult(new { ok = false, error = ex.Message }); }
    }

    private void LoadLookups()
    {
        MainUnitOptions = new() { new SelectListItem("(Ana birim seçiniz)", "0") };
        MainUnitOptions.AddRange(_units.GetAll()
            .Where(u => u.IsActive)
            .Select(u => new SelectListItem(u.Display, u.Id.ToString())));

        // Satır (fiyat/birim/barkod) birim seçimleri — "0 = seçiniz" ile.
        UnitOptionsForRows = new() { new SelectListItem("(Birim)", "0") };
        UnitOptionsForRows.AddRange(_units.GetAll()
            .Where(u => u.IsActive)
            .Select(u => new SelectListItem(u.Display, u.Id.ToString())));

        TaxOptions = new() { new SelectListItem("(Yok)", "0") };
        TaxOptions.AddRange(_taxes.GetAll()
            .Where(t => t.IsActive)
            .Select(t => new SelectListItem(t.Display, t.Id.ToString())));

        CategoryPicks = _categories.GetAll()
            .Where(c => c.IsActive)
            .Select(c => new CategoryPick { Id = c.Id, Name = c.Name, Code = c.Code })
            .ToList();

        // Reçete bileşenleri: aktif ürünler (kendisi hariç Save aşamasında sorun olmaz; UI'da tüm ürünler).
        ComponentOptions = new() { new SelectListItem("(Bileşen ürün)", "0") };
        ComponentOptions.AddRange(_svc.GetAll()
            .Where(x => x.IsActive)
            .Select(x => new SelectListItem($"{x.Name} ({x.Code})", x.Id.ToString())));

        // Şube tüketim depoları için şube + depo listeleri (depolar şube filtreli JS ile).
        int companyId = Session.CurrentCompany?.Id ?? 0;
        BranchPicks = new BranchService().GetByCompany(companyId)
            .Select(b => new BranchPick { Id = b.Id, Name = b.Display }).ToList();
        WarehousePicks = new WarehouseService().GetAll()
            .Where(w => w.IsActive)
            .Select(w => new WarehousePick { Id = w.Id, BranchId = w.BranchId, Name = w.Display }).ToList();
    }

    // ---- Form satır tipleri ----
    public class PriceInput
    {
        public int PriceType { get; set; } = (int)MindAeonRest.Models.PriceType.Satis;
        public int UnitId { get; set; }
        public decimal Price { get; set; }
        public decimal? ListPrice { get; set; }
        public bool IsTaxIncluded { get; set; } = true;

        // Pass-through / gösterim alanları (kapsamlı fiyatlar için korunur)
        public bool Scoped { get; set; }
        public string? BranchName { get; set; }
        public int? CurrencyId { get; set; }
        public int? BranchId { get; set; }
        public int? CustomerId { get; set; }
        public int? CustomerGroupId { get; set; }
        public int? PriceListId { get; set; }
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public bool IsActive { get; set; } = true;

        public bool IsEmpty => !Scoped && UnitId == 0 && Price == 0 && ListPrice is null or 0;
    }

    public class UnitInput
    {
        public int UnitId { get; set; }
        public bool IsMainUnit { get; set; }
        public decimal ConversionFactor1 { get; set; } = 1;
        public decimal ConversionFactor2 { get; set; } = 1;
        public decimal? Width { get; set; }
        public decimal? Length { get; set; }
        public decimal? Height { get; set; }
        public decimal? Weight { get; set; }
    }

    public class BarcodeInput
    {
        public int UnitId { get; set; }
        public string Barcode { get; set; } = "";
    }

    public class OptionInput
    {
        public string GroupName { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal PriceDelta { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
    }

    public class RecipeInput
    {
        public int ComponentProductId { get; set; }
        public decimal Quantity { get; set; } = 1;
        public int UnitId { get; set; }
    }

    public class DescriptionInput
    {
        public string Description { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ConsumeInput
    {
        public int BranchId { get; set; }
        public string BranchName { get; set; } = "";
        public int ConsumeWarehouseId { get; set; }
    }

    public class ImageInput
    {
        public int Id { get; set; }
        public bool IsMain { get; set; }
        public bool Remove { get; set; }
    }

    public class CategoryPick
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public class BranchPick
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class WarehousePick
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string Name { get; set; } = "";
    }
}
