using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Models;

namespace MindAeonRest.Licence.Portal.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TicketsModel(IHttpClientFactory factory, IConfiguration config, IHttpContextAccessor accessor) : PageModel
{
    private readonly AdminApi _api = new(factory, config, accessor);

    public IReadOnlyList<TicketListItem> Items { get; private set; } = [];
    public TicketDetail? Detail { get; private set; }

    [BindProperty(SupportsGet = true)] public TicketStatus? Status { get; set; }

    private string ListPath => Status is { } s ? $"/api/v1/tickets?status={(int)s}" : "/api/v1/tickets";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<TicketListItem>>(ListPath, ct) ?? [];
    }

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        Items = await _api.GetAsync<List<TicketListItem>>(ListPath, ct) ?? [];
        return Partial("_TicketsTable", this);
    }

    public async Task<IActionResult> OnGetDetailAsync(int id, CancellationToken ct)
    {
        Detail = await _api.GetAsync<TicketDetail>($"/api/v1/tickets/{id}", ct);
        return Partial("_TicketDetail", this);
    }

    public async Task<IActionResult> OnPostReplyAsync(int id, string body, bool isInternal, CancellationToken ct)
    {
        var (ok, err) = await _api.PostAsync($"/api/v1/tickets/{id}/messages", new { body, isInternal }, ct);
        return await DetailRefreshAsync(id, ok, err, "Mesaj eklendi.", ct);
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, TicketStatus status, CancellationToken ct)
    {
        var (ok, err) = await _api.PutAsync($"/api/v1/tickets/{id}/status", new { status }, ct);
        return await DetailRefreshAsync(id, ok, err, "Durum güncellendi.", ct);
    }

    public async Task<IActionResult> OnPostAssignAsync(int id, string? assignedToUserId, CancellationToken ct)
    {
        var (ok, err) = await _api.PutAsync($"/api/v1/tickets/{id}/assign", new { assignedToUserId }, ct);
        return await DetailRefreshAsync(id, ok, err, "Atama güncellendi.", ct);
    }

    private async Task<IActionResult> DetailRefreshAsync(int id, bool ok, string? err, string okMsg, CancellationToken ct)
    {
        if (!ok)
        {
            Response.StatusCode = 400;
            Response.Headers["HX-Trigger"] = Toast(err ?? "İşlem başarısız.", "danger");
            return Content("");
        }
        Response.Headers["HX-Trigger"] = Toast(okMsg, "success");
        Detail = await _api.GetAsync<TicketDetail>($"/api/v1/tickets/{id}", ct);
        return Partial("_TicketDetail", this);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
