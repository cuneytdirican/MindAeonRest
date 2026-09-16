using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api.Controllers;

/// <summary>
/// Destek talepleri (ticket) ucu.
/// - Bayi/Müşteri: talep açar, yalnızca kendi taleplerini görür, mesaj ekler (iç-not yazamaz).
/// - Admin: tüm talepleri görür, durum değiştirir/atar, iç-not yazar.
/// Tenant sahipliği hem DB query filter (LicenceDbContext) hem de denetim kontrolleriyle zorlanır.
/// </summary>
[ApiController]
[Route("api/v1/tickets")]
[Authorize]
public class TicketsController(LicenceDbContext db, ITenantInfo tenant) : ControllerBase
{
    private string CurrentUserId =>
        tenant.UserId ?? throw new InvalidOperationException("Kimlik doğrulanmış kullanıcı kimliği yok.");

    /// <summary>BAYİ/MÜŞTERİ: yeni destek talebi açar. Admin de açabilir (Dealer/Business yok). Sahiplik token'dan damgalanır.</summary>
    [HttpPost]
    [Authorize(Roles = "Dealer,Customer,Admin")]
    public async Task<ActionResult<TicketDetailResp>> Create([FromBody] TicketCreateReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Subject)) return BadRequest(new { message = "Konu zorunludur." });
        if (string.IsNullOrWhiteSpace(req.Body)) return BadRequest(new { message = "Mesaj zorunludur." });

        var uid = CurrentUserId;
        var ticket = new Ticket
        {
            OpenerUserId = uid,
            // Sahiplik istemciden DEĞİL, token'dan (tenant) alınır.
            DealerId = tenant.DealerId,
            BusinessId = tenant.BusinessId,
            Subject = req.Subject.Trim(),
            Status = TicketStatus.Open,
            Priority = req.Priority
        };
        ticket.Messages.Add(new TicketMessage
        {
            AuthorUserId = uid,
            Body = req.Body.Trim(),
            IsInternal = false
        });

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = ticket.Id }, ToDetail(ticket, includeInternal: tenant.IsAdmin));
    }

    /// <summary>Talep listesi. Admin: tümü (+ status/atama filtresi). Bayi/Müşteri: yalnızca kendi (query filter zorlar).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketListItem>>> List(
        [FromQuery] TicketStatus? status = null,
        [FromQuery] string? assignedTo = null)
    {
        // Tenant query filter (LicenceDbContext) zaten bayi/müşteriyi kendi verisine kısar.
        var q = db.Tickets.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(t => t.Status == s);
        if (!string.IsNullOrWhiteSpace(assignedTo)) q = q.Where(t => t.AssignedToUserId == assignedTo);

        var list = await q
            .OrderByDescending(t => t.UpdatedUtc ?? t.CreatedUtc)
            .Select(t => new TicketListItem(
                t.Id, t.Subject, t.Status, t.Priority,
                t.DealerId, t.BusinessId, t.OpenerUserId, t.AssignedToUserId,
                t.CreatedUtc, t.UpdatedUtc, t.ClosedUtc,
                t.Messages.Count(m => !m.IsDeleted && (!m.IsInternal || tenant.IsAdmin))))
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>Talep detayı + mesajlar. Sahiplik query filter ile zorlanır (yetkisiz → NotFound). İç-notlar sadece Admin'e.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDetailResp>> Get(int id)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound(new { message = "Talep bulunamadı." });

        return Ok(ToDetail(ticket, includeInternal: tenant.IsAdmin));
    }

    /// <summary>Talebe mesaj ekle. Sahiplik query filter ile zorlanır. IsInternal (iç-not) yalnızca Admin için.</summary>
    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<TicketMessageResp>> AddMessage(int id, [FromBody] TicketMessageReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Body)) return BadRequest(new { message = "Mesaj boş olamaz." });

        var ticket = await db.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound(new { message = "Talep bulunamadı." });
        if (ticket.Status == TicketStatus.Closed)
            return BadRequest(new { message = "Kapalı talebe mesaj eklenemez." });

        // İç-not yalnızca Admin yazabilir; diğerleri için yok sayılır (güvenlik UI'de değil burada).
        bool isInternal = req.IsInternal && tenant.IsAdmin;

        var msg = new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorUserId = CurrentUserId,
            Body = req.Body.Trim(),
            IsInternal = isInternal
        };
        ticket.Messages.Add(msg);
        ticket.UpdatedUtc = DateTime.UtcNow;

        // Müşteri/Bayi cevabı → tekrar Open'a (kapalı değilse). Admin cevabı → Answered (görünür notsa).
        if (tenant.IsAdmin)
        {
            if (!isInternal && ticket.Status != TicketStatus.Closed)
                ticket.Status = TicketStatus.Answered;
        }
        else if (ticket.Status is TicketStatus.Answered or TicketStatus.InProgress)
        {
            ticket.Status = TicketStatus.Open;
        }

        await db.SaveChangesAsync();

        return Ok(new TicketMessageResp(msg.Id, msg.AuthorUserId, msg.Body, msg.IsInternal, msg.CreatedUtc));
    }

    /// <summary>ADMIN: talep durumunu değiştir. Closed → ClosedUtc damgalanır.</summary>
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TicketDetailResp>> ChangeStatus(int id, [FromBody] TicketStatusReq req)
    {
        var ticket = await db.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound(new { message = "Talep bulunamadı." });

        ticket.Status = req.Status;
        ticket.UpdatedUtc = DateTime.UtcNow;
        ticket.ClosedUtc = req.Status == TicketStatus.Closed ? DateTime.UtcNow : null;

        await db.SaveChangesAsync();
        return Ok(ToDetail(ticket, includeInternal: true));
    }

    /// <summary>ADMIN: talebi bir kullanıcıya ata (null = atamayı kaldır). Atanınca InProgress'e çekilir.</summary>
    [HttpPut("{id:int}/assign")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TicketDetailResp>> Assign(int id, [FromBody] TicketAssignReq req)
    {
        var ticket = await db.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound(new { message = "Talep bulunamadı." });

        ticket.AssignedToUserId = string.IsNullOrWhiteSpace(req.AssignedToUserId) ? null : req.AssignedToUserId;
        ticket.UpdatedUtc = DateTime.UtcNow;
        if (ticket.AssignedToUserId is not null && ticket.Status == TicketStatus.Open)
            ticket.Status = TicketStatus.InProgress;

        await db.SaveChangesAsync();
        return Ok(ToDetail(ticket, includeInternal: true));
    }

    // ---- Yardımcılar ----

    private static TicketDetailResp ToDetail(Ticket t, bool includeInternal)
    {
        var msgs = t.Messages
            .Where(m => !m.IsDeleted && (includeInternal || !m.IsInternal))
            .OrderBy(m => m.CreatedUtc)
            .Select(m => new TicketMessageResp(m.Id, m.AuthorUserId, m.Body, m.IsInternal, m.CreatedUtc))
            .ToList();

        return new TicketDetailResp(
            t.Id, t.Subject, t.Status, t.Priority,
            t.DealerId, t.BusinessId, t.OpenerUserId, t.AssignedToUserId,
            t.CreatedUtc, t.UpdatedUtc, t.ClosedUtc, msgs);
    }
}
