using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

// ---- Ticket DTO'ları (Endpoints/tickets görevi) ----

/// <summary>Bayi/Müşteri yeni destek talebi açar. DealerId/BusinessId token'dan alınır (istemci gönderemez).</summary>
public record TicketCreateReq(string Subject, string Body, TicketPriority Priority = TicketPriority.Normal);

/// <summary>Listede tek satır özeti.</summary>
public record TicketListItem(
    int Id,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    int MessageCount);

/// <summary>Talep detayı + mesaj akışı.</summary>
public record TicketDetailResp(
    int Id,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    int? DealerId,
    int? BusinessId,
    string OpenerUserId,
    string? AssignedToUserId,
    DateTime CreatedUtc,
    DateTime? UpdatedUtc,
    DateTime? ClosedUtc,
    IReadOnlyList<TicketMessageResp> Messages);

/// <summary>Mesaj satırı. IsInternal mesajlar yalnızca Admin'e döndürülür.</summary>
public record TicketMessageResp(
    int Id,
    string AuthorUserId,
    string Body,
    bool IsInternal,
    DateTime CreatedUtc);

/// <summary>Talebe mesaj ekle. IsInternal (iç-not) yalnızca Admin için geçerlidir; diğerleri için yok sayılır.</summary>
public record TicketMessageReq(string Body, bool IsInternal = false);

/// <summary>ADMIN: durum değiştir.</summary>
public record TicketStatusReq(TicketStatus Status);

/// <summary>ADMIN: talebi bir kullanıcıya ata (null = atamayı kaldır).</summary>
public record TicketAssignReq(string? AssignedToUserId);
