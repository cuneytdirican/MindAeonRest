using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.ReyonRouting;

/// <summary>
/// Reyon Yönlendirme (Lokasyon → Kategori → Reyon). WPF ReyonRouteMatrixWindow ile birebir servis çağrıları:
/// okuma <see cref="ReyonRouteService.GetAll"/>, satır yazma <see cref="ReyonRouteService.Save"/>,
/// satır silme <see cref="ReyonRouteService.Delete"/>. Eksenler <see cref="ZoneService.GetAll"/>,
/// <see cref="MindAeonRest.Services.ReyonService.GetAll"/>, <see cref="ProductCategoryService.GetAll"/>,
/// <see cref="BranchService.GetByCompany"/>.
///
/// Matris seçilen LOKASYON (zone) bazlıdır. Her ürün kategorisi için bu lokasyonda hangi reyona düşeceği
/// seçilir; ayrıca lokasyonun VARSAYILAN reyonu (CategoryId=null, ProductId=null kaydı) seçilir. WPF fallback
/// çözümlemesi (ReyonRouteService.Resolve): (zone+ürün) > (ürün) > (zone+kategori) > (kategori) > (zone varsayılan).
/// Bu ekran ürün-özel kuralları düzenlemez; kategori kuralları + lokasyon varsayılanı ile aynı düzeni yazar.
/// Yalnız SEÇİLİ LOKASYONUN reyonları seçilebilir (yanlış bara yönlendirme imkânsız). Boş (0) seçim → kural
/// yoksa atlanır, varsa silinir; böylece kategori "varsayılana düşsün" durumuna döner.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReyonRouteService _routes = new();
    private readonly ZoneService _zoneSvc = new();
    private readonly ReyonService _reyonSvc = new();
    private readonly ProductCategoryService _catSvc = new();
    private readonly BranchService _branchSvc = new();

    [BindProperty(SupportsGet = true)] public int BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int ZoneId { get; set; }

    public List<CompanyBranch> Branches { get; private set; } = new();
    public List<Zone> Zones { get; private set; } = new();
    public List<Reyon> Reyons { get; private set; } = new();
    public List<CategoryRow> Rows { get; private set; } = new();
    public int DefaultReyonId { get; private set; }
    public string? Message { get; private set; }
    public string? Hint { get; private set; }

    // Mutfak/reyon özelliği — yalnız SysAdmin/Patron + Feature.Kitchen (Reyons ekranı ile aynı gate).
    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.Kitchen);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        if (TempData["ReyonRouteResult"] is string msg) Message = msg;
        Load();
        return Page();
    }

    /// <summary>
    /// Seçili lokasyon için kategori→reyon kurallarını + lokasyon varsayılan reyonunu kaydeder.
    /// WPF Save_Click ile birebir: reyon &gt; 0 ise Save (upsert), aksi halde mevcut kural varsa Delete.
    /// </summary>
    public IActionResult OnPostSaveAll(int branchId, int zoneId, int[] categoryId, int[] reyonId, int defaultReyonId)
    {
        if (!CanAccess) return Forbid();
        if (zoneId <= 0) { TempData["ReyonRouteResult"] = "Lokasyon seçin."; return RedirectToPage(new { BranchId = branchId, ZoneId = zoneId }); }

        int? uid = Session.CurrentUserId;
        int saved = 0, cleared = 0;
        var errors = new List<string>();

        // Bu lokasyona ait mevcut kayıtlar (upsert/delete kararı için).
        var existing = _routes.GetAll().Where(r => r.SourceZoneId == zoneId).ToList();

        // Kategori → reyon kuralları.
        int n = categoryId.Length;
        for (int i = 0; i < n; i++)
        {
            int cat = categoryId[i];
            int rey = i < reyonId.Length ? reyonId[i] : 0;
            var rule = existing.FirstOrDefault(r => r.CategoryId == cat && r.ProductId == null);
            try
            {
                if (rey > 0)
                {
                    _routes.Save(new ReyonRoute { Id = rule?.Id ?? 0, SourceZoneId = zoneId, CategoryId = cat, ProductId = null, ReyonId = rey }, uid);
                    saved++;
                }
                else if (rule is not null)
                {
                    _routes.Delete(rule.Id, uid);
                    cleared++;
                }
            }
            catch (Exception ex) { errors.Add($"Kategori #{cat}: {ex.Message}"); }
        }

        // Lokasyon varsayılan reyonu (CategoryId=null, ProductId=null).
        var def = existing.FirstOrDefault(r => r.CategoryId == null && r.ProductId == null);
        try
        {
            if (defaultReyonId > 0)
            {
                _routes.Save(new ReyonRoute { Id = def?.Id ?? 0, SourceZoneId = zoneId, CategoryId = null, ProductId = null, ReyonId = defaultReyonId }, uid);
                saved++;
            }
            else if (def is not null)
            {
                _routes.Delete(def.Id, uid);
                cleared++;
            }
        }
        catch (Exception ex) { errors.Add($"Varsayılan reyon: {ex.Message}"); }

        TempData["ReyonRouteResult"] = errors.Count > 0
            ? $"{saved} kaydedildi, {cleared} temizlendi, {errors.Count} hata: {string.Join(" | ", errors.Take(10))}"
            : $"Yönlendirme kuralları kaydedildi. ({saved} kural yazıldı, {cleared} temizlendi.) Bu lokasyondan verilen siparişler ilgili reyona/yazıcıya düşer.";

        return RedirectToPage(new { BranchId = branchId, ZoneId = zoneId });
    }

    private void Load()
    {
        int companyId = Session.CurrentCompany?.Id ?? 0;
        Branches = companyId == 0 ? new() : _branchSvc.GetByCompany(companyId);

        // Seçili şube yoksa ilk şubeye düş.
        if (BranchId <= 0 && Branches.Count > 0) BranchId = Branches[0].Id;

        // Bu şubenin aktif lokasyonları.
        Zones = BranchId <= 0
            ? new()
            : _zoneSvc.GetAll().Where(z => z.BranchId == BranchId && z.IsActive).OrderBy(z => z.Code).ToList();

        // Seçili lokasyon bu şubede değilse ilk lokasyona düş.
        if (Zones.Count > 0 && !Zones.Any(z => z.Id == ZoneId)) ZoneId = Zones[0].Id;
        if (Zones.Count == 0) ZoneId = 0;

        if (ZoneId <= 0)
        {
            Hint = Zones.Count == 0
                ? "Bu şubede lokasyon yok — önce Lokasyon Yönetimi ekranından lokasyon ekleyin."
                : "Lokasyon seçin.";
            return;
        }

        // Yalnız BU lokasyonun aktif reyonları (zone-kısıtlı → yanlış bara yönlendirme imkânsız).
        Reyons = _reyonSvc.GetAll().Where(r => r.ZoneId == ZoneId && r.IsActive).ToList();
        if (Reyons.Count == 0)
        {
            Hint = "Bu lokasyonda reyon yok — önce Reyonlar (Mutfak) ekranından bu lokasyona reyon ekleyin.";
        }

        var existing = _routes.GetAll().Where(r => r.SourceZoneId == ZoneId).ToList();

        // Varsayılan reyon = (zone, kategori yok, ürün yok) kuralı.
        DefaultReyonId = existing.FirstOrDefault(r => r.CategoryId == null && r.ProductId == null)?.ReyonId ?? 0;

        // Kategori satırları (aktif kategoriler).
        var cats = _catSvc.GetAll().Where(c => c.IsActive).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Rows = cats.Select(c =>
        {
            var rule = existing.FirstOrDefault(r => r.CategoryId == c.Id && r.ProductId == null);
            return new CategoryRow { CategoryId = c.Id, CategoryName = c.Name, ReyonId = rule?.ReyonId ?? 0 };
        }).ToList();

        if (Hint is null)
            Hint = $"{Reyons.Count} reyon · {cats.Count} kategori. Boş bırakılan kategori, lokasyon varsayılan reyonuna düşer.";
    }

    public class CategoryRow
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int ReyonId { get; set; }
    }
}
