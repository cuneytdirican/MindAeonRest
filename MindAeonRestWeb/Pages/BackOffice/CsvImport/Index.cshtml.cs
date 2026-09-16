using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CsvImport;

/// <summary>
/// CSV ile toplu stok kartı + fiyat içe aktarımı. WPF CsvImportWindow ile birebir akış:
/// CSV yükle → varsayılan atamaları seç → "Doğrula" (ayrıştır + satır-satır önizle) → "İçe Aktar".
/// Ayrıştırma/doğrulama/aktarma tümü Core CsvImportService içindedir (ParseAndValidate + Import).
/// WPF admin-only; web'de kapı: Admin/Patron + Feature.ProductBasic.
///
/// Web quirk: Core.ParseAndValidate bir DOSYA YOLU bekler (WPF OpenFileDialog). Web'de yüklenen
/// IFormFile geçici bir dosyaya yazılır; "Doğrula" adımı sonrası aynı dosya "İçe Aktar" adımında
/// tekrar ayrıştırılabilsin diye geçici dosya adı TempData ile taşınır (satır durumu re-parse edilir).
/// </summary>
public class IndexModel : PageModel
{
    private readonly CsvImportService _service = new();

    // Varsayılan atama seçimleri (WPF: UnitCombo / SalesTaxCombo / CurrencyCombo / ScopeCombo)
    [BindProperty] public int DefaultUnitId { get; set; }        // 0 = (yok)
    [BindProperty] public int DefaultSalesTaxId { get; set; }    // 0 = (yok) — WPF'te alış/iade için de bu kullanılır
    [BindProperty] public int DefaultCurrencyId { get; set; }    // 0 = (yok)
    [BindProperty] public int TargetBranchId { get; set; }       // 0 = Genel (tüm şubeler)
    [BindProperty] public bool UpdateExisting { get; set; } = true;

    // Ayrıştırılan/doğrulanan satırlar (önizleme) ve özet
    public List<CsvProductRow> Rows { get; private set; } = new();
    public string? Summary { get; private set; }
    public bool ShowImport { get; private set; }
    public string? Error { get; private set; }
    public string? ImportResultMessage { get; private set; }
    public bool ImportHadFailures { get; private set; }

    // Lookuplar (WPF ile aynı: birim, satış vergi, döviz, şube kapsamı)
    public List<SelectListItem> UnitOptions { get; private set; } = new();
    public List<SelectListItem> TaxOptions { get; private set; } = new();
    public List<SelectListItem> CurrencyOptions { get; private set; } = new();
    public List<SelectListItem> ScopeOptions { get; private set; } = new();

