namespace Claims.Api.Domain;

/// <summary>
/// Integration events describing claim lifecycle changes. They are written to the outbox in the
/// same transaction as the state change and published asynchronously (in-process or Kafka).
/// Keyed by ClaimId so a partitioned transport keeps per-claim ordering.
/// </summary>
public interface IClaimEvent
{
    Guid EventId { get; }
    Guid ClaimId { get; }
    string ClaimNumber { get; }
    DateTime OccurredAt { get; }
}

public abstract record ClaimEventBase(Guid ClaimId, string ClaimNumber, DateTime OccurredAt) : IClaimEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

public record ClaimSubmitted(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, string MarketCode, ClaimType ClaimType, decimal EstimatedAmount, string Currency)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record ClaimAssigned(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, string OfficerId, string OfficerName)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record InformationRequested(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, Guid RequestId, string Question)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record InformationProvided(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string? OfficerId, Guid RequestId)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record ClaimApproved(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, decimal ApprovedAmount, string Currency)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record ClaimRejected(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, string Reason)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

public record ClaimSettled(
    Guid ClaimId, string ClaimNumber, DateTime OccurredAt,
    string ClaimantId, decimal SettledAmount, string Currency)
    : ClaimEventBase(ClaimId, ClaimNumber, OccurredAt);

/// <summary>Stable mapping between event type names (stored in the outbox / Kafka header) and CLR types.</summary>
public static class ClaimEventTypes
{
    private static readonly Dictionary<string, Type> ByName = new[]
    {
        typeof(ClaimSubmitted), typeof(ClaimAssigned), typeof(InformationRequested),
        typeof(InformationProvided), typeof(ClaimApproved), typeof(ClaimRejected), typeof(ClaimSettled)
    }.ToDictionary(t => t.Name);

    public static string NameOf(IClaimEvent evt) => evt.GetType().Name;

    public static Type Resolve(string name) =>
        ByName.TryGetValue(name, out var type) ? type : throw new InvalidOperationException($"Unknown event type '{name}'.");
}
