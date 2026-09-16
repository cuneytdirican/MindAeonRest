using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Reports.XZ;

/// <summary>X (anlık) / Z (gün sonu) raporu — RAPOR deseni (filtre → sonuç tablo).
/// WPF <c>RaporWindow</c> birebir karşılığı: şube + iş-günü tarihi seç → GetDailyReport;
/// "Z AL" → nakit devir + hedef kasa/POS ile TakeZWithTransfer.</summary>
public class IndexModel : PageModel
{
    private readonly ReportService _reports = new();
    private readonly BranchService _branch = new();

    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }

    public List<SelectListItem> BranchOptions { get; private set; } = new();

    // ── Sonuç ──
    public XZReport? Report { get; private set; }
    public string? ReportTitle { get; private set; }
    public string? BusinessDayInfo { get; private set; }
    public int Cutoff { get; private set; }

    // ── Z devir formu (WPF Load() varsayılanları) ──
    public List<SelectListItem> TargetCashOptions { get; private set; } = new();
    public List<SelectListItem> TargetPosOptions { get; private set; } = new();
    public decimal DefaultCashTransfer { get; private set; }
    public bool CanTakeZ => Session.IsTenantAdmin || Permissions.ZReport;

    public string? Error { get; private set; }
    public string? Info { get; private set; }

    // ── Açık masa uyarısı (WPF RaporWindow: Z'den ÖNCE GetUnpaidTables + Evet/Hayır/İptal) ──
    // Doldurulunca .cshtml açık-masa onay bölümünü gösterir; kullanıcı "Yine de Z Al" (confirm=1) ile devam eder.
    public List<UnpaidTable> OpenTables { get; private set; } = new();
    public bool ShowOpenTablesWarning { get; private set; }
    // Onay bölümündeki "Yine de Z Al" formunun tekrar taşıması gereken devir alanları.
    public decimal PendingCashTransfer { get; private set; }
    public int? PendingTargetCashId { get; private set; }
    public int? PendingTargetPosId { get; private set; }
    public decimal OpenTablesRemaining => OpenTables.Sum(t => t.Remaining);

    // Core özellik (lisans kapısı yok) — WPF RaporWindow Permissions.Reports.
    private static bool CanAccess => Session.IsTenantAdmin || Permissions.Reports;

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        return Page();
    }

    /// <summary>CSV dışa aktarım — ekrandaki X/Z özetini (bölüm/kalem/tutar) aynı sorguyla üretir.
    /// UTF-8 BOM + ';' ayraç (Türkçe Excel). Filtreler querystring ile taşınır.</summary>
    public IActionResult OnGetCsv()
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        if (Report is null) return NotFound();

        var rep = Report;
        var sb = new System.Text.StringBuilder();
        void Row(string a, string b, string c = "") => sb.Append(Esc(a)).Append(';').Append(Esc(b)).Append(';').Append(Esc(c)).Append("\r\n");

        Row("Bölüm", "Açıklama", "Tutar");
        Row("Özet", "Toplam Satış", rep.SalesTotal.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")));
        Row("Özet", "Adisyon", rep.OrderCount.ToString());
        Row("Özet", "Fiş", rep.FisCount.ToString());
        foreach (var p in rep.Payments) Row("Ödeme Dağılımı", p.KindText, p.AmountText);
        foreach (var k in rep.Kdv) Row("KDV", k.RateText, $"Matrah {k.MatrahText} | KDV {k.TaxText}");
        Row("İkram/Fire", "İkram", rep.TreatText);
        Row("İkram/Fire", "Fire (adet)", rep.FireQty.ToString("0.##"));
        foreach (var c in rep.CashBalances) Row("Kasa Durumu", c.Name, c.BalanceText);
        foreach (var c in rep.BankBalances) Row("Banka Defter", c.Name, c.BalanceText);
        foreach (var c in rep.PosBalances) Row("POS Defter", c.Name, c.BalanceText);
        foreach (var c in rep.MealVoucherBalances) Row("Yemek Çeki Defter", c.Name, c.BalanceText);

        return CsvFile(sb.ToString(), $"xz-raporu-{(Date ?? DateTime.Today):yyyyMMdd}.csv");
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

    /// <summary>Z (gün sonu) al: WPF TakeZ_Click birebir — yalnız içinde bulunulan iş günü,
    /// nakit devir tutarı + hedef şube kasası/POS, mükerrer guard serviste.</summary>
    public IActionResult OnPostTakeZ(decimal cashTransfer, int? targetCashId, int? targetPosId, int confirm = 0)
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        if (Report is null) { Error = "Rapor yüklenemedi."; return Page(); }

        if (!CanTakeZ)
        {
            Error = "Z (gün sonu) raporu almak için yetkiniz yok. Yalnız yönetici veya 'Z_Rapor' rolü alabilir.";
            return Page();
        }

        // Z yalnız İÇİNDE BULUNULAN İŞ GÜNÜ için alınır (WPF ile aynı guard).
        var bizToday = BusinessDay.CurrentDate(Cutoff);
        var selected = (Date ?? bizToday).Date;
        if (selected != bizToday)
        {
            Error = "Z raporu yalnız içinde bulunulan iş günü için alınabilir. Lütfen tarihi geçerli iş gününe alın.";
            return Page();
        }

        if (cashTransfer > 0 && targetCashId is null)
        {
            Error = "Nakit devri için hedef şube nakit kasası seçin (ya da devir tutarını 0 yapın).";
            return Page();
        }

        // AÇIK MASA UYARISI (WPF RaporWindow.xaml.cs:109-145 birebir):
        // Z almadan ÖNCE şubede açık (Status=Açık) adisyon var mı kontrol et. İlk POST'ta (confirm=0)
        // varsa Z'yi durdur, onay bölümünü göster (kod/kalan/durum + "Yine de Z Al"/"Vazgeç").
        // Kullanıcı "Yine de Z Al" (confirm=1) ile onaylayınca kontrol atlanır ve devir çalışır.
        if (confirm != 1)
        {
            try
            {
                var openTables = new OrderService().GetUnpaidTables(BranchId!.Value);
                if (openTables.Count > 0)
                {
                    OpenTables = openTables;
                    ShowOpenTablesWarning = true;
                    // "Yine de Z Al" formunun taşıyacağı devir seçimleri.
                    PendingCashTransfer = cashTransfer;
                    PendingTargetCashId = targetCashId;
                    PendingTargetPosId = targetPosId;
                    return Page();
                }
            }
            catch (Exception ex) { Error = ex.Message; return Page(); }
        }

        try
        {
            _reports.TakeZWithTransfer(Report, BranchId!.Value, cashTransfer, targetCashId, targetPosId, Session.CurrentUserId);
            ReportTitle = "GÜN SONU RAPORU (Z)";
            Info = "Z raporu alındı, devir yapıldı ve kaydedildi. Açık/ödenmemiş masalar etkilenmez; sonraki vardiyaya devreder.";
        }
        catch (Exception ex) { Error = ex.Message; }
        return Page();
    }

    /// <summary>Filtre varsayılanları + şube seçenekleri (Session.CurrentBranch, BusinessDay.CurrentDate(cut)).</summary>
    private void Prepare()
    {
        var co = Session.CurrentCompany;
        var branches = co is null ? new List<CompanyBranch>() : _branch.GetByCompany(co.Id).Where(b => b.IsActive).ToList();
        BranchOptions = branches.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();

        BranchId ??= Session.CurrentBranch?.Id ?? branches.FirstOrDefault()?.Id;
        Cutoff = BranchId is int b2 ? _branch.GetDayStartMinutes(b2) : (Session.CurrentBranch?.DayStartMinutes ?? 0);
        Date ??= BusinessDay.CurrentDate(Cutoff);
    }

    /// <summary>X raporunu üret + başlık/iş-günü aralığı + Z devir formu varsayılanları (WPF Load()).</summary>
    private void Build()
    {
        if (BranchId is not int branchId) { Error = "Şube bulunamadı."; return; }
        try
        {
            var date = (Date ?? BusinessDay.CurrentDate(Cutoff)).Date;
            var rep = _reports.GetDailyReport(branchId, date);
            Report = rep;
            ReportTitle ??= "GÜN RAPORU (X)";

            // İş günü aralığı başlığı (cut > 0). cut = 0 ise sadece tarih.
            if (Cutoff > 0)
            {
                string startHm = TimeSpan.FromMinutes(Cutoff).ToString(@"hh\:mm");
                BusinessDayInfo = $"{rep.BranchName} · {rep.DateText}  (İş günü: {date:dd.MM.yyyy} {startHm} – ertesi {startHm})";
            }
            else
            {
                BusinessDayInfo = $"{rep.BranchName} · {rep.DateText}";
            }

            // ── Gün sonu devri: hedef ŞUBE kasası/POS (şube kapsamlı, aktif) + nakit devir varsayılan ──
            TargetCashOptions = new CashRegisterService().GetAll()
                .Where(k => k.BranchId == branchId && k.IsActive && k.Scope == FinanceScope.Sube)
                .Select(k => new SelectListItem(k.Name, k.Id.ToString())).ToList();
            TargetPosOptions = new PosService().GetAll()
                .Where(p => p.BranchId == branchId && p.IsActive && p.Scope == FinanceScope.Sube)
                .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();

            // Varsayılan nakit devir = KIOSK kasalarındaki devredilebilir nakit (şube kasaları hariç).
            var kioskCashIds = new CashRegisterService().GetAll()
                .Where(k => k.BranchId == branchId && k.Scope == FinanceScope.Kiosk).Select(k => k.Id).ToHashSet();
            DefaultCashTransfer = new CashMovementService().GetBalances(branchId)
                .Where(b => kioskCashIds.Contains(b.CashRegisterId)).Sum(b => b.Balance);
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public static string Money(decimal v) => "₺" + v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
