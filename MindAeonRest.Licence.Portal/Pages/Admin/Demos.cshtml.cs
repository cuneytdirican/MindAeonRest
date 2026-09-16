using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DemosModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<DemoItem> Items { get; private set; } = [];

    public int Total => Items.Count;
    public int Converted => Items.Count(d => d.Converted);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<DemoItem>>("/api/v1/demos", ct) ?? [];
    }

    public async Task<IActionResult> OnPostConvertAsync(int id, int? licenseId, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync($"/api/v1/demos/{id}/convert", new { licenseId }, ct);
        if (!ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = Toast(err ?? "İşlem başarısız.", "danger");
            return Content("");
        }
        Response.Headers["HX-Trigger"] = Toast("Demo dönüştürüldü olarak işaretlendi.", "success");
        Items = await _api.GetAsync<List<DemoItem>>("/api/v1/demos", ct) ?? [];
        return Partial("_DemosTable", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
