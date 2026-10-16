using System.Collections.Immutable;
using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>What happened to a flight event. <see cref="Rejected"/> carries the domain error.</summary>
public abstract record HandleOutcome
{
    private HandleOutcome()
    {
    }

    public sealed record Applied(string Command) : HandleOutcome;

    /// <summary>Nothing to do, for example a departure, a boarding update or an event seen before.</summary>
    public sealed record Ignored(string Reason) : HandleOutcome;

    public sealed record Rejected(Error Error) : HandleOutcome;
}

/// <summary>
/// Turns simulator flight events for flights <b>arriving</b> at the home airport into reclaim commands:
/// <c>Landed</c> assigns a belt and creates the flight's bags, <c>Delayed</c> records the delay on an open reclaim,
/// <c>Cancelled</c> closes it and frees the belt slot. Everything else is ignored.
/// Idempotent: an applied <see cref="FlightEventMessage.EventId"/> is remembered in the hall, in the same atomic
/// update as the change, so a redelivered message is ignored rather than applied twice.
/// </summary>
public sealed class ReclaimEventHandler(IReclaimHallStore store, IOptions<FlightEventsOptions> options)
{
    private readonly FlightEventsOptions _options = options.Value;

    public HandleOutcome Handle(FlightEventMessage evt)
    {
        if (!string.Equals(evt.Destination, _options.HomeAirport, StringComparison.OrdinalIgnoreCase))
        {
            return new HandleOutcome.Ignored($"not arriving at {_options.HomeAirport}");
        }

        if (evt.EventType is not (FlightEventType.Landed or FlightEventType.Delayed or FlightEventType.Cancelled))
        {
            return new HandleOutcome.Ignored($"{evt.EventType} does not affect reclaim");
        }

        if (store.Current().HasProcessed(evt.EventId))
        {
            return new HandleOutcome.Ignored($"event {evt.EventId} already applied");
        }

        var parsed = FlightId.Parse(evt.FlightId)
            .Bind(id => FlightNumber.Parse(evt.FlightNumber).Map(number => (Id: id, Number: number)));
        if (parsed is Result<(FlightId Id, FlightNumber Number)>.Failure failure)
        {
            return new HandleOutcome.Rejected(failure.Error);
        }

        var (flightId, flightNumber) = ((Result<(FlightId Id, FlightNumber Number)>.Ok)parsed).Value;

        // Delays and cancellations normally arrive before landing, when there is no belt yet: nothing to change.
        if (evt.EventType != FlightEventType.Landed && store.Current().ReclaimFor(flightId) is Option<FlightReclaim>.None)
        {
            return new HandleOutcome.Ignored($"{evt.EventType} before landing; no belt assigned yet");
        }

        ReclaimCommand command = evt.EventType switch
        {
            FlightEventType.Landed => new AssignBelt(flightId, flightNumber, evt.Origin, evt.OccurredAt, BagCount(flightId)),
            FlightEventType.Delayed => new RecordDelay(flightId, TimeSpan.FromMinutes(evt.DelayMinutes ?? 0)),
            _ => new CancelReclaim(flightId, evt.Reason ?? "cancelled"),
        };

        // The change, the processed id and (for Landed) BeltAssigned are saved in one atomic update: the event sits
        // in the outbox until OutboxDispatcher sends it, so a failed send is retried rather than lost (ADR-0017).
        return store.UpdateWithEvents(hall => hall.HasProcessed(evt.EventId)
                ? new HallChange(hall, [])
                : ReclaimPlanner.Apply(hall, command).Map(next => new HallChange(
                    next.MarkProcessed(evt.EventId),
                    evt.EventType == FlightEventType.Landed ? BeltAssigned(next, flightId, evt) : [])))
            .Match<HandleOutcome>(
                _ => new HandleOutcome.Applied(command.Name),
                error => new HandleOutcome.Rejected(error));
    }

    /// <summary>
    /// The <c>BeltAssigned</c> event for an applied <c>Landed</c>. Its id is derived from the flight event's id
    /// (not equal to it, so it never collides with the <c>Landed</c> event in a consumer's seen-ids set).
    /// </summary>
    private static ImmutableList<BaggageEvent> BeltAssigned(ReclaimHall hall, FlightId flight, FlightEventMessage evt) =>
        hall.ReclaimFor(flight).Match(
            r => ImmutableList.Create(BaggageEvent.For(r, BaggageEventType.BeltAssigned, evt.OccurredAt, BaggageEvent.CausedBy(evt.EventId, BaggageEventType.BeltAssigned))),
            () => ImmutableList<BaggageEvent>.Empty);

    /// <summary>Stable per flight (FNV-1a, not string.GetHashCode, which changes per process).</summary>
    private int BagCount(FlightId flight)
    {
        var min = Math.Max(0, _options.MinBagsPerFlight);
        var max = Math.Clamp(_options.MaxBagsPerFlight, min, ReclaimRules.MaxBagsPerFlight);
        var hash = 2166136261u;
        foreach (var c in flight.Value)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return min + (int)(hash % (uint)(max - min + 1));
    }
}
