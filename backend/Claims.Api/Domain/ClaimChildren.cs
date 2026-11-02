namespace Claims.Api.Domain;

/// <summary>A question from the assigned officer to the claimant, and the claimant's answer.</summary>
public class InformationRequest
{
    private InformationRequest() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public string Question { get; private set; } = "";
    public string RequestedById { get; private set; } = "";
    public string RequestedByName { get; private set; } = "";
    public DateTime RequestedAt { get; private set; }
    public string? Response { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    /// <summary>Set when the request is closed without an answer (e.g. the claim was rejected).</summary>
    public DateTime? ClosedAt { get; private set; }

    public bool IsOpen => Response is null && ClosedAt is null;

    internal static InformationRequest Create(Guid claimId, Actor officer, string question, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ClaimId = claimId,
        Question = question,
        RequestedById = officer.Id,
        RequestedByName = officer.Name,
        RequestedAt = now
    };

    internal void Answer(string response, DateTime now)
    {
        Response = response;
        RespondedAt = now;
    }

    internal void Close(DateTime now) => ClosedAt = now;
}

/// <summary>Append-only audit trail of who changed what on a claim.</summary>
public class ClaimHistoryEntry
{
    private ClaimHistoryEntry() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string ActorId { get; private set; } = "";
    public string ActorName { get; private set; } = "";
    public UserRole ActorRole { get; private set; }
    public string Action { get; private set; } = "";
    public ClaimStatus? FromStatus { get; private set; }
    public ClaimStatus ToStatus { get; private set; }
    public string? Note { get; private set; }

    internal static ClaimHistoryEntry Create(Guid claimId, Actor actor, string action, ClaimStatus? from, ClaimStatus to, string? note, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ClaimId = claimId,
        OccurredAt = now,
        ActorId = actor.Id,
        ActorName = actor.Name,
        ActorRole = actor.Role,
        Action = action,
        FromStatus = from,
        ToStatus = to,
        Note = note
    };
}

/// <summary>A message for a claimant or officer, produced asynchronously from claim events.</summary>
public class Notification
{
    private Notification() { } // EF Core

    public Guid Id { get; private set; }
    public string RecipientId { get; private set; } = "";
    public Guid ClaimId { get; private set; }
    public string ClaimNumber { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public Guid SourceEventId { get; private set; }

    public static Notification Create(string recipientId, IClaimEvent source, string title, string message) => new()
    {
        Id = Guid.NewGuid(),
        RecipientId = recipientId,
        ClaimId = source.ClaimId,
        ClaimNumber = source.ClaimNumber,
        Title = title,
        Message = message,
        CreatedAt = source.OccurredAt,
        SourceEventId = source.EventId
    };

    public void MarkRead(DateTime now) => ReadAt ??= now;
}
