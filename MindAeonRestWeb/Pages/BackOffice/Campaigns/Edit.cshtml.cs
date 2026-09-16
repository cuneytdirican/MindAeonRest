using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Campaigns;

public class EditModel : PageModel
{
    private readonly CampaignService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public int Type { get; set; } = (int)CampaignType.TumSiparisYuzde;
    [BindProperty] public decimal Value { get; set; }

    [BindProperty] public int TargetCategoryId { get; set; }   // 0 = seçilmedi
    [BindProperty] public int TargetProductId { get; set; }    // 0 = seçilmedi
    [BindProperty] public decimal MinAmount { get; set; }
    [BindProperty] public decimal BuyQty { get; set; }
    [BindProperty] public decimal PayQty { get; set; }

    // Tarih aralığı (yyyy-MM-dd; type="date"). WPF gg.aa.yyyy SS:dd yerine gün bazında.
    [BindProperty] public string StartDate { get; set; } = "";
    [BindProperty] public string EndDate { get; set; } = "";

    // Günler (bit maske bileşenleri) + günlük saat aralığı (SS:dd, opsiyonel happy hour)
    [BindProperty] public bool[] Days { get; set; } = new bool[7];   // 0=Pzt ... 6=Paz
    [BindProperty] public string? DailyStart { get; set; }
    [BindProperty] public string? DailyEnd { get; set; }

    [BindProperty] public bool AllBranches { get; set; } = true;
    [BindProperty] public int[] BranchIds { get; set; } = Array.Empty<int>();

    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public bool Stackable { get; set; }
    [BindProperty] public bool IsMandatory { get; set; }

