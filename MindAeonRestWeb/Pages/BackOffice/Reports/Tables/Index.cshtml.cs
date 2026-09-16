using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.Tables;

/// <summary>
/// Masa Raporları — WPF MasaRaporWindow'un web karşılığı.
/// Anlık: masa doluluğu (boş/dolu/rezerve, açık adisyon tutarı, garson, süre).
/// Geçmiş: tarih aralığında masa performansı (devir, ciro, ort. fiş, kişi, iptal, koltuk başı ciro).
/// Filtre: Şube + Lokasyon (+ geçmiş için Başlangıç/Bitiş). Sekmeler: Anlık / Geçmiş.
/// Salt-okunur; dışa aktarım (Excel/PDF) WPF'e özel — web'de window.print.
/// Gate: SysAdmin/Patron + Feature.AdvancedReports.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReportService _reports = new();
    private readonly BranchService _branchSvc = new();
    private readonly ZoneService _zoneSvc = new();

    public List<(int Value, string Text)> BranchOptions { get; } = new();
    public List<(int Value, string Text)> ZoneOptions { get; } = new();
    public List<TableLiveRow> Live { get; private set; } = new();
    public List<TableHistoryRow> History { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ZoneId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "live";

    public bool HistoryTab => string.Equals(Tab, "history", StringComparison.OrdinalIgnoreCase);

    // Anlık özet
    public int LiveCount => Live.Count;
    public int DoluCount => Live.Count(x => x.State == "Dolu");
    public int BosCount => Live.Count(x => x.State == "Boş");
    public int RezerveCount => Live.Count(x => x.State == "Rezerve");
    public decimal AcikCiro => Live.Where(x => x.State == "Dolu").Sum(x => x.Total);

    // Geçmiş özet
    public int HistTableCount => History.Count;
    public int HistAdisyon => History.Sum(x => x.AdisyonCount);
    public decimal HistCiro => History.Sum(x => x.Revenue);
    public int HistCovers => History.Sum(x => x.Covers);
    public int HistCancels => History.Sum(x => x.CancelCount);
    public decimal HistOrtFis => HistAdisyon > 0 ? HistCiro / HistAdisyon : 0;

    private static bool CanAccess =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.AdvancedReports);

    /// <summary>Web'de Session.CurrentCompany.Id dolu değildir; Company.Id'yi koddan çözeriz (Branches deseni).</summary>
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

        // Şube listesi — WPF gibi "Tüm Şubeler" (0) seçeneğiyle.
        BranchOptions.Add((0, "Tüm Şubeler"));
        if (companyId is not null)
            foreach (var b in _branchSvc.GetByCompany(companyId.Value))
                BranchOptions.Add((b.Id, b.Name));

        // Varsayılanlar: son 7 gün, oturum şubesi (yoksa "Tüm Şubeler").
        From ??= DateTime.Today.AddDays(-7);
        To ??= DateTime.Today;
        BranchId ??= Session.CurrentBranch?.Id ?? 0;

        int? branchFilter = BranchId is int bf && bf > 0 ? bf : null;
        int? zoneFilter = ZoneId is int zf && zf > 0 ? zf : null;

        // Lokasyon listesi — seçili şubeye göre (WPF ReloadZones deseni).
        ZoneOptions.Add((0, "Tüm Lokasyonlar"));
        foreach (var z in _zoneSvc.GetAll().Where(z => z.IsActive && (branchFilter is null || z.BranchId == branchFilter)))
            ZoneOptions.Add((z.Id, z.Name));

        // Seçili lokasyon artık geçerli değilse (şube değişti) sıfırla.
        if (zoneFilter is int zid && ZoneOptions.All(o => o.Value != zid))
        {
            ZoneId = 0;
            zoneFilter = null;
        }

        if (HistoryTab)
        {
            var from = From.Value;
            var to = To.Value;
            if (to < from) (from, to) = (to, from);
            History = _reports.GetTableHistory(branchFilter, zoneFilter, from, to);
        }
        else
        {
            Live = _reports.GetTableLiveStatus(branchFilter, zoneFilter);
        }

        return Page();
    }

    /// <summary>CSV dışa aktarım — aktif sekmenin (Anlık/Geçmiş) ekrandaki kolonlarıyla. Diğer raporlarla tutarlı.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        int? branchFilter = BranchId is int bf && bf > 0 ? bf : null;
        int? zoneFilter = ZoneId is int zf && zf > 0 ? zf : null;
        var sb = new System.Text.StringBuilder();
        string name;
        if (HistoryTab)
        {
            var from = From ?? DateTime.Today.AddDays(-7);
            var to = To ?? DateTime.Today;
            if (to < from) (from, to) = (to, from);
            var rows = _reports.GetTableHistory(branchFilter, zoneFilter, from, to);
            sb.AppendLine("Lokasyon;Masa;Kişilik;Adisyon;Ciro;Ort. Fiş;Toplam Kişi;İptal;Koltuk Başı Ciro;Son Kullanım");
            foreach (var r in rows)
                sb.AppendLine(string.Join(";", Esc(r.ZoneName), Esc(r.TableCode), r.Seats, r.AdisyonCount,
                    Esc(r.RevenueText), Esc(r.AvgTicketText), r.Covers, r.CancelCount, Esc(r.RevPerSeatText), Esc(r.LastUsedText)));
            name = $"masa-gecmis-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        }
        else
        {
            var rows = _reports.GetTableLiveStatus(branchFilter, zoneFilter);
            sb.AppendLine("Lokasyon;Masa;Kişilik;Durum;Açık Adisyon;Açılış;Süre;Kişi;Tutar;Garson;Not");
            foreach (var r in rows)
                sb.AppendLine(string.Join(";", Esc(r.ZoneName), Esc(r.TableCode), r.Seats, Esc(r.State),
                    Esc(r.OrderCode), Esc(r.OpenedText), Esc(r.DurationText), Esc(r.CoversText), Esc(r.TotalText), Esc(r.WaiterName), Esc(r.Info)));
            name = $"masa-anlik-{DateTime.Today:yyyyMMdd}.csv";
        }
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    // BULGU 11: CSV/formül enjeksiyonu nötrleştirme + ayraç kaçışı ortak yardımcıda (CsvHelper.Field).
    private static string Esc(string? v) => MindAeonRest.Helpers.CsvHelper.Field(v);
}
