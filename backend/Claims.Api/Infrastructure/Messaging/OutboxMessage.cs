using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Domain;

namespace Claims.Api.Infrastructure.Messaging;

/// <summary>
/// Transactional outbox row. Written in the same SaveChanges as the claim change, so an event is
/// never published for a change that was rolled back, and never lost for a change that committed.
/// </summary>
public class OutboxMessage
{
    private OutboxMessage() { } // EF Core

    public Guid Id { get; private set; }          // == event id
    public string Type { get; private set; } = "";
    public string Payload { get; private set; } = "";
    public Guid ClaimId { get; private set; }     // partition key for ordering
    public DateTime OccurredAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage From(IClaimEvent evt) => new()
    {
        Id = evt.EventId,
        Type = ClaimEventTypes.NameOf(evt),
        Payload = EventSerializer.Serialize(evt),
        ClaimId = evt.ClaimId,
        OccurredAt = evt.OccurredAt
    };

    public void MarkProcessed(DateTime now)
    {
        ProcessedAt = now;
        Attempts++;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

/// <summary>Records which consumer has handled which event, making handlers idempotent under at-least-once delivery.</summary>
public class ProcessedEvent
{
    private ProcessedEvent() { } // EF Core

    public ProcessedEvent(Guid eventId, string consumer, DateTime processedAt)
    {
        EventId = eventId;
        Consumer = consumer;
        ProcessedAt = processedAt;
    }

    public Guid EventId { get; private set; }
    public string Consumer { get; private set; } = "";
    public DateTime ProcessedAt { get; private set; }
}

public static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IClaimEvent evt) => JsonSerializer.Serialize(evt, evt.GetType(), Options);

    public static IClaimEvent Deserialize(string type, string payload) =>
        (IClaimEvent)(JsonSerializer.Deserialize(payload, ClaimEventTypes.Resolve(type), Options)
            ?? throw new InvalidOperationException($"Could not deserialize event of type '{type}'."));
}
