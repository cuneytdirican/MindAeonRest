using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Licence.Portal.Models;
using MindAeonRest.Licence.Portal.Services;

namespace MindAeonRest.Licence.Portal.Pages.Dealer;

[Authorize(Roles = "Dealer")]
public class TicketsModel(ApiClient api) : PageModel
{
    public List<TicketListItemVm> Tickets { get; private set; } = [];
    public TicketDetailRespVm? Selected { get; private set; }

    [BindProperty(SupportsGet = true)] public int? SelectedId { get; set; }

    [TempData] public string? Flash { get; set; }
    [TempData] public string? FlashKind { get; set; }

    // Yeni talep
    [BindProperty] public string Subject { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public int Priority { get; set; } = 1;

    // Yanıt
    [BindProperty] public string ReplyBody { get; set; } = "";

    public int OpenCount => Tickets.Count(t => t.Status != 3);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        Tickets = await api.GetAsync<List<TicketListItemVm>>("/api/v1/tickets", ct) ?? [];
        if (SelectedId is { } id)
            Selected = await api.GetAsync<TicketDetailRespVm>($"/api/v1/tickets/{id}", ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Subject) || string.IsNullOrWhiteSpace(Body))
        {
            Flash = "Konu ve mesaj zorunludur.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        var body = new { subject = Subject.Trim(), body = Body.Trim(), priority = Priority };
        var (resp, error) = await api.PostResultAsync<TicketDetailRespVm>("/api/v1/tickets", body, ct);
        if (resp is null)
        {
            Flash = error ?? "Destek talebi açılamadı.";
            FlashKind = "danger";
            return RedirectToPage();
        }

        Flash = "Destek talebiniz açıldı.";
        FlashKind = "success";
        return RedirectToPage(new { SelectedId = resp.Id });
    }

    public async Task<IActionResult> OnPostReplyAsync(int ticketId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ReplyBody))
        {
            Flash = "Mesaj boş olamaz.";
            FlashKind = "danger";
            return RedirectToPage(new { SelectedId = ticketId });
        }

        var (ok, error) = await api.PostResultAsync($"/api/v1/tickets/{ticketId}/messages", new { body = ReplyBody.Trim(), isInternal = false }, ct);
        if (!ok)
        {
            Flash = error ?? "Mesaj gönderilemedi (talep kapalı olabilir).";
            FlashKind = "danger";
        }
        else
        {
            Flash = "Mesajınız iletildi.";
            FlashKind = "success";
        }
        return RedirectToPage(new { SelectedId = ticketId });
    }
}
