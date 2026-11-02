using Claims.Api.Contracts;
using Claims.Api.Domain;
using Claims.Api.Infrastructure.Messaging;
using Claims.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Services;

public sealed class NotificationService(ClaimsDbContext db, TimeProvider clock)
{
    public async Task<NotificationListDto> ListAsync(Actor user, CancellationToken ct)
    {
        var mine = db.Notifications.AsNoTracking().Where(n => n.RecipientId == user.Id);
        var items = await mine
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.ClaimId, n.ClaimNumber, n.Title, n.Message, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        return new NotificationListDto(items, unread);
    }

    public async Task MarkReadAsync(Actor user, Guid id, CancellationToken ct)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == user.Id, ct)
            ?? throw new NotFoundException($"Notification {id} was not found.");
        notification.MarkRead(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Actor user, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var unread = await db.Notifications.Where(n => n.RecipientId == user.Id && n.ReadAt == null).ToListAsync(ct);
        unread.ForEach(n => n.MarkRead(now));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Event consumer that turns claim lifecycle events into notifications for claimants and officers.
/// This is the "receive decisions" path: asynchronous, decoupled from the request that made the change,
/// and the natural place to add email/SMS/push delivery later.
/// </summary>
public sealed class NotificationEventHandler(ClaimsDbContext db, TimeProvider clock) : IClaimEventHandler
{
    public const string ConsumerName = "notifications";

    public async Task HandleAsync(IClaimEvent evt, CancellationToken ct)
    {
        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == evt.EventId && p.Consumer == ConsumerName, ct))
            return; // duplicate delivery

        foreach (var notification in Build(evt))
            db.Notifications.Add(notification);

        db.ProcessedEvents.Add(new ProcessedEvent(evt.EventId, ConsumerName, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<Notification> Build(IClaimEvent evt) => evt switch
    {
        ClaimSubmitted e => [Notification.Create(e.ClaimantId, e, "Claim received",
            $"We have received claim {e.ClaimNumber}. A claims officer will pick it up shortly.")],
        ClaimAssigned e => [Notification.Create(e.ClaimantId, e, "Claim under review",
            $"{e.OfficerName} is now reviewing claim {e.ClaimNumber}.")],
        InformationRequested e => [Notification.Create(e.ClaimantId, e, "More information needed",
            $"Claim {e.ClaimNumber}: {e.Question}")],
        InformationProvided { OfficerId: not null } e => [Notification.Create(e.OfficerId, e, "Claimant responded",
            $"The claimant has provided the requested information on claim {e.ClaimNumber}.")],
        ClaimApproved e => [Notification.Create(e.ClaimantId, e, "Claim approved",
            $"Claim {e.ClaimNumber} has been approved for {e.Currency} {e.ApprovedAmount:N2}. Payment will follow.")],
        ClaimRejected e => [Notification.Create(e.ClaimantId, e, "Claim decision",
            $"Claim {e.ClaimNumber} was not approved. Reason: {e.Reason}")],
        ClaimSettled e => [Notification.Create(e.ClaimantId, e, "Claim settled",
            $"Payment of {e.Currency} {e.SettledAmount:N2} has been issued for claim {e.ClaimNumber}.")],
        _ => []
    };
}