    // Hediye/İkram alt-koleksiyonları (WPF Conditions/Gifts karşılığı)
    [BindProperty] public List<ConditionRow> Conditions { get; set; } = new();
    [BindProperty] public List<GiftRow> Gifts { get; set; } = new();

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> TypeOptions { get; private set; } = new();
    public List<SelectListItem> CategoryOptions { get; private set; } = new();
    public List<SelectListItem> ProductOptions { get; private set; } = new();
    public List<BranchSel> Branches { get; private set; } = new();

    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.Campaign);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var c = _svc.GetById(i);
            if (c is null) return RedirectToPage("Index");
            Id = c.Id;
            Code = c.Code;
            Name = c.Name;
            Description = c.Description;
            Type = (int)c.Type;
            Value = c.Value;
            TargetCategoryId = c.TargetCategoryId ?? 0;
            TargetProductId = c.TargetProductId ?? 0;
            MinAmount = c.MinAmount;
            BuyQty = c.BuyQty;
            PayQty = c.PayQty;
            StartDate = c.StartDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            EndDate = c.EndDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int b = 0; b < 7; b++) Days[b] = (c.DaysOfWeek & (1 << b)) != 0;
            DailyStart = MinToText(c.DailyStartMin);
            DailyEnd = MinToText(c.DailyEndMin);
            AllBranches = c.AllBranches;
            IsActive = c.IsActive;
            Stackable = c.Stackable;
            IsMandatory = c.IsMandatory;
            Conditions = c.Conditions.Select(x => new ConditionRow { ProductId = x.ProductId, MinQty = x.MinQty }).ToList();
            Gifts = c.Gifts.Select(x => new GiftRow { ProductId = x.ProductId, Qty = x.Qty }).ToList();
            foreach (var b in Branches) b.IsSelected = c.BranchIds.Contains(b.Id);
        }
        else
        {
            StartDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            EndDate = DateTime.Today.AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();
        foreach (var b in Branches) b.IsSelected = BranchIds.Contains(b.Id);

        var type = (CampaignType)Type;

        if (string.IsNullOrWhiteSpace(Code)) { Error = "Kod giriniz."; return Page(); }
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Ad giriniz."; return Page(); }
        if (!TryParseDate(StartDate, out var sd)) { Error = "Geçerli bir başlangıç tarihi giriniz."; return Page(); }
        if (!TryParseDate(EndDate, out var ed)) { Error = "Geçerli bir bitiş tarihi giriniz."; return Page(); }
        // Bitiş gün sonuna kadar geçerli olsun.
        ed = ed.Date.AddDays(1).AddSeconds(-1);
        if (ed < sd) { Error = "Bitiş başlangıçtan önce olamaz."; return Page(); }

        decimal value = 0, min = 0, buy = 0, pay = 0;
        if (type is not (CampaignType.XAlYOde or CampaignType.Hediye))
        {
            value = Value;
            if (value < 0) { Error = "Geçerli değer giriniz."; return Page(); }
        }
        if (type is CampaignType.MinTutarYuzde or CampaignType.MinTutarTutar) min = MinAmount;

        int? catId = null, prodId = null;
        if (type == CampaignType.KategoriYuzde)
        {
            if (TargetCategoryId == 0) { Error = "Hedef kategori seçiniz."; return Page(); }
            catId = TargetCategoryId;
        }
        if (type == CampaignType.UrunYuzde || type == CampaignType.XAlYOde)
        {
            if (TargetProductId == 0) { Error = "Hedef ürün seçiniz."; return Page(); }
            prodId = TargetProductId;
        }
        if (type == CampaignType.XAlYOde)
        {
            buy = BuyQty; pay = PayQty;
            if (buy <= 0 || pay < 0 || pay >= buy) { Error = "X Al Y Öde: 'kaç al' > 'kaç öde' olmalı."; return Page(); }
        }

        var conds = new List<CampaignCondition>();
        var gifts = new List<CampaignGift>();
        if (type == CampaignType.Hediye)
        {
            min = MinAmount;   // Hediye'de min tutar opsiyonel koşul
            conds = Conditions.Where(x => x.ProductId > 0 && x.MinQty > 0)
                .Select(x => new CampaignCondition { ProductId = x.ProductId, MinQty = x.MinQty }).ToList();
            gifts = Gifts.Where(x => x.ProductId > 0 && x.Qty > 0)
                .Select(x => new CampaignGift { ProductId = x.ProductId, Qty = x.Qty }).ToList();
            if (gifts.Count == 0) { Error = "En az bir ikram (hediye) ürün ekleyin."; return Page(); }
        }

        var branchIds = AllBranches ? new List<int>() : Branches.Where(b => b.IsSelected).Select(b => b.Id).ToList();
        if (!AllBranches && branchIds.Count == 0) { Error = "En az bir şube seçin ya da 'Tüm şubeler'i işaretleyin."; return Page(); }

        int dow = 0;
        for (int b = 0; b < 7; b++) if (Days[b]) dow |= (1 << b);

        var m = new Campaign
        {
            Id = Id,
            Code = Code,
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Type = type,
            Value = value,
            MinAmount = min,
            BuyQty = buy,
            PayQty = pay,
            TargetCategoryId = catId,
            TargetProductId = prodId,
            StartDateTime = sd,
            EndDateTime = ed,
            DaysOfWeek = dow,
            DailyStartMin = ParseMin(DailyStart),
            DailyEndMin = ParseMin(DailyEnd),
            AllBranches = AllBranches,
            BranchIds = branchIds,
            IsActive = IsActive,
            Stackable = Stackable,
            IsMandatory = IsMandatory,
            Conditions = conds,
            Gifts = gifts
        };

        try
        {
            _svc.Save(m, Session.CurrentUserId);   // Kod normalize + firma-benzersizlik + junction/koşul/ikram Save içinde
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private void LoadLookups()
    {
        TypeOptions = Enum.GetValues<CampaignType>()
            .Select(t => new SelectListItem(t.ToTr(), ((int)t).ToString()))
            .ToList();

        CategoryOptions = new() { new SelectListItem("(Kategori seçiniz)", "0") };
        CategoryOptions.AddRange(new ProductCategoryService().GetAll()
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())));

        ProductOptions = new() { new SelectListItem("(Ürün seçiniz)", "0") };
        ProductOptions.AddRange(new ProductService().GetAll(includeDeleted: false)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())));

        Branches = new();
        if (Session.CurrentCompany is { } co)
            Branches = new BranchService().GetByCompany(co.Id)
                .Select(b => new BranchSel { Id = b.Id, Name = b.Name }).ToList();
    }

    private static string? MinToText(int? min) => min is int m ? $"{m / 60:00}:{m % 60:00}" : null;

    private static int? ParseMin(string? t)
    {
        t = (t ?? "").Trim();
        if (t.Length == 0) return null;
        var parts = t.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return Math.Clamp(h, 0, 23) * 60 + Math.Clamp(m, 0, 59);
        return null;
    }

    private static bool TryParseDate(string? s, out DateTime value)
        => DateTime.TryParse((s ?? "").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public class BranchSel { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsSelected { get; set; } }
    public class ConditionRow { public int ProductId { get; set; } public decimal MinQty { get; set; } = 1; }
    public class GiftRow { public int ProductId { get; set; } public decimal Qty { get; set; } = 1; }
}
