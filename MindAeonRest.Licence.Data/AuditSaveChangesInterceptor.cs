using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MindAeonRest.Licence.Data;

/// <summary>
/// SaveChanges sırasında:
///  - Added → CreatedUtc/CreatedByUserId; Modified → UpdatedUtc/UpdatedByUserId otomatik damgalanır.
///  - Hard delete yerine soft-delete (IsDeleted/DeletedUtc) uygulanır.
///  - Kritik değişiklikler (Dealer.MarginPercent, PackagePrice.BasePrice, License.Status ve
///    diğer tüm BaseEntity hareketleri) AuditLog'a yazılır.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ITenantInfo tenant) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext ctx)
    {
        var now = DateTime.UtcNow;
        var userId = tenant.UserId;
        var ip = tenant.Ip;
        var audits = new List<AuditLog>();

        // AuditLog'un kendisini denetleme; ToList ile snapshot al (eklerken koleksiyon değişmesin)
        var entries = ctx.ChangeTracker.Entries<BaseEntity>().ToList();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedUtc = now;
                    entry.Entity.CreatedByUserId ??= userId;
                    audits.Add(Build("Added", entry, userId, ip, now, before: false, after: true));
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedUtc = now;
                    entry.Entity.UpdatedByUserId = userId;
                    audits.Add(Build("Modified", entry, userId, ip, now, before: true, after: true));
                    break;

                case EntityState.Deleted:
                    // Hard delete'i soft-delete'e çevir
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedUtc = now;
                    entry.Entity.UpdatedUtc = now;
                    entry.Entity.UpdatedByUserId = userId;
                    audits.Add(Build("Deleted", entry, userId, ip, now, before: true, after: false));
                    break;
            }
        }

        if (audits.Count > 0)
            ctx.Set<AuditLog>().AddRange(audits);
    }

    private static AuditLog Build(
        string action, EntityEntry<BaseEntity> entry, string? userId, string? ip, DateTime now,
        bool before, bool after)
    {
        return new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entry.Entity.Id == 0 ? null : entry.Entity.Id.ToString(),
            BeforeJson = before ? Snapshot(entry, original: true) : null,
            AfterJson = after ? Snapshot(entry, original: false) : null,
            Ip = ip,
            Utc = now
        };
    }

    /// <summary>Kritik + temel alanların JSON anlık görüntüsü (özellikle marj/fiyat/durum).</summary>
    private static string Snapshot(EntityEntry<BaseEntity> entry, bool original)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (p.Metadata.IsPrimaryKey()) continue;
            if (p.Metadata.Name == nameof(BaseEntity.RowVersion)) continue;
            dict[p.Metadata.Name] = original ? p.OriginalValue : p.CurrentValue;
        }
        return JsonSerializer.Serialize(dict, JsonOpts);
    }
}
