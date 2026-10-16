using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>Event types published on the <c>baggage-events</c> topic (owned by this service, ADR-0013).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BaggageEventType>))]
public enum BaggageEventType
{
    BeltAssigned,
    BagsOnBelt,
    LastBagOnBelt,
    ReclaimComplete,
    BagDelayed,
}

/// <summary>
/// Wire contract for a baggage event (JSON body of a Service Bus message). Message <c>Subject</c> carries the
/// event type and <c>CorrelationId</c> the flight id, the same conventions as <c>flight-events</c>.
/// Optional fields are only set for the event types that need them.
/// </summary>
public sealed record BaggageEvent(
    Guid EventId,
    BaggageEventType EventType,
    string FlightId,
    string FlightNumber,
    string Belt,
    DateTimeOffset OccurredAt,
    int? BagCount = null,
    int? BagsCollected = null,
    int? BagsMissing = null,
    string? BagTag = null,
    string? Reason = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static BaggageEvent For(FlightReclaim reclaim, BaggageEventType type, DateTimeOffset at, Guid id) =>
        new(id, type, reclaim.Flight.Value, reclaim.FlightNumber.Value, reclaim.Belt.Value, at);

    /// <summary>Namespace for ids of events produced by unloading ticks, which have no triggering message.</summary>
    public static readonly Guid TickNamespace = Guid.Parse("3b1f6a52-6c0e-4c7e-9a43-0b6f6b2a8d17");

    /// <summary>
    /// Deterministic id for a tick event (ADR-0017): flight, landing time, event type and, for <c>BagDelayed</c>, the
    /// bag tag. Each of these events happens at most once per reclaim, so the id is unique and stable.
    /// </summary>
    public static Guid TickId(FlightReclaim reclaim, BaggageEventType type, string? bagTag = null) =>
        EventIds.Derive(TickNamespace, $"{reclaim.Flight.Value}/{reclaim.LandedAt.UtcTicks}/{type}/{bagTag}");

    /// <summary>Deterministic id for an event caused by a flight event: that event's id plus the baggage event type.</summary>
    public static Guid CausedBy(Guid flightEventId, BaggageEventType type) => EventIds.Derive(flightEventId, type.ToString());
}
