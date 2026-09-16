using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Data;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Musteri;

[Authorize(Roles = "Customer")]
public class DestekModel(ApiClient api) : PageModel
{
    public List<TicketVm> Tickets { get; private set; } = new();
    public TicketDetailVm? Selected { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Tickets = (await api.GetAsync<List<TicketVm>>("/api/v1/tickets", ct) ?? new())
            .OrderByDescending(t => t.UpdatedUtc ?? t.CreatedUtc).ToList();

        if (Id is { } id)
            Selected = await api.GetAsync<TicketDetailVm>($"/api/v1/tickets/{id}", ct);
    }

    /// <summary>htmx: yeni destek talebi oluştur → kısmi liste döndür.</summary>
    public async Task<IActionResult> OnPostCreateAsync(string subject, string body, int priority, CancellationToken ct)
    {
        var (ok, error) = await api.PostResultAsync("/api/v1/tickets", new { subject, body, priority }, ct);
        if (!ok)
        {
            Response.StatusCode = 400; // htmx event.detail.successful=false → istemcideki "başarılı" toast'ı bastırır
            Response.Headers["HX-Trigger"] = Toast(error ?? "Destek talebi açılamadı.", "danger");
        }
        Tickets = (await api.GetAsync<List<TicketVm>>("/api/v1/tickets", ct) ?? new())
            .OrderByDescending(t => t.UpdatedUtc ?? t.CreatedUtc).ToList();
        return Partial("_TicketList", Tickets);
    }

    /// <summary>htmx: mevcut talebe mesaj ekle → güncel detayı döndür.</summary>
    public async Task<IActionResult> OnPostReplyAsync(int id, string body, CancellationToken ct)
    {
        var (ok, error) = await api.PostResultAsync($"/api/v1/tickets/{id}/messages", new { body, isInternal = false }, ct);
        if (!ok)
        {
            Response.StatusCode = 400; // htmx başarısızlık → form reset/iyimser akış tetiklenmez
            Response.Headers["HX-Trigger"] = Toast(error ?? "Mesaj gönderilemedi.", "danger");
        }
        Selected = await api.GetAsync<TicketDetailVm>($"/api/v1/tickets/{id}", ct);
        return Partial("_TicketDetail", Selected);
    }

    private static string Toast(string msg, string kind)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["portalToast"] = new { message = msg, kind } });
}
