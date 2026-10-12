using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Messaging;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Messaging;

public sealed class ReclaimEventHandlerTests
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);

    private readonly InMemoryReclaimHallStore _store = new(DefaultReclaimHall.Create());
    private readonly ReclaimEventHandler _handler;

    public ReclaimEventHandlerTests()
    {
        _handler = new ReclaimEventHandler(_store, Options.Create(new FlightEventsOptions()));
    }

    [Fact]
    public void Landed_assigns_a_belt_with_bags()
    {
        var outcome = _handler.Handle(Event(FlightEventType.Landed));

        Assert.Equal(new HandleOutcome.Applied(nameof(AssignBelt)), outcome);
        var reclaim = Reclaim("SIM-00001");
        Assert.Equal("1", reclaim.Belt.Value);
        Assert.InRange(reclaim.Bags.Count, 60, 180);
        Assert.Equal("AMS", reclaim.Origin);
    }

    [Fact]
    public void Same_event_redelivered_is_ignored()
    {
        var evt = Event(FlightEventType.Landed);
        _handler.Handle(evt);
        var before = _store.Current();

        Assert.IsType<HandleOutcome.Ignored>(_handler.Handle(evt));
        Assert.Same(before, _store.Current());
    }

    [Fact]
    public void Bag_count_is_stable_per_flight()
    {
        var other = new InMemoryReclaimHallStore(DefaultReclaimHall.Create());
        new ReclaimEventHandler(other, Options.Create(new FlightEventsOptions())).Handle(Event(FlightEventType.Landed));
        _handler.Handle(Event(FlightEventType.Landed));

        Assert.Equal(
            Assert.IsType<Option<FlightReclaim>.Some>(other.Current().ReclaimFor(Id("SIM-00001"))).Value.Bags.Count,
            Reclaim("SIM-00001").Bags.Count);
    }

    [Fact]
    public void Delay_after_landing_is_recorded()
    {
        _handler.Handle(Event(FlightEventType.Landed));

        var outcome = _handler.Handle(Event(FlightEventType.Delayed) with { DelayMinutes = 25 });

        Assert.Equal(new HandleOutcome.Applied(nameof(RecordDelay)), outcome);
        Assert.Equal(TimeSpan.FromMinutes(25), Reclaim("SIM-00001").Delay);
    }

    [Theory]
    [InlineData(FlightEventType.Delayed)]
    [InlineData(FlightEventType.Cancelled)]
    public void Delay_or_cancel_before_landing_is_ignored(FlightEventType type)
    {
        Assert.IsType<HandleOutcome.Ignored>(_handler.Handle(Event(type)));
        Assert.Empty(_store.Current().Reclaims);
    }

    [Fact]
    public void Cancelled_after_assignment_closes_the_reclaim()
    {
        _handler.Handle(Event(FlightEventType.Landed));

        var outcome = _handler.Handle(Event(FlightEventType.Cancelled) with { Reason = "diverted" });

        Assert.Equal(new HandleOutcome.Applied(nameof(CancelReclaim)), outcome);
        Assert.Equal(new ReclaimStatus.Cancelled("diverted"), Reclaim("SIM-00001").Status);
    }

    [Theory]
    [InlineData(FlightEventType.Scheduled)]
    [InlineData(FlightEventType.Boarding)]
    [InlineData(FlightEventType.Departed)]
    public void Other_lifecycle_events_are_ignored(FlightEventType type)
    {
        Assert.IsType<HandleOutcome.Ignored>(_handler.Handle(Event(type)));
    }

    [Fact]
    public void Departures_from_home_are_ignored()
    {
        var outcome = _handler.Handle(Event(FlightEventType.Landed) with { Origin = "LHR", Destination = "AMS" });

        Assert.IsType<HandleOutcome.Ignored>(outcome);
        Assert.Empty(_store.Current().Reclaims);
    }

    [Fact]
    public void Second_landing_with_a_new_event_id_is_rejected()
    {
        _handler.Handle(Event(FlightEventType.Landed));

        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Landed)));

        Assert.IsType<AlreadyAssigned>(outcome.Error);
    }

    [Fact]
    public void Invalid_flight_number_is_rejected()
    {
        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Landed, "EZY1234")));

        Assert.IsType<InvalidFlightNumber>(outcome.Error);
    }

    [Fact]
    public void Simulator_json_deserialises()
    {
        const string json = """
            {"eventId":"8c6f2f1e-6f7e-4a52-9a43-5b9e0a3c2d11","flightId":"SIM-00001","flightNumber":"U2123",
             "eventType":"Landed","origin":"AMS","destination":"LHR","scheduledDeparture":"2026-10-12T16:00:00+00:00",
             "occurredAt":"2026-10-12T17:10:00+00:00"}
            """;

        var evt = System.Text.Json.JsonSerializer.Deserialize<FlightEventMessage>(json, FlightEventMessage.JsonOptions);

        Assert.NotNull(evt);
        Assert.Equal(new HandleOutcome.Applied(nameof(AssignBelt)), _handler.Handle(evt));
        Assert.StartsWith("U2", Reclaim("SIM-00001").Bags[0].Tag.Value);
    }

    private FlightReclaim Reclaim(string id) =>
        Assert.IsType<Option<FlightReclaim>.Some>(_store.Current().ReclaimFor(Id(id))).Value;

    private static FlightId Id(string id) => FlightId.Parse(id).Match(f => f, e => throw new InvalidOperationException());

    private static FlightEventMessage Event(FlightEventType type, string flight = "BA123") =>
        new(Guid.NewGuid(), "SIM-00001", flight, type, "AMS", "LHR", Departure, Departure.AddHours(1));
}
