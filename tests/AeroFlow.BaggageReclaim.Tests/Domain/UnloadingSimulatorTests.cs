using AeroFlow.BaggageReclaim.Domain;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Domain;

public sealed class UnloadingSimulatorTests
{
    private static readonly DateTimeOffset Landed = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly UnloadingSettings Settings = new(TimeSpan.FromSeconds(10), BagsPerTick: 2, CollectedPerTick: 2, DelayedBagProbability: 0);

    [Fact]
    public void Nothing_happens_before_the_first_bag_time()
    {
        var step = Tick(Hall(bags: 3), Landed.AddSeconds(5));

        Assert.Empty(step.Events);
        Assert.All(Reclaim(step.Hall).Bags, b => Assert.Equal(BagStatus.Unloaded, b.Status));
    }

    [Fact]
    public void Full_lifecycle_emits_events_in_order()
    {
        var hall = Hall(bags: 3);
        var types = new List<BaggageEventType>();
        for (var t = 10; t <= 30; t += 2)
        {
            var step = Tick(hall, Landed.AddSeconds(t));
            hall = step.Hall;
            types.AddRange(step.Events.Select(e => e.EventType));
        }

        Assert.Equal([BaggageEventType.BagsOnBelt, BaggageEventType.LastBagOnBelt, BaggageEventType.ReclaimComplete], types);
        var reclaim = Reclaim(hall);
        Assert.IsType<ReclaimStatus.Complete>(reclaim.Status);
        Assert.Equal(3, reclaim.BagsCollected);
    }

    [Fact]
    public void Bags_on_belt_carries_the_flight_and_belt()
    {
        var evt = Assert.Single(Tick(Hall(bags: 3), Landed.AddSeconds(10)).Events);

        Assert.Equal(BaggageEventType.BagsOnBelt, evt.EventType);
        Assert.Equal(("SIM-1", "BA123", "1", 3), (evt.FlightId, evt.FlightNumber, evt.Belt, evt.BagCount));
    }

    [Fact]
    public void Reclaim_complete_reports_counts()
    {
        var hall = Hall(bags: 2);
        hall = Tick(hall, Landed.AddSeconds(10)).Hall;
        var done = Assert.Single(Tick(hall, Landed.AddSeconds(12)).Events);

        Assert.Equal(BaggageEventType.ReclaimComplete, done.EventType);
        Assert.Equal((2, 2, 0), (done.BagCount!.Value, done.BagsCollected!.Value, done.BagsMissing!.Value));
    }

    [Fact]
    public void Delayed_bags_are_reported_and_end_up_missing()
    {
        var settings = Settings with { DelayedBagProbability = 1 };
        var step = UnloadingSimulator.Tick(Hall(bags: 2), Landed.AddSeconds(10), settings, () => 0.0, Guid.NewGuid);

        Assert.Equal(
            [BaggageEventType.BagDelayed, BaggageEventType.BagDelayed, BaggageEventType.ReclaimComplete],
            step.Events.Select(e => e.EventType));
        Assert.Equal("BA000001", step.Events[0].BagTag);
        Assert.Equal(2, step.Events[^1].BagsMissing);
    }

    [Fact]
    public void Landing_delay_pushes_the_first_bag_back()
    {
        var hall = Ok(ReclaimPlanner.Apply(Hall(bags: 1), new RecordDelay(Id(), TimeSpan.FromMinutes(5))));

        Assert.Empty(Tick(hall, Landed.AddSeconds(10)).Events);
    }

    [Fact]
    public void Closed_reclaims_are_left_alone()
    {
        var hall = Ok(ReclaimPlanner.Apply(Hall(bags: 2), new CancelReclaim(Id(), "x")));

        Assert.Empty(Tick(hall, Landed.AddMinutes(1)).Events);
    }

    private static UnloadingStep Tick(ReclaimHall hall, DateTimeOffset now) =>
        UnloadingSimulator.Tick(hall, now, Settings, () => 0.5, Guid.NewGuid);

    private static ReclaimHall Hall(int bags)
    {
        var hall = Ok(ReclaimHall.Create([new Belt(Ok(BeltId.Parse("1")), 2)]));
        return Ok(ReclaimPlanner.Apply(hall, new AssignBelt(Id(), Ok(FlightNumber.Parse("BA123")), "AMS", Landed, bags)));
    }

    private static FlightReclaim Reclaim(ReclaimHall hall) => hall.Reclaims[Id()];

    private static FlightId Id() => Ok(FlightId.Parse("SIM-1"));

    private static T Ok<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;
}
