using AeroFlow.BaggageReclaim.Domain;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Domain;

public sealed class ReclaimQueriesTests
{
    private static readonly DateTimeOffset Landed = new(2026, 10, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Belts_lists_every_belt_in_hall_order_even_when_empty()
    {
        var belts = ReclaimQueries.Belts(Hall());

        Assert.Equal(["1", "2"], belts.Select(b => b.Belt.Id.Value));
        Assert.All(belts, b => Assert.Empty(b.Reclaims));
    }

    [Fact]
    public void Belts_keeps_open_and_complete_reclaims_but_drops_cancelled()
    {
        var hall = Apply(
            Hall(),
            Assign("SIM-1", "BA1", Landed),
            Assign("SIM-2", "BA2", Landed.AddMinutes(1)),
            Assign("SIM-3", "BA3", Landed.AddMinutes(2)),
            new CompleteReclaim(Id("SIM-1"), Landed.AddMinutes(30)),
            new CancelReclaim(Id("SIM-2"), "diverted"));

        var belts = ReclaimQueries.Belts(hall);

        Assert.Equal(["SIM-1", "SIM-3"], belts.SelectMany(b => b.Reclaims).Select(r => r.Flight.Value).Order());
        Assert.DoesNotContain(belts.SelectMany(b => b.Reclaims), r => r.Flight.Value == "SIM-2");
    }

    [Fact]
    public void Stage_follows_the_bags_out_of_the_hold()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1", Landed, bags: 2));
        Assert.Equal(ReclaimStage.Assigned, ReclaimQueries.Stage(Reclaim(hall)));

        hall = Apply(hall, new MoveBag(Tag("BA000001"), BagStatus.OnBelt));
        Assert.Equal(ReclaimStage.OnBelt, ReclaimQueries.Stage(Reclaim(hall)));

        hall = Apply(hall, new MoveBag(Tag("BA000002"), BagStatus.Delayed));
        Assert.Equal(ReclaimStage.OnBelt, ReclaimQueries.Stage(Reclaim(hall)));

        hall = Apply(hall, new MoveBag(Tag("BA000002"), BagStatus.OnBelt));
        Assert.Equal(ReclaimStage.LastBag, ReclaimQueries.Stage(Reclaim(hall)));

        hall = Apply(hall, new CompleteReclaim(Id("SIM-1"), Landed.AddMinutes(20)));
        Assert.Equal(ReclaimStage.Complete, ReclaimQueries.Stage(Reclaim(hall)));
    }

    [Fact]
    public void A_flight_with_no_bags_is_assigned_until_completed()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1", Landed, bags: 0));

        Assert.Equal(ReclaimStage.Assigned, ReclaimQueries.Stage(Reclaim(hall)));
    }

    private static ReclaimHall Hall() =>
        Ok(ReclaimHall.Create([new Belt(Ok(BeltId.Parse("1")), 2), new Belt(Ok(BeltId.Parse("2")), 2)]));

    private static AssignBelt Assign(string id, string flight, DateTimeOffset landed, int bags = 2) =>
        new(Id(id), Ok(FlightNumber.Parse(flight)), "AMS", landed, bags);

    private static ReclaimHall Apply(ReclaimHall hall, params ReclaimCommand[] commands) =>
        commands.Aggregate(hall, (h, c) => Ok(ReclaimPlanner.Apply(h, c)));

    private static FlightReclaim Reclaim(ReclaimHall hall) =>
        Assert.IsType<Option<FlightReclaim>.Some>(hall.ReclaimFor(Id("SIM-1"))).Value;

    private static FlightId Id(string id) => Ok(FlightId.Parse(id));

    private static BagTag Tag(string tag) => Ok(BagTag.Parse(tag));

    private static T Ok<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;
}
