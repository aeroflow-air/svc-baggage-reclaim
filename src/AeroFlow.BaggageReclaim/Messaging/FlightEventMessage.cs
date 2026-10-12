using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Lifecycle stages published on the <c>flight-events</c> topic by the aeroflow-local flight simulator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlightEventType>))]
public enum FlightEventType
{
    Scheduled,
    Boarding,
    Departed,
    Landed,
    Delayed,
    Cancelled,
}

/// <summary>
/// Local copy of the wire contract in aeroflow-local (<c>AeroFlow.Local.FlightEvents.FlightEvent</c>):
/// the JSON body of a Service Bus message. Keep the two in step until the contract moves to a shared package.
/// </summary>
public sealed record FlightEventMessage(
    Guid EventId,
    string FlightId,
    string FlightNumber,
    FlightEventType EventType,
    string Origin,
    string Destination,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset OccurredAt,
    int? DelayMinutes = null,
    string? Reason = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
