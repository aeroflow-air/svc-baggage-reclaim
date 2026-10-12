using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Messaging;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Messaging;

public sealed class BaggagePublishingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryReclaimHallStore _store = new(DefaultReclaimHall.Create());
    private readonly CapturingPublisher _publisher = new();
    private readonly FakeTimeProvider _clock = new(Now);

    [Fact]
    public void Belt_assigned_uses_the_landed_event_id()
    {
        var handler = new ReclaimEventHandler(_store, Options.Create(new FlightEventsOptions()));
        var landed = Landed();
        handler.Handle(landed);

        var evt = Assert.IsType<Option<BaggageEvent>.Some>(handler.BeltAssignedFor(landed)).Value;

        Assert.Equal((landed.EventId, BaggageEventType.BeltAssigned, "SIM-00001", "BA123", "1"), (evt.EventId, evt.EventType, evt.FlightId, evt.FlightNumber, evt.Belt));
    }

    [Fact]
    public void No_belt_assigned_for_other_events()
    {
        var handler = new ReclaimEventHandler(_store, Options.Create(new FlightEventsOptions()));

        Assert.IsType<Option<BaggageEvent>.None>(handler.BeltAssignedFor(Landed() with { EventType = FlightEventType.Delayed }));
    }

    [Fact]
    public async Task Worker_runs_a_landed_flight_through_to_reclaim_complete()
    {
        new ReclaimEventHandler(_store, Options.Create(new FlightEventsOptions { MinBagsPerFlight = 5, MaxBagsPerFlight = 5 })).Handle(Landed());
        var worker = Worker(new BaggageOptions { FirstBagAfterLandingSeconds = 10, BagsPerTick = 2, CollectedPerTick = 2, DelayedBagProbability = 0, Seed = 1 });

        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            await worker.TickAsync(CancellationToken.None);
        }

        Assert.Equal(
            [BaggageEventType.BagsOnBelt, BaggageEventType.LastBagOnBelt, BaggageEventType.ReclaimComplete],
            _publisher.Published.Select(e => e.EventType));
        Assert.IsType<ReclaimStatus.Complete>(_store.Current().Reclaims.Values.Single().Status);
    }

    [Fact]
    public async Task Worker_publishes_nothing_when_idle()
    {
        await Worker(new BaggageOptions()).TickAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void Service_bus_message_has_subject_and_correlation()
    {
        var evt = new BaggageEvent(Guid.NewGuid(), BaggageEventType.BagsOnBelt, "SIM-00001", "BA123", "3", Now, BagCount: 120);

        var message = ServiceBusBaggageEventPublisher.ToMessage(evt);

        Assert.Equal(("BagsOnBelt", "SIM-00001", evt.EventId.ToString(), "application/json"), (message.Subject, message.CorrelationId, message.MessageId, message.ContentType));
        var json = message.Body.ToString();
        Assert.Contains("\"eventType\":\"BagsOnBelt\"", json, StringComparison.Ordinal);
        Assert.Contains("\"bagCount\":120", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bagTag", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_are_clamped_to_sane_values()
    {
        var settings = new BaggageOptions { BagsPerTick = 0, CollectedPerTick = -3, DelayedBagProbability = 5, FirstBagAfterLandingSeconds = -1 }.ToSettings();

        Assert.Equal((1, 1, 1.0, TimeSpan.Zero), (settings.BagsPerTick, settings.CollectedPerTick, settings.DelayedBagProbability, settings.FirstBagAfterLanding));
    }

    private UnloadingWorker Worker(BaggageOptions options) =>
        new(_store, _publisher, Options.Create(options), _clock, NullLogger<UnloadingWorker>.Instance);

    private FlightEventMessage Landed() =>
        new(Guid.NewGuid(), "SIM-00001", "BA123", FlightEventType.Landed, "AMS", "LHR", Now.AddHours(-1), Now);

    private sealed class CapturingPublisher : IBaggageEventPublisher
    {
        public List<BaggageEvent> Published { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<BaggageEvent> events, CancellationToken cancellationToken)
        {
            Published.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