    // Örnek şablon başlığı (bilgi amaçlı gösterim)
    public string SampleHeader => CsvImportService.SampleHeader;

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        return Page();
    }

    /// <summary>Örnek CSV şablonunu indir (WPF DownloadSample_Click karşılığı).</summary>
    public IActionResult OnGetSample()
    {
        if (!CanAccess) return Forbid();
        var bytes = new UTF8Encoding(true).GetBytes(_service.BuildSample());
        return File(bytes, "text/csv; charset=utf-8", "stok_ornek.csv");
    }

    /// <summary>Doğrula: yüklenen CSV'yi ayrıştır + satır-satır doğrula ve önizleme göster.</summary>
    public IActionResult OnPostValidate(IFormFile? csvFile)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        if (csvFile is not { Length: > 0 })
        {
            Error = "Önce bir CSV dosyası seçin.";
            return Page();
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"maerest_csv_{Guid.NewGuid():N}.csv");
        try
        {
            using (var fs = System.IO.File.Create(tempPath))
                csvFile.CopyTo(fs);

            Rows = _service.ParseAndValidate(tempPath, BuildOptions());
            BuildSummary();

            // İçe aktar adımında tekrar ayrıştırılmak üzere geçici dosyayı sakla.
            TempData["CsvTempPath"] = tempPath;
            TempData.Keep("CsvTempPath");
            ShowImport = Rows.Any(r => r.IsValid);
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            Error = "CSV okunamadı: " + ex.Message;
        }
        return Page();
    }

    /// <summary>İçe Aktar: geçici dosyayı aynı varsayılanlarla tekrar ayrıştır ve geçerli satırları aktar.</summary>
    public IActionResult OnPostImport()
    {
        if (!CanAccess) return Forbid();
        LoadLookups();

        string? tempPath = TempData["CsvTempPath"] as string;
        if (string.IsNullOrEmpty(tempPath) || !System.IO.File.Exists(tempPath))
        {
            Error = "Oturum süresi doldu veya dosya bulunamadı. Lütfen dosyayı tekrar seçip 'Doğrula' yapın.";
            return Page();
        }

        try
        {
            Rows = _service.ParseAndValidate(tempPath, BuildOptions());
            int valid = Rows.Count(r => r.IsValid);
            if (valid == 0)
            {
                BuildSummary();
                ShowImport = false;
                Error = "Geçerli satır yok.";
                return Page();
            }

            var result = _service.Import(Rows, Session.CurrentUserId);
            var sb = new StringBuilder();
            sb.Append($"Aktarım tamamlandı. Eklenen: {result.Created} · Güncellenen: {result.Updated} · Başarısız: {result.Failed}");
            if (result.Errors.Count > 0)
                sb.Append(" | Hatalar: " + string.Join(" ; ", result.Errors.Take(15)));
            ImportResultMessage = sb.ToString();
            ImportHadFailures = result.Failed > 0;
        }
        catch (Exception ex)
        {
            Error = "Aktarım hatası: " + ex.Message;
        }
        finally
        {
            SafeDelete(tempPath);
        }
        return Page();
    }

    private CsvImportOptions BuildOptions() => new()
    {
        DefaultUnitId = DefaultUnitId > 0 ? DefaultUnitId : null,
        DefaultSalesTaxId = DefaultSalesTaxId > 0 ? DefaultSalesTaxId : null,
        DefaultPurchaseTaxId = DefaultSalesTaxId > 0 ? DefaultSalesTaxId : null,  // WPF: alış/iade için de satış varsayılanı
        DefaultReturnTaxId = DefaultSalesTaxId > 0 ? DefaultSalesTaxId : null,
        DefaultCurrencyId = DefaultCurrencyId > 0 ? DefaultCurrencyId : null,
        TargetBranchId = TargetBranchId > 0 ? TargetBranchId : null,
        UpdateExisting = UpdateExisting
    };

    private void BuildSummary()
    {
        int valid = Rows.Count(r => r.IsValid);
        int add = Rows.Count(r => r.IsValid && !r.ExistsInDb);
        int upd = Rows.Count(r => r.IsValid && r.ExistsInDb);
        int bad = Rows.Count - valid;
        Summary = $"Toplam {Rows.Count} · {add} eklenecek · {upd} güncellenecek · {bad} hatalı";
    }

    private void LoadLookups()
    {
        UnitOptions = WithNone(new MainUnitService().GetAll().Select(u => new SelectListItem(u.Display, u.Id.ToString())));
        TaxOptions = WithNone(new TaxGroupService().GetAll().Select(t => new SelectListItem(t.Display, t.Id.ToString())));
        CurrencyOptions = WithNone(new CurrencyService().GetAll().Select(c => new SelectListItem(c.Code, c.Id.ToString())));

        ScopeOptions = new() { new SelectListItem("Genel (tüm şubeler)", "0") };
        ScopeOptions.AddRange(new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0)
            .Select(b => new SelectListItem(b.Display, b.Id.ToString())));
    }

    private static List<SelectListItem> WithNone(IEnumerable<SelectListItem> items)
    {
        var list = new List<SelectListItem> { new("(yok)", "0") };
        list.AddRange(items);
        return list;
    }

    private static void SafeDelete(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* geçici dosya; yok sayılabilir */ }
    }
}
