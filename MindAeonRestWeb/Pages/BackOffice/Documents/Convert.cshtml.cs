using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Documents;

/// <summary>Kaynaktan dönüşüm — WPF DocumentConvertWindow birebir karşılığı.
/// Hedef tip (?type) için SourceType() kaynak belgeleri (yalnız Onaylandı, henüz TAM dönüşmemiş) listelenir;
/// çoklu seçim → DocumentService.CreateDraftFromSources → yeni taslak → Edit'e yönlendirir.
/// Zaten tam dönüşmüş kaynaklar GetConvertedSourceIds ile hariç tutulur.</summary>
public class ConvertModel : PageModel
{
    private readonly DocumentService _docs = new();

    [BindProperty(SupportsGet = true)] public DocType Type { get; set; } = DocType.SatisFatura;
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty] public List<int> SelectedIds { get; set; } = new();

    public List<Document> Sources { get; private set; } = new();
    public string? Error { get; private set; }

    public DocType? SourceType => Type.SourceType();
    public string TargetTitle => Type.ToTr();
    public string SourceTitle => SourceType?.ToTr() ?? "";

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.PreAccounting);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        if (SourceType is null) return RedirectToPage("Index", new { type = (int)Type });
        From ??= DateTime.Today.AddMonths(-1);
        To ??= DateTime.Today;
        Error = TempData["DocError"] as string;
        Load();
        return Page();
    }

    public IActionResult OnPostCreate()
    {
        if (!CanAccess) return Forbid();
        if (SourceType is null) return RedirectToPage("Index", new { type = (int)Type });
        var ids = (SelectedIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            From ??= DateTime.Today.AddMonths(-1);
            To ??= DateTime.Today;
            Error = "En az bir kaynak evrak seçin.";
            Load();
            return Page();
        }
        try
        {
            int newId = _docs.CreateDraftFromSources(Type, ids, Session.CurrentUserId);
            TempData["DocMessage"] = "Kaynaktan taslak oluşturuldu.";
            return RedirectToPage("Edit", new { type = (int)Type, id = newId });
        }
        catch (Exception ex)
        {
            From ??= DateTime.Today.AddMonths(-1);
            To ??= DateTime.Today;
            Error = ex.Message;
            Load();
            return Page();
        }
    }

    /// <summary>Yalnız Onaylandı kaynaklar; hedef tipe TAM dönüşmüş olanlar hariç.</summary>
    private void Load()
    {
        var converted = _docs.GetConvertedSourceIds(Type);
        Sources = _docs.GetList(SourceType!.Value, From, To)
            .Where(d => d.Status == DocStatus.Onaylandi && !converted.Contains(d.Id))
            .ToList();
    }
}
