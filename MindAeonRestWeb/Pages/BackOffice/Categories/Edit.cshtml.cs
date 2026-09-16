using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Categories;

public class EditModel : PageModel
{
    private readonly ProductCategoryService _svc = new();

    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public bool RemoveImage { get; set; }

    public int Id { get; private set; }
    public bool IsNew => Id == 0;
    public bool HasImage { get; private set; }
    public string? Error { get; private set; }

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.ProductBasic);

    public IActionResult OnGet(int? id)
    {
        if (!CanAccess) return Forbid();
        if (id is int i && i > 0)
        {
            var c = _svc.GetById(i);
            if (c is null) return RedirectToPage("Index");
            Id = c.Id; Code = c.Code; Name = c.Name; IsActive = c.IsActive; HasImage = c.HasImage;
        }
        return Page();
    }

    public IActionResult OnPost(int? id, IFormFile? imageFile)
    {
        if (!CanAccess) return Forbid();
        Id = id ?? 0;
        HasImage = Id > 0 && (_svc.GetById(Id)?.HasImage ?? false);

        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            Error = "Kod ve ad zorunludur.";
            return Page();
        }

        var cat = new ProductCategory { Id = Id, Code = Code, Name = Name, IsActive = IsActive };

        // Resim: yeni yüklendiyse onu al; "kaldır" işaretliyse null; aksi halde mevcut resmi koru.
        if (imageFile is { Length: > 0 })
        {
            using var ms = new MemoryStream();
            imageFile.CopyTo(ms);
            cat.Image = ms.ToArray();
        }
        else if (!RemoveImage && Id > 0)
        {
            cat.Image = _svc.GetImage(Id);   // mevcut resmi koru (Save resmi Image alanından yazıyor)
        }

        try
        {
            _svc.Save(cat, Session.CurrentUserId);
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
