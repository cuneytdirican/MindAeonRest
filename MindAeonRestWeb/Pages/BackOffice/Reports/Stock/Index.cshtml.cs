using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.Stock;

/// <summary>
/// Stok Raporları — WPF "Stok Miktar Raporu" + "Stok Hareket Raporu" (ReportBuilders.StokMiktar /
/// ReportBuilders.StokHareket) pencerelerinin web karşılığı.
/// Sekmeler:
///   • Stok Miktar : şube + depo + arama → ürün/depo bazlı bakiye (birim çevrimli).
///     WPF: ReportFilterOptions.Branch | Warehouse | Search → StockService.GetBalances(branchId, warehouseId) + arama.
///   • Stok Hareket: tarih aralığı + depo + arama → hareket listesi.
///     WPF: ReportFilterOptions.DateRange | Warehouse | Search → StockService.GetMovements(from, to, warehouseId, branchId) + arama.
/// WPF ReportBuilders'ın kullandığı StockService imzaları birebir çağrılır. Salt-okunur + tarayıcı yazdır.
/// Gate: SysAdmin/Patron + Feature.AdvancedReports (Fiche raporu ile aynı).
/// </summary>
public class IndexModel : PageModel
{
    private readonly StockService _stock = new();
    private readonly BranchService _branchSvc = new();
    private readonly WarehouseService _warehouseSvc = new();

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> WarehouseOptions { get; } = new();

    public List<StockBalance> Balances { get; private set; } = new();
    public List<StockMovement> Movements { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? WarehouseId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "qty";

    public bool MovesTab => string.Equals(Tab, "moves", StringComparison.OrdinalIgnoreCase);

    // Stok Miktar özet
    public int BalanceCount => Balances.Count;

    // Stok Hareket özet (WPF footer ile aynı: giriş + çıkış = net)
    public int MovementCount => Movements.Count;
    public decimal Giris => Movements.Where(m => m.Qty >= 0).Sum(m => m.Qty);
    public decimal Cikis => Movements.Where(m => m.Qty < 0).Sum(m => m.Qty);
    public decimal Net => Giris + Cikis;

    private static bool CanAccess =>
        Session.IsTenantAdmin && Session.LicenseHas(Feature.AdvancedReports);

    /// <summary>Web'de Session.CurrentCompany.Id dolu değildir; Company.Id'yi koddan çözeriz (Fiche/Branches deseni).</summary>
    private static int? ResolveCompanyId()
    {
        var code = Session.CurrentCompany?.Code;
        if (string.IsNullOrEmpty(code)) return null;
        return new CompanyService().GetByCode(code)?.Id;
    }

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();

        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        BranchId ??= Session.CurrentBranch?.Id ?? (BranchOptions.Count > 0 ? BranchOptions[0].Value : null);

        // Depo listesi: firma DB'sinden. Şube seçiliyse o şubenin depolarına daralt (WPF filtre davranışı).
        var warehouses = _warehouseSvc.GetAll();
        if (BranchId is int bid && bid != 0)
            warehouses = warehouses.Where(w => w.BranchId == bid).ToList();
        foreach (var w in warehouses)
            WarehouseOptions.Add((w.Id, w.Display));

        // Seçili depo artık listede yoksa (şube değişti) temizle.
        if (WarehouseId is int wid && WarehouseOptions.All(o => o.Value != wid))
            WarehouseId = null;

        if (MovesTab)
        {
            From ??= DateTime.Today.AddDays(-7);
            To ??= DateTime.Today;
            var from = From.Value;
            var to = To.Value;
            if (to < from) (from, to) = (to, from);

            // WPF: ReportBuilders.StokHareket → StockService.GetMovements(from, to, warehouseId, branchId) + arama.
            Movements = _stock.GetMovements(from, to, WarehouseId, BranchId);
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                Movements = Movements
                    .Where(x => (x.ProductName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        else
        {
            // WPF: ReportBuilders.StokMiktar → StockService.GetBalances(branchId, warehouseId) + arama.
            Balances = _stock.GetBalances(BranchId, WarehouseId);
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                Balances = Balances
                    .Where(x => (x.Code + " " + x.Name).Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return Page();
    }

    /// <summary>CSV dışa aktarım — aktif sekmenin (Stok Miktar / Stok Hareket) ekrandaki kolonlarını
    /// aynı GetBalances / GetMovements sorgusu + arama ile üretir.
    /// UTF-8 BOM + ';' ayraç (Türkçe Excel). Filtreler querystring ile taşınır.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();

        var companyId = ResolveCompanyId();
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        BranchId ??= Session.CurrentBranch?.Id ?? (BranchOptions.Count > 0 ? BranchOptions[0].Value : null);

        var sb = new System.Text.StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        string name;
        if (MovesTab)
        {
            name = "stok-hareket";
            From ??= DateTime.Today.AddDays(-7);
            To ??= DateTime.Today;
            var from = From.Value;
            var to = To.Value;
            if (to < from) (from, to) = (to, from);

            Movements = _stock.GetMovements(from, to, WarehouseId, BranchId);
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                Movements = Movements.Where(x => (x.ProductName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            Line("Tarih", "Ürün", "Depo", "Hareket", "Miktar");
            foreach (var m in Movements)
                Line(m.DateText, m.ProductName, m.WarehouseName, m.TypeText, m.Qty.ToString("0.###"));
        }
        else
        {
            name = "stok-miktar";
            Balances = _stock.GetBalances(BranchId, WarehouseId);
            if (!string.IsNullOrWhiteSpace(Q))
            {
                var q = Q.Trim();
                Balances = Balances.Where(x => (x.Code + " " + x.Name).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            Line("Kod", "Ürün", "Depo", "Miktar", "Birim", "Alt Birim Miktar", "Alt Birim");
            foreach (var b in Balances)
                Line(b.Code, b.Name, b.WarehouseName, b.Qty.ToString("0.###"), b.MainUnit, b.AltQty?.ToString("0.###"), b.AltUnit);
        }

        return CsvFile(sb.ToString(), $"{name}-{DateTime.Today:yyyyMMdd}.csv");
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
