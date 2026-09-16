using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.CashMovements;

/// <summary>Kasa Hareketleri — salt-okunur liste (WPF <c>KasaWindow</c> birebir karşılığı):
/// nakit kasa seç + tarih aralığı → hareketler (tarih/tip/tutar/açıklama/yürüyen bakiye) + özet.
/// Ekleme/tahsilat/ödeme YOK; bunlar Dekont/WPF üzerinden yapılır. RAPOR deseni (filtre → tablo).
/// Kasalar arası nakit transfer formu mevcut (WPF KasaWindow → KasaIslemWindow karşılığı).</summary>
public class IndexModel : PageModel
{
    private readonly CashMovementService _cash = new();

    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public int? CashRegisterId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> CashOptions { get; private set; } = new();

    // ── Sonuç ──
    public CashBalance? Cash { get; private set; }
    public List<Row> Rows { get; private set; } = new();

    // Özet
    public decimal TotalIn { get; private set; }
    public decimal TotalOut { get; private set; }
    public decimal NetPeriod => TotalIn - TotalOut;
    public decimal ClosingBalance { get; private set; }   // kasanın anlık (tüm zaman) bakiyesi

    public string? CurrencyCode { get; private set; }
    public string? Error { get; private set; }
    public string? Success { get; private set; }

    // WPF KasaWindow (nakit kasa) — çekirdek özellik (nav "Nakit Kasalar" lisans gerektirmez):
    // yalnızca admin/Patron erişebilir, ayrı lisans şartı yok.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    // Aktif şube (WPF: Session.CurrentBranch?.Id ?? 0) — kasalar şubeye göre süzülür.
    private static int BranchId => Session.CurrentBranch?.Id ?? 0;

    // ── Transfer (WPF KasaWindow → KasaIslemWindow transfer karşılığı) ──
    // Aynı döviz iki kasa arası; Core CashMovementService.Transfer(fromId,toId,amount,note,userId) çağrılır.
    [BindProperty] public int TransferFromId { get; set; }
    [BindProperty] public int TransferToId { get; set; }
    [BindProperty] public string TransferAmount { get; set; } = "";
    [BindProperty] public string? TransferNote { get; set; }

    public IActionResult OnGet(bool ok = false)
    {
        if (!CanAccess) return Forbid();
        Prepare();
        Build();
        if (ok) Success = "Transfer gerçekleştirildi.";
        return Page();
    }

    /// <summary>İki kasa arası transfer → PRG (başarı/hatada mesaj + liste tazelenir).</summary>
    public IActionResult OnPostTransfer()
    {
        if (!CanAccess) return Forbid();

        if (TransferFromId == 0 || TransferToId == 0)
        {
            Prepare(); Build();
            Error = "Kaynak ve hedef kasa seçiniz.";
            return Page();
        }

        if (!Num.TryParseDecimal(TransferAmount, out var amount) || amount <= 0)
        {
            Prepare(); Build();
            Error = "Geçerli bir tutar giriniz (0'dan büyük).";
            return Page();
        }

        string? note = string.IsNullOrWhiteSpace(TransferNote) ? null : TransferNote.Trim();

        try
        {
            _cash.Transfer(TransferFromId, TransferToId, amount, note, Session.CurrentUserId);
            // PRG: kaynak kasa seçili + başarı mesajı ile geri dön.
            return RedirectToPage(new { CashRegisterId = TransferFromId, ok = true });
        }
        catch (Exception ex)
        {
            Prepare(); Build();
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>Filtre varsayılanları + kasa seçenekleri (GetBalances, şubeye göre).</summary>
    private void Prepare()
    {
        var balances = _cash.GetBalances(BranchId);
        CashOptions = balances
            .Select(k => new SelectListItem($"{k.Name}{(string.IsNullOrEmpty(k.CurrencyCode) ? "" : " · " + k.CurrencyCode)}", k.CashRegisterId.ToString()))
            .ToList();

        CashRegisterId ??= balances.FirstOrDefault()?.CashRegisterId;
        var today = DateTime.Today;
        From ??= new DateTime(today.Year, today.Month, 1);
        To ??= today;
    }

    /// <summary>Seçilen kasanın hareketlerini tarih aralığına göre süz, yürüyen bakiye + özet üret.</summary>
    private void Build()
    {
        if (CashRegisterId is not int cashId) { Error = "Kasa bulunamadı."; return; }
        try
        {
            var balances = _cash.GetBalances(BranchId);
            Cash = balances.FirstOrDefault(x => x.CashRegisterId == cashId);
            if (Cash is null) { Error = "Kasa bulunamadı."; return; }
            CurrencyCode = Cash.CurrencyCode;
            ClosingBalance = Cash.Balance;

            DateTime fromD = (From ?? DateTime.Today).Date;
            DateTime toEx = (To ?? DateTime.Today).Date.AddDays(1); // gün sonu dahil

            // GetMovements Id DESC döner (en yeni önce), tüm zaman. Bakiye kronolojik yürütülür.
            var all = _cash.GetMovements(cashId, top: 100000)
                .OrderBy(m => m.MoveDate).ThenBy(m => m.Id)
                .ToList();

            decimal running = 0m;
            var rows = new List<Row>();
            foreach (var m in all)
            {
                running += m.Amount; // Amount işaretli (+ giriş, - çıkış)
                var local = m.MoveDate.ToLocalTime();
                if (local < fromD || local >= toEx) continue; // aralık dışı → bakiyeyi yürüt ama gösterme
                if (m.Amount >= 0) TotalIn += m.Amount; else TotalOut += -m.Amount;
                rows.Add(new Row
                {
                    DateText = m.DateText,
                    TypeText = m.TypeText,
                    Amount = m.Amount,
                    Note = m.Note,
                    RunningBalance = running
                });
            }
            rows.Reverse(); // en yeni üstte (WPF Id DESC)
            Rows = rows;
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public class Row
    {
        public string DateText { get; set; } = "";
        public string TypeText { get; set; } = "";
        public decimal Amount { get; set; }
        public string? Note { get; set; }
        public decimal RunningBalance { get; set; }
        public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
        public string RunningText => RunningBalance.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
    }

    public static string Money(decimal v) => v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
