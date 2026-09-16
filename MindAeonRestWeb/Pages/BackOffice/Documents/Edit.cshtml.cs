using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Documents;

/// <summary>Jenerik evrak edit — WPF DocumentEditWindow birebir karşılığı.
/// Başlık (BelgeNo/Tarih/Belge Tarihi/Döviz/Şube/Depo/Proje/Personel/Cari + Transfer'de Hedef Şube/Depo + Gider'de Gider Tipi),
/// indexed satır alt-tablosu (Ürün + Miktar + Fiyat + İskonto% + KDV Grubu). Kaydet → DocumentService.Save.
/// Satır Matrah/KDV/Toplam ve alt toplamlar JS ile; server yeniden hesaplar (Document.Recalc / DocumentLine.Recalc).
/// Kaydedilmiş belgede "Yazdır" (?print=1 → @@media print bloğu + otomatik window.print). Onaylı/iptal evrak salt-inceleme (Save engellenir).</summary>
public class EditModel : PageModel
{
    private readonly DocumentService _docs = new();
    private readonly ProductService _productService = new();
    private readonly TaxGroupService _taxService = new();
    private readonly DocumentPostingService _posting = new();

    // --- Başlık ---
    [BindProperty(SupportsGet = true)] public DocType Type { get; set; } = DocType.SatisFatura;
    [BindProperty] public string? BelgeNo { get; set; }
    [BindProperty] public string DocDate { get; set; } = "";
    [BindProperty] public string BelgeTarihi { get; set; } = "";
    [BindProperty] public int CurrencyId { get; set; }
    [BindProperty] public int BranchId { get; set; }
    [BindProperty] public int WarehouseId { get; set; }
    [BindProperty] public int ProjectId { get; set; }
    [BindProperty] public int PersonnelId { get; set; }
    [BindProperty] public int CustomerId { get; set; }
    [BindProperty] public int AddressId { get; set; }
    [BindProperty] public int TargetBranchId { get; set; }
    [BindProperty] public int TargetWarehouseId { get; set; }
    [BindProperty] public int ExpenseTypeId { get; set; }
    [BindProperty] public string? Note { get; set; }

    // --- Satırlar (indexed alt-tablo) ---
    [BindProperty] public List<LineInput> Lines { get; set; } = new();

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public bool ReadOnlyDoc { get; private set; }   // onaylı/iptal → salt-inceleme
    public string? FisNo { get; private set; }
    public DocStatus Status { get; private set; } = DocStatus.Taslak;
    public string? Error { get; private set; }
    public string Title => Type.ToTr();

    // --- Yazdırma (Print) ---
    /// <summary>?print=1 ile açıldığında baskı bloğu render edilir + JS otomatik window.print() tetikler.</summary>
    [BindProperty(SupportsGet = true)] public bool Print { get; set; }
    public Document? PrintDoc { get; private set; }
    public List<DocumentLine> PrintLines { get; private set; } = new();
    public string PrintCompanyName { get; private set; } = "";
    public string? PrintBranchName { get; private set; }
    /// <summary>Kaydedilmiş bir belge var mı? (Print bloğu ve buton için).</summary>
    public bool CanPrint => Id > 0 && PrintDoc is not null;

    // Tip-koşullu alan görünürlüğü (WPF ApplyTypeVisibility birebir).
    public bool NeedsCustomer => Type.NeedsCustomer();
    public bool IsTransfer => Type == DocType.DepoTransfer;
    public bool IsGider => Type == DocType.Gider;
    public bool IsPurchase => Type is DocType.AlisFatura or DocType.AlisSiparis or DocType.AlisIrsaliye or DocType.AlisIadeFatura;

    // Lookuplar
    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<SelectListItem> WarehouseOptions { get; private set; } = new();
    public List<SelectListItem> TargetWarehouseOptions { get; private set; } = new();
    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> ProjectOptions { get; private set; } = new();
    public List<SelectListItem> PersonnelOptions { get; private set; } = new();
    public List<SelectListItem> CustomerOptions { get; private set; } = new();
    public List<SelectListItem> AddressOptions { get; private set; } = new();
    public List<SelectListItem> ExpenseOptions { get; private set; } = new();
    public List<SelectListItem> ProductOptions { get; private set; } = new();
    public List<SelectListItem> TaxOptions { get; private set; } = new();

