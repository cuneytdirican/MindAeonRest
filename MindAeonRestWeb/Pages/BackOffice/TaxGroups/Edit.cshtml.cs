using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.TaxGroups;

public class EditModel : PageModel
{
    private readonly TaxGroupService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;

    /// <summary>Oranlar alt-tablosu — form satırlarından indeksli POST (Rates[i].X).</summary>
    [BindProperty] public List<RateInput> Rates { get; set; } = new();

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    // Vergi grupları Core kapsamında (Feature yok) — sadece SysAdmin/Patron.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("Patron");

    /// <summary>TİP seçenekleri (VatType enum): (value=int, text).</summary>
    public static IReadOnlyList<(int Value, string Text)> VatTypeOptions { get; } =
        Enum.GetValues<VatType>().Select(v => ((int)v, v.ToTr())).ToList();

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var g = _svc.GetById(i);
            if (g is null) return RedirectToPage("Index");
            Id = g.Id; Code = g.Code; Name = g.Name; IsActive = g.IsActive;
            Rates = _svc.GetRates(g.Id).Select(r => new RateInput
            {
                VatType = (int)r.VatType,
                Rate = r.Rate,
                StartDate = r.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = r.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
            }).ToList();
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;

        // Boş satırları at (tümüyle boş kalan şablon satırlar).
        Rates = (Rates ?? new()).Where(r => !r.IsEmpty).ToList();

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }

        var rates = new List<TaxGroupRate>();
        for (int i = 0; i < Rates.Count; i++)
        {
            var r = Rates[i];
            if (r.Rate < 0)
            {
                Error = $"{i + 1}. satır: oran negatif olamaz.";
                return Page();
            }
            if (!TryParseDate(r.StartDate, out var start))
            {
                Error = $"{i + 1}. satır: geçerli bir başlangıç tarihi giriniz.";
                return Page();
            }
            DateTime? end = null;
            if (!string.IsNullOrWhiteSpace(r.EndDate))
            {
                if (!TryParseDate(r.EndDate, out var e2))
                {
                    Error = $"{i + 1}. satır: geçerli bir bitiş tarihi giriniz.";
                    return Page();
                }
                if (e2 < start)
                {
                    Error = $"{i + 1}. satır: bitiş tarihi başlangıçtan önce olamaz.";
                    return Page();
                }
                end = e2;
            }
            rates.Add(new TaxGroupRate
            {
                VatType = (VatType)r.VatType,
                Rate = r.Rate,
                StartDate = start,
                EndDate = end
            });
        }

        var group = new TaxGroup { Id = Id, Code = Code, Name = Name, IsActive = IsActive };

        try
        {
            _svc.Save(group, rates, Session.CurrentUserId);   // atomik: grup + oranlar tek çağrı
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private static bool TryParseDate(string? s, out DateTime value)
        => DateTime.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>Formdan gelen tek oran satırı (indeksli bind).</summary>
    public class RateInput
    {
        public int VatType { get; set; } = (int)MindAeonRest.Models.VatType.Satis;
        public decimal Rate { get; set; }
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";

        public bool IsEmpty => Rate == 0 && string.IsNullOrWhiteSpace(StartDate) && string.IsNullOrWhiteSpace(EndDate);
    }
}
