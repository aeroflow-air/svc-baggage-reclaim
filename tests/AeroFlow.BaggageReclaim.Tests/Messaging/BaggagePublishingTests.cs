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
    public void Landed_queues_belt_assigned_with_an_id_derived_from_the_flight_event()
    {
        var landed = Landed();
        Handler().Handle(landed);

        var evt = Assert.Single(_store.Pending());

        Assert.Equal((BaggageEventType.BeltAssigned, "SIM-00001", "BA123", "1"), (evt.EventType, evt.FlightId, evt.FlightNumber, evt.Belt));
        Assert.Equal(BaggageEvent.CausedBy(landed.EventId, BaggageEventType.BeltAssigned), evt.EventId);
        Assert.NotEqual(landed.EventId, evt.EventId); // never collides with the Landed event in a consumer's seen set
    }

    [Fact]
    public void Redelivered_landed_queues_nothing_more()
    {
        var landed = Landed();
        var handler = Handler();
        handler.Handle(landed);

        Assert.IsType<HandleOutcome.Ignored>(handler.Handle(landed));

        Assert.Single(_store.Pending());
    }

    [Fact]
    public void Other_events_queue_nothing()
    {
        var handler = Handler();
        handler.Handle(Landed() with { EventType = FlightEventType.Delayed });

        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Failed_belt_assigned_send_is_retried_not_lost()
    {
        Handler().Handle(Landed());
        _publisher.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Dispatcher().DrainAsync(CancellationToken.None));
        Assert.Single(_store.Pending());

        await Dispatcher().DrainAsync(CancellationToken.None);

        Assert.Equal(BaggageEventType.BeltAssigned, Assert.Single(_publisher.Published).EventType);
        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Worker_runs_a_landed_flight_through_to_reclaim_complete()
    {
        Handler(new FlightEventsOptions { MinBagsPerFlight = 5, MaxBagsPerFlight = 5 }).Handle(Landed());
        var worker = Worker(new BaggageOptions { FirstBagAfterLandingSeconds = 10, BagsPerTick = 2, CollectedPerTick = 2, DelayedBagProbability = 0, Seed = 1 });

        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            worker.Tick();
        }

        await Dispatcher().DrainAsync(CancellationToken.None);

        Assert.Equal(
            [BaggageEventType.BeltAssigned, BaggageEventType.BagsOnBelt, BaggageEventType.LastBagOnBelt, BaggageEventType.ReclaimComplete],
            _publisher.Published.Select(e => e.EventType));
        Assert.IsType<ReclaimStatus.Complete>(_store.Current().Reclaims.Values.Single().Status);
    }

    [Fact]
    public async Task Tick_events_survive_a_failed_publish_and_keep_their_order()
    {
        Handler(new FlightEventsOptions { MinBagsPerFlight = 2, MaxBagsPerFlight = 2 }).Handle(Landed());
        var worker = Worker(new BaggageOptions { FirstBagAfterLandingSeconds = 0, BagsPerTick = 2, CollectedPerTick = 2, DelayedBagProbability = 0, Seed = 1 });
        await Dispatcher().DrainAsync(CancellationToken.None);
        _publisher.FailNext = true;

        worker.Tick(); // the hall moves on: bags on belt, last bag on belt
        await Assert.ThrowsAsync<InvalidOperationException>(() => Dispatcher().DrainAsync(CancellationToken.None));
        _clock.Advance(TimeSpan.FromSeconds(5));
        worker.Tick(); // collected: reclaim complete
        await Dispatcher().DrainAsync(CancellationToken.None);

        Assert.Equal(
            [BaggageEventType.BeltAssigned, BaggageEventType.BagsOnBelt, BaggageEventType.LastBagOnBelt, BaggageEventType.ReclaimComplete],
            _publisher.Published.Select(e => e.EventType));
        Assert.Empty(_store.Pending());
    }

    [Fact]
    public void Worker_queues_nothing_when_idle()
    {
        Assert.Empty(Worker(new BaggageOptions()).Tick());

        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Running_dispatcher_survives_a_publisher_timeout_and_retries_after_the_interval()
    {
        Handler().Handle(Landed());
        _publisher.ThrowCancelled = true;
        var dispatcher = new OutboxDispatcher(_store, _publisher, Options.Create(new OutboxOptions { RetrySeconds = 5 }), _clock, NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntil(() => _publisher.Attempts == 1);
        dispatcher.Notify(); // ignored during the back-off
        await Task.Delay(50);
        Assert.Equal(1, _publisher.Attempts);

        await AdvanceUntil(() => _store.Pending().Count == 0);
        Assert.Equal(BaggageEventType.BeltAssigned, Assert.Single(_publisher.Published).EventType);

        await dispatcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Running_dispatcher_sends_straight_away_when_notified()
    {
        var dispatcher = Dispatcher();
        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntil(() => _publisher.Attempts == 0); // started, outbox empty

        Handler().Handle(Landed());
        dispatcher.Notify();

        await WaitUntil(() => _publisher.Published.Count == 1); // clock frozen: only the wake-up can do this
        await dispatcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Rejected_landed_queues_no_belt_assigned()
    {
        var handler = Handler();
        for (var i = 0; i < 14; i++)
        {
            handler.Handle(Landed() with { FlightId = $"SIM-{i:D5}", FlightNumber = $"BA{100 + i}" });
        }

        var pending = _store.Pending().Count;

        Assert.IsType<HandleOutcome.Rejected>(handler.Handle(Landed() with { FlightId = "SIM-99999", FlightNumber = "BA999" }));
        Assert.Equal(pending, _store.Pending().Count);
    }

    [Fact]
    public void Derive_matches_the_rfc_uuid_v5_test_vector()
    {
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), EventIds.Derive(dns, "www.example.com"));
    }

    private async Task AdvanceUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
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

    private ReclaimEventHandler Handler(FlightEventsOptions? options = null) =>
        new(_store, Options.Create(options ?? new FlightEventsOptions()));

    private OutboxDispatcher Dispatcher() =>
        new(_store, _publisher, Options.Create(new OutboxOptions()), _clock, NullLogger<OutboxDispatcher>.Instance);

    private UnloadingWorker Worker(BaggageOptions options) =>
        new(_store, Dispatcher(), Options.Create(options), _clock, NullLogger<UnloadingWorker>.Instance);

    private FlightEventMessage Landed() =>
        new(Guid.NewGuid(), "SIM-00001", "BA123", FlightEventType.Landed, "AMS", "LHR", Now.AddHours(-1), Now);

    private sealed class CapturingPublisher : IBaggageEventPublisher
    {
        public bool FailNext { get; set; }

        public bool ThrowCancelled { get; set; }

        public int Attempts { get; private set; }

        public List<BaggageEvent> Published { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<BaggageEvent> events, CancellationToken cancellationToken)
        {
            Attempts++;
            if (ThrowCancelled)
            {
                ThrowCancelled = false;
                throw new TaskCanceledException("send timed out");
            }

            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("bus down");
            }

            Published.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