    /// <summary>JS için: warehouse[branchId] = [{v,t}], product[id]={sales,purchase}, taxRates[groupId]=[{r,s,e}].</summary>
    public string WarehousesByBranchJson { get; private set; } = "{}";
    public string ProductTaxMapJson { get; private set; } = "{}";
    public string TaxRatesJson { get; private set; } = "{}";
    /// <summary>JS için: adres[customerId] = [{v,t}] (WPF LoadAddresses karşılığı, cari seçilince süzülür).</summary>
    public string AddressesByCustomerJson { get; private set; } = "{}";

    // Döviz çevrim (WPF UpdateFx): en güncel USD/EUR satış kuru + currencyId→kod haritası. Salt görsel bilgi.
    public decimal? FxUsd { get; private set; }
    public decimal? FxEur { get; private set; }
    public string CurrencyCodesJson { get; private set; } = "{}";

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        DocDate = DateTime.Today.ToString("yyyy-MM-dd");
        CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
        BranchId = Session.CurrentBranch?.Id ?? (BranchOptions.Count > 0 ? int.Parse(BranchOptions[0].Value) : 0);

        if (id is int i && i > 0)
        {
            var d = _docs.GetById(i);
            if (d is null) return RedirectToPage("Index", new { type = (int)Type });
            Id = d.Id; Type = d.DocType; FisNo = d.FisNo; Status = d.Status;
            ReadOnlyDoc = d.Status != DocStatus.Taslak;
            BelgeNo = d.BelgeNo;
            DocDate = d.DocDate.ToString("yyyy-MM-dd");
            BelgeTarihi = d.BelgeTarihi?.ToString("yyyy-MM-dd") ?? "";
            CurrencyId = d.CurrencyId;
            BranchId = d.BranchId;
            WarehouseId = d.WarehouseId ?? 0;
            ProjectId = d.ProjectId ?? 0;
            PersonnelId = d.PersonnelId ?? 0;
            CustomerId = d.CustomerId ?? 0;
            AddressId = d.AddressId ?? 0;
            TargetBranchId = d.TargetBranchId ?? 0;
            TargetWarehouseId = d.TargetWarehouseId ?? 0;
            ExpenseTypeId = d.ExpenseTypeId ?? 0;
            Note = d.Note;
            Lines = _docs.GetLines(d.Id).Select(l => new LineInput
            {
                ProductId = l.ProductId, ProductName = l.ProductName, ProductCode = l.ProductCode,
                Qty = l.Qty, UnitPrice = l.UnitPrice,
                Disc1 = l.Disc1, Disc2 = l.Disc2, Disc3 = l.Disc3, Disc4 = l.Disc4, Disc5 = l.Disc5,
                Disc6 = l.Disc6, Disc7 = l.Disc7, Disc8 = l.Disc8, Disc9 = l.Disc9, Disc10 = l.Disc10,
                TaxGroupId = l.TaxGroupId ?? 0, TaxRate = l.TaxRate
            }).ToList();

            // Cari seçiliyse adres <select>'i doldur (edit yeniden yükleme).
            if (CustomerId > 0) LoadAddresses(CustomerId);

            LoadPrint(d);
        }
        return Page();
    }

    /// <summary>"Kaydet" — belge kaydedilir, listeye döner (WPF Save_Click).</summary>
    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        var saved = BuildAndSave(id);
        if (saved is null) return Page();   // Error set edildi
        return RedirectToPage("Index", new { type = (int)Type });
    }

    /// <summary>"Kaydet ve İşle" — belge kaydedilip tipe göre işlenir, sonra listeye döner (WPF SavePost_Click).
    /// Save başarısızsa Error ile sayfada kalır. Save başarılı ama Post başarısızsa evrak TASLAK olarak kalır ve mesajla listeye dönülür.</summary>
    public IActionResult OnPostSavePost(int? id)
    {
        if (!CanAccess) return Forbid();
        var saved = BuildAndSave(id);
        if (saved is null) return Page();   // Error set edildi

        try
        {
            if (saved.DocType == DocType.DepoTransfer) _posting.PostTransfer(saved.Id, Session.CurrentUserId);
            else if (saved.DocType == DocType.Sayim) _posting.PostCount(saved.Id, Session.CurrentUserId);
            else _posting.Post(saved.Id, Session.CurrentUserId);   // fatura/irsaliye/fire → cari + stok
            TempData["DocMessage"] = $"Evrak kaydedildi ve işlendi. Fiş No: {saved.FisNo}";
        }
        catch (Exception ex)
        {
            TempData["DocError"] = $"Kaydedildi ancak işlenemedi: {ex.Message} Evrak TASLAK olarak duruyor; listeden 'İşle' ile tekrar deneyebilirsiniz.";
        }
        return RedirectToPage("Index", new { type = (int)Type });
    }

    /// <summary>"Kaydet ve Yazdır" — belge kaydedilir, kaydedilen belgenin Edit'ine ?print=1 ile dönülür
    /// (mevcut print akışı: @@media print bloğu + otomatik window.print) — WPF SavePrint_Click karşılığı.
    /// Save başarısızsa Error ile sayfada kalır.</summary>
    public IActionResult OnPostSavePrint(int? id)
    {
        if (!CanAccess) return Forbid();
        var saved = BuildAndSave(id);
        if (saved is null) return Page();   // Error set edildi
        return RedirectToPage("Edit", new { type = (int)Type, id = saved.Id, print = 1 });
    }

    /// <summary>Doğrulama + Document/DocumentLine kurulumu + DocumentService.Save (WPF BuildAndSave birebir).
    /// Başarısızsa <see cref="Error"/> set edilip null döner (çağıran Page() döndürür).</summary>
    private Document? BuildAndSave(int? id)
    {
        Id = id ?? 0;
        LoadLookups();

        if (Id > 0)
        {
            var existing = _docs.GetById(Id);
            if (existing is null) { Error = "Evrak bulunamadı."; return null; }
            Type = existing.DocType; FisNo = existing.FisNo; Status = existing.Status;
            if (existing.Status != DocStatus.Taslak)
            {
                ReadOnlyDoc = true;
                Error = "İşlenmiş veya iptal edilmiş evrak düzenlenemez. Önce iptal edin.";
                return null;
            }
        }

        // Boş satırları at (ürünsüz şablon satırlar).
        Lines = (Lines ?? new()).Where(l => l.ProductId > 0).ToList();

        // Zorunlu alanlar (WPF BuildAndSave birebir).
        if (string.IsNullOrWhiteSpace(BelgeNo)) { Error = "Belge No zorunlu (firmadan gelen ya da resmi belge numarası)."; return null; }
        if (!TryDate(DocDate, out var docDate)) { Error = "Tarih zorunlu."; return null; }
        if (!TryDate(BelgeTarihi, out var belgeTarihi)) { Error = "Belge Tarihi zorunlu."; return null; }
        if (CurrencyId <= 0) { Error = "Döviz zorunlu."; return null; }
        if (BranchId <= 0) { Error = "Şube zorunlu."; return null; }
        if (WarehouseId <= 0) { Error = "Depo zorunlu."; return null; }
        if (Lines.Count == 0) { Error = "En az bir satır ekleyin."; return null; }
        if (NeedsCustomer && CustomerId <= 0) { Error = "Cari zorunlu."; return null; }
        if (IsTransfer && TargetWarehouseId <= 0) { Error = "Hedef depo zorunlu."; return null; }
        if (IsGider && ExpenseTypeId <= 0) { Error = "Gider tipi zorunlu."; return null; }

        // Cari seçiliyse adres <select>'ini yeniden doldur (hata dönüşünde görünür kalsın).
        if (NeedsCustomer && CustomerId > 0) LoadAddresses(CustomerId);

        // Tarih değişmediyse orijinal saati koru; aksi halde güncel saat (WPF mantığı).
        DateTime docDateTime = docDate.Date.Add(DateTime.Now.TimeOfDay);
        if (Id > 0)
        {
            var prev = _docs.GetById(Id);
            if (prev is not null && prev.DocDate.Date == docDate.Date) docDateTime = prev.DocDate;
        }

        var doc = new Document
        {
            Id = Id,
            DocType = Type,
            BelgeNo = BelgeNo.Trim(),
            DocDate = docDateTime,
            BelgeTarihi = belgeTarihi,
            BranchId = BranchId,
            WarehouseId = WarehouseId > 0 ? WarehouseId : null,
            ProjectId = ProjectId > 0 ? ProjectId : null,
            CurrencyId = CurrencyId,
            PersonnelId = PersonnelId > 0 ? PersonnelId : null,
            CustomerId = NeedsCustomer && CustomerId > 0 ? CustomerId : null,
            AddressId = NeedsCustomer && CustomerId > 0 && AddressId > 0 ? AddressId : null,
            TargetBranchId = IsTransfer && TargetBranchId > 0 ? TargetBranchId : null,
            TargetWarehouseId = IsTransfer && TargetWarehouseId > 0 ? TargetWarehouseId : null,
            ExpenseTypeId = IsGider && ExpenseTypeId > 0 ? ExpenseTypeId : null,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
        };

        var lines = new List<DocumentLine>();
        foreach (var l in Lines)
        {
            if (l.Qty <= 0) { Error = "Satır miktarı 0'dan büyük olmalı."; return null; }
            if (l.UnitPrice < 0) { Error = "Satır fiyatı negatif olamaz."; return null; }
            int tgId = l.TaxGroupId;
            lines.Add(new DocumentLine
            {
                ProductId = l.ProductId,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                // Zincirli iskonto (Disc1..Disc10) — server doc.Recalc() ile çarpımsal işler.
                Disc1 = l.Disc1, Disc2 = l.Disc2, Disc3 = l.Disc3, Disc4 = l.Disc4, Disc5 = l.Disc5,
                Disc6 = l.Disc6, Disc7 = l.Disc7, Disc8 = l.Disc8, Disc9 = l.Disc9, Disc10 = l.Disc10,
                TaxGroupId = tgId == 0 ? null : tgId,
                TaxRate = EffectiveRate(tgId, docDate.Date)
                // LineGross/Net/Tax/Total serviste doc.Recalc() ile yeniden hesaplanır.
            });
        }

        try
        {
            _docs.Save(doc, lines, Session.CurrentUserId);   // FisNo + kur snapshot serviste
            return doc;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return null;
        }
    }

    /// <summary>KDV oranı EVRAK TARİHİNE göre (geri tarihli evrak doğru oran) — WPF EffectiveRate birebir.</summary>
    private decimal EffectiveRate(int taxGroupId, DateTime asOf)
    {
        if (taxGroupId == 0) return 0;
        var r = _taxService.GetRates(taxGroupId)
            .Where(x => x.StartDate.Date <= asOf && (x.EndDate is null || x.EndDate.Value.Date >= asOf))
            .OrderByDescending(x => x.StartDate).FirstOrDefault();
        return r?.Rate ?? 0;
    }

    /// <summary>Baskı bloğu için belge + satır + firma/şube adı (WPF DocumentReceipt.Print içerik karşılığı).</summary>
    private void LoadPrint(Document d)
    {
        PrintDoc = d;
        PrintLines = _docs.GetLines(d.Id);
        var co = Session.CurrentCompany;
        PrintCompanyName = (string.IsNullOrWhiteSpace(co?.Name) ? co?.Code : co.Name) ?? "";
        var branch = BranchOptions.FirstOrDefault(b => b.Value == d.BranchId.ToString());
        PrintBranchName = branch?.Text;
    }

    private void LoadLookups()
    {
        var co = Session.CurrentCompany;
        var branches = co is null ? new List<CompanyBranch>() : new BranchService().GetByCompany(co.Id);
        BranchOptions = branches.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();

        var warehouses = new WarehouseService().GetAll().Where(w => w.IsActive).ToList();
        WarehouseOptions = warehouses.Where(w => w.BranchId == BranchId)
            .Select(w => new SelectListItem(w.Display, w.Id.ToString())).ToList();
        TargetWarehouseOptions = warehouses.Where(w => w.BranchId == TargetBranchId)
            .Select(w => new SelectListItem(w.Display, w.Id.ToString())).ToList();
        WarehousesByBranchJson = JsonSerializer.Serialize(warehouses
            .GroupBy(w => w.BranchId)
            .ToDictionary(g => g.Key.ToString(), g => g.Select(w => new { v = w.Id, t = w.Display })));

        var currencies = new CurrencyService().GetAll().Where(c => c.IsActive).ToList();
        CurrencyOptions = currencies.Select(c => new SelectListItem(c.Code, c.Id.ToString())).ToList();
        // JS FX için: currencyId → kod (WPF CurrencyCombo.SelectedItem.Text karşılığı).
        CurrencyCodesJson = JsonSerializer.Serialize(currencies.ToDictionary(c => c.Id.ToString(), c => c.Code));
        // En güncel USD/EUR satış kuru (WPF CurrencyRateService.GetUsdEur birebir; DB'den, ağ çağrısı yok). Hata yutulur.
        try { var (usd, eur) = new CurrencyRateService().GetUsdEur(); FxUsd = usd; FxEur = eur; }
        catch { FxUsd = null; FxEur = null; }

        ProjectOptions = new() { new SelectListItem("(Yok)", "0") };
        ProjectOptions.AddRange(new ProjectService().GetAll().Where(p => p.IsActive)
            .Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString())));

        PersonnelOptions = new() { new SelectListItem("(Yok)", "0") };
        PersonnelOptions.AddRange(new PersonnelService().GetAll().Where(p => p.IsActive)
            .Select(p => new SelectListItem($"{p.Code} · {p.FullName}", p.Id.ToString())));

        var customerSvc = new CustomerService();
        var customers = customerSvc.GetAll();
        CustomerOptions = new() { new SelectListItem("(Cari seçiniz)", "0") };
        CustomerOptions.AddRange(customers
            .Select(c => new SelectListItem($"{c.Code} · {c.Name}", c.Id.ToString())));
        AddressOptions = new() { new SelectListItem("(Adres yok)", "0") };   // varsayılan; cari seçilince JS/GET doldurur

        // Adres haritası (WPF LoadAddresses): cari seçilince JS ilgili adresleri süzer. NeedsCustomer tiplerde doldurulur.
        if (NeedsCustomer)
        {
            var addrMap = new Dictionary<string, object>();
            foreach (var c in customers)
            {
                var addrs = customerSvc.GetAddresses(c.Id);
                if (addrs.Count == 0) continue;
                addrMap[c.Id.ToString()] = addrs.Select(a => new
                {
                    v = a.Id,
                    t = a.Title ?? a.Address ?? $"Adres {a.Id}"
                });
            }
            AddressesByCustomerJson = JsonSerializer.Serialize(addrMap);
        }

        ExpenseOptions = new() { new SelectListItem("(Gider tipi seçiniz)", "0") };
        ExpenseOptions.AddRange(new ExpenseTypeService().GetAll().Where(x => x.IsActive)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString())));

        var products = _productService.GetAll().Where(p => p.IsActive).ToList();
        ProductOptions = new() { new SelectListItem("(Ürün seçiniz)", "0") };
        ProductOptions.AddRange(products.Select(p => new SelectListItem($"{p.Code} · {p.Name}", p.Id.ToString())));
        ProductTaxMapJson = JsonSerializer.Serialize(products.ToDictionary(
            p => p.Id.ToString(),
            p => new { sales = p.SalesTaxGroupId ?? 0, purchase = p.PurchaseTaxGroupId ?? 0 }));

        var taxes = _taxService.GetAll().Where(t => t.IsActive).ToList();
        TaxOptions = new() { new SelectListItem("(KDV yok)", "0") };
        TaxOptions.AddRange(taxes.Select(t => new SelectListItem(t.Display, t.Id.ToString())));
        TaxRatesJson = JsonSerializer.Serialize(taxes.ToDictionary(
            t => t.Id.ToString(),
            t => _taxService.GetRates(t.Id).Select(r => new
            {
                r = r.Rate,
                s = r.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e = r.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
            })));
    }

    /// <summary>Seçili carinin adreslerini AddressOptions'a yükler (WPF LoadAddresses birebir).</summary>
    private void LoadAddresses(int customerId)
    {
        AddressOptions = new() { new SelectListItem("(Adres yok)", "0") };
        AddressOptions.AddRange(new CustomerService().GetAddresses(customerId)
            .Select(a => new SelectListItem(a.Title ?? a.Address ?? $"Adres {a.Id}", a.Id.ToString())));
    }

    private static bool TryDate(string? s, out DateTime value)
        => DateTime.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>Formdan gelen tek satır (indexed bind: Lines[i].X).</summary>
    public class LineInput
    {
        public int ProductId { get; set; }
        public decimal Qty { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        // Zincirli iskonto (WPF DiscountsDialog: 10'a kadar). Disc1 satırda görünür; Disc2..Disc10 "İskontolar" modalinde.
        public decimal Disc1 { get; set; }
        public decimal Disc2 { get; set; }
        public decimal Disc3 { get; set; }
        public decimal Disc4 { get; set; }
        public decimal Disc5 { get; set; }
        public decimal Disc6 { get; set; }
        public decimal Disc7 { get; set; }
        public decimal Disc8 { get; set; }
        public decimal Disc9 { get; set; }
        public decimal Disc10 { get; set; }
        public int TaxGroupId { get; set; }
        public decimal TaxRate { get; set; }

        // Gösterim (düzenlemede yükle)
        public string? ProductName { get; set; }
        public string? ProductCode { get; set; }
    }
}
