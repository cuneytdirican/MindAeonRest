using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;
using MindAeonRestWeb.Infrastructure;

namespace MindAeonRestWeb.Pages.BackOffice.PosMovements;

/// <summary>POS Hareketleri — salt-okunur liste (WPF <c>PosWindow</c> birebir karşılığı):
/// POS seç + tarih aralığı → hareketler (tarih/tip/tutar/açıklama/yürüyen bakiye) + özet.
/// Ekleme/ödeme/transfer YOK; bunlar Dekont/WPF üzerinden yapılır. RAPOR deseni (filtre → tablo).</summary>
public class IndexModel : PageModel
{
    private readonly PosMovementService _pos = new();

    // ── Filtre ──
    [BindProperty(SupportsGet = true)] public int? PosId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<SelectListItem> PosOptions { get; private set; } = new();

    // ── Sonuç ──
    public PosBalance? Pos { get; private set; }
    public List<Row> Rows { get; private set; } = new();

    // Özet
    public decimal TotalIn { get; private set; }
    public decimal TotalOut { get; private set; }
    public decimal NetPeriod => TotalIn - TotalOut;
    public decimal ClosingBalance { get; private set; }   // POS'un anlık (tüm zaman) bakiyesi

    public string? CurrencyCode { get; private set; }
    public string? Error { get; private set; }
    public string? Success { get; private set; }

    // WPF PosWindow (ön-muhasebe) — admin/Patron + Feature.PreAccounting.
    private static bool CanAccess =>
        (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    // ── Transfer (WPF PosWindow → transfer karşılığı) ──
    // Aynı döviz iki POS arası; Core PosMovementService.Transfer(fromId,toId,amount,note,userId) çağrılır.
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

    /// <summary>İki POS arası transfer → PRG (başarı/hatada mesaj + liste tazelenir).</summary>
    public IActionResult OnPostTransfer()
    {
        if (!CanAccess) return Forbid();

        if (TransferFromId == 0 || TransferToId == 0)
        {
            Prepare(); Build();
            Error = "Kaynak ve hedef POS seçiniz.";
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
            _pos.Transfer(TransferFromId, TransferToId, amount, note, Session.CurrentUserId);
            // PRG: kaynak POS seçili + başarı mesajı ile geri dön.
            return RedirectToPage(new { PosId = TransferFromId, ok = true });
        }
        catch (Exception ex)
        {
            Prepare(); Build();
            Error = ex.Message;
            return Page();
        }
    }

    /// <summary>Filtre varsayılanları + POS seçenekleri (GetBalances).</summary>
    private void Prepare()
    {
        var balances = _pos.GetBalances();
        PosOptions = balances
            .Select(p => new SelectListItem($"{p.Name}{(string.IsNullOrEmpty(p.BankName) ? "" : " · " + p.BankName)}", p.PosId.ToString()))
            .ToList();

        PosId ??= balances.FirstOrDefault()?.PosId;
        var today = DateTime.Today;
        From ??= new DateTime(today.Year, today.Month, 1);
        To ??= today;
    }

    /// <summary>Seçilen POS'un hareketlerini tarih aralığına göre süz, yürüyen bakiye + özet üret.</summary>
    private void Build()
    {
        if (PosId is not int posId) { Error = "POS bulunamadı."; return; }
        try
        {
            var balances = _pos.GetBalances();
            Pos = balances.FirstOrDefault(x => x.PosId == posId);
            if (Pos is null) { Error = "POS bulunamadı."; return; }
            CurrencyCode = Pos.CurrencyCode;
            ClosingBalance = Pos.Balance;

            DateTime fromD = (From ?? DateTime.Today).Date;
            DateTime toEx = (To ?? DateTime.Today).Date.AddDays(1); // gün sonu dahil

            // GetMovements Id DESC döner (en yeni önce), tüm zaman. Bakiye kronolojik yürütülür.
            var all = _pos.GetMovements(posId, top: 100000)
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
                    CustomerName = m.CustomerName,
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
        public string? CustomerName { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
        public decimal RunningBalance { get; set; }
        public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
        public string RunningText => RunningBalance.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
    }

    public static string Money(decimal v) => v.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
