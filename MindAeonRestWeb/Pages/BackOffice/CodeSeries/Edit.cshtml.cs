using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.CodeSeries;

public class EditModel : PageModel
{
    private readonly CodeSeriesService _svc = new();

    [BindProperty] public int EntityType { get; set; } = (int)CodeSeriesType.Product;
    [BindProperty] public int BranchId { get; set; }          // 0 = varsayılan (tüm şubeler)
    [BindProperty] public string? Prefix { get; set; }
    [BindProperty] public int Padding { get; set; } = 6;
    [BindProperty] public int NextNumber { get; set; } = 1;
    [BindProperty] public bool IsActive { get; set; } = true;

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public string? Error { get; private set; }

    public List<SelectListItem> TypeOptions { get; private set; } = new();
    public List<SelectListItem> BranchOptions { get; private set; } = new();

    private static bool CanAccess => Session.IsTenantAdmin;

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        LoadLookups();
        if (id is int i && i > 0)
        {
            var s = _svc.GetById(i);
            if (s is null) return RedirectToPage("Index");
            Id = s.Id;
            EntityType = (int)s.EntityType;
            BranchId = s.BranchId ?? 0;
            Prefix = s.Prefix;
            Padding = s.Padding;
            NextNumber = s.NextNumber;
            IsActive = s.IsActive;
        }
        return Page();
    }

    public IActionResult OnPost(int? id)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        LoadLookups();

        if (!Enum.IsDefined(typeof(CodeSeriesType), EntityType))
        {
            Error = "Geçerli bir kart tipi seçiniz.";
            return Page();
        }
        if (Padding < 1) Padding = 1;
        if (NextNumber < 1) NextNumber = 1;

        var s = new MindAeonRest.Models.CodeSeries
        {
            Id = Id,
            EntityType = (CodeSeriesType)EntityType,
            BranchId = BranchId > 0 ? BranchId : null,
            Prefix = string.IsNullOrWhiteSpace(Prefix) ? null : CodeHelper.Normalize(Prefix),
            Padding = Padding,
            NextNumber = NextNumber,
            IsActive = IsActive
        };

        try
        {
            _svc.Save(s, Session.CurrentUserId);   // upsert: (EntityType + BranchId) doğal anahtarı
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
        TypeOptions = Enum.GetValues<CodeSeriesType>()
            .Select(t => new SelectListItem(t.ToTr(), ((int)t).ToString()))
            .ToList();

        BranchOptions = new() { new SelectListItem("(Tüm şubeler — varsayılan)", "0") };
        if (Session.CurrentCompany is { } co)
            BranchOptions.AddRange(new BranchService().GetByCompany(co.Id)
                .Select(b => new SelectListItem(b.Name, b.Id.ToString())));
    }
}
