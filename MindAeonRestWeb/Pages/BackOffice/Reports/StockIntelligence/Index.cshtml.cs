using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.StockIntelligence;

/// <summary>
/// FAZ 4b Stok Zekâsı — teorik (satış×reçete) vs gerçek (stok çıkışı: satış + fire + sayım farkı) bileşen
/// tüketim sapması (fire/kayıp maliyeti) + düşük-stok (bakiye &lt; MinStock) listesi + tek tıkla AlisSiparis
/// taslağı üretimi + CSV. Salt-okuma; taslak üretimi yalnız yeni Taslak evrak yazar (DocumentService).
/// Gate: SysAdmin/Patron + Feature.AdvancedReports.
/// </summary>
public class IndexModel : PageModel
{
    private readonly StockIntelligenceService _svc = new();
    private readonly BranchService _branchSvc = new();
    private readonly WarehouseService _warehouseSvc = new();

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> WarehouseOptions { get; } = new();

    public StockVarianceReport Report { get; private set; } = new();
    public List<LowStockItem> LowStock { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? WarehouseId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    private static bool CanAccess =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.AdvancedReports);

    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    private int LoadFilters()
    {
        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        From ??= DateTime.Today.AddDays(-29);
        To ??= DateTime.Today;
        BranchId ??= Session.CurrentBranch?.Id ?? 0;

        var warehouses = _warehouseSvc.GetAll();
        if (BranchId is int bid && bid != 0)
            warehouses = warehouses.Where(w => w.BranchId == bid).ToList();
        foreach (var w in warehouses)
            WarehouseOptions.Add((w.Id, w.Display));
        if (WarehouseId is int wid && WarehouseOptions.All(o => o.Value != wid))
            WarehouseId = null;

        return BranchId.Value;
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        int branchId = LoadFilters();
        Report = _svc.GetVarianceReport(From!.Value, To!.Value, branchId, WarehouseId);
        LowStock = _svc.GetLowStock(branchId == 0 ? null : branchId, WarehouseId);
        return Page();
    }

    /// <summary>Düşük-stok ürünlerden AlisSiparis taslağı üretir → taslağı düzenleme ekranında açar.</summary>
    public IActionResult OnPostReorder()
    {
        if (!CanAccess) return Forbid();
        int branchId = LoadFilters();
        if (branchId == 0)
        {
            TempData["SiError"] = "Sipariş taslağı için tek bir şube seçili olmalı (Tüm Şubeler ile üretilemez).";
            return RedirectToPage(RouteVals());
        }
        try
        {
            int docId = _svc.GenerateReorderDraft(branchId, WarehouseId, Session.CurrentUserId);
            if (docId == 0)
            {
                TempData["SiError"] = "Düşük-stok kalem bulunamadı; sipariş taslağı üretilmedi.";
                return RedirectToPage(RouteVals());
            }
            TempData["SiMessage"] = "Düşük-stok alış siparişi taslağı oluşturuldu. Tedarikçiyi ve miktarları kontrol edip kaydedin.";
            return RedirectToPage("/BackOffice/Documents/Edit", new { type = 10, id = docId });
        }
        catch (Exception ex)
        {
            TempData["SiError"] = $"Taslak üretilemedi: {ex.Message}";
            return RedirectToPage(RouteVals());
        }
    }

    private object RouteVals() => new
    {
        BranchId,
        WarehouseId,
        From = From?.ToString("yyyy-MM-dd"),
        To = To?.ToString("yyyy-MM-dd")
    };

    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        int branchId = LoadFilters();
        Report = _svc.GetVarianceReport(From!.Value, To!.Value, branchId, WarehouseId);

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");
        Line("Kod", "Bileşen", "Teorik Miktar", "Gerçek Miktar", "Sapma Miktar", "Birim Maliyet",
             "Teorik Maliyet", "Gerçek Maliyet", "Sapma Maliyet", "Sapma %");
        foreach (var r in Report.Rows)
            Line(r.Code, r.Name, r.TheoreticalQty.ToString("0.###"), r.ActualQty.ToString("0.###"),
                 r.VarianceQty.ToString("0.###"), r.UnitCost.ToString("N4"),
                 r.TheoreticalCost.ToString("N2"), r.ActualCost.ToString("N2"),
                 r.VarianceCost.ToString("N2"), r.VariancePercent.ToString("N1"));

        return CsvFile(sb.ToString(), $"stok-zekasi-{Report.From:yyyyMMdd}-{Report.To:yyyyMMdd}.csv");
    }

    // ── CSV yardımcıları (';' ayraç, tırnak-kaçış, UTF-8 BOM) ──
    private static string Esc(string? s) => MindAeonRest.Helpers.CsvHelper.Field(s);

    private FileContentResult CsvFile(string csv, string fileName)
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = System.Text.Encoding.UTF8.GetBytes(csv);
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
