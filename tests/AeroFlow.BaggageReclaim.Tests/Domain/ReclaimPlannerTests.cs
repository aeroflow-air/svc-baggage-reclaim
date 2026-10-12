using AeroFlow.BaggageReclaim.Domain;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Domain;

public sealed class ReclaimPlannerTests
{
    private static readonly DateTimeOffset Landed = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Landing_assigns_the_first_belt_and_creates_unloaded_bags()
    {
        var hall = Ok(ReclaimPlanner.Apply(Hall(), Assign("SIM-1", "BA123", bags: 3)));

        var reclaim = Reclaim(hall, "SIM-1");
        Assert.Equal("1", reclaim.Belt.Value);
        Assert.Equal(["BA000001", "BA000002", "BA000003"], reclaim.Bags.Select(b => b.Tag.Value));
        Assert.All(reclaim.Bags, b => Assert.Equal(BagStatus.Unloaded, b.Status));
        Assert.IsType<ReclaimStatus.Unloading>(reclaim.Status);
    }

    [Fact]
    public void Next_flight_goes_to_the_least_busy_belt()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1"), Assign("SIM-2", "U21"));

        Assert.Equal("2", Reclaim(hall, "SIM-2").Belt.Value);
        Assert.Equal("U2000003", Reclaim(hall, "SIM-2").Bags[0].Tag.Value);
    }

    [Fact]
    public void Full_hall_is_rejected()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1"), Assign("SIM-2", "BA2"));

        Assert.IsType<NoBeltAvailable>(Fail(ReclaimPlanner.Apply(hall, Assign("SIM-3", "BA3"))));
    }

    [Fact]
    public void Cancelling_frees_the_belt_slot()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1"), Assign("SIM-2", "BA2"), new CancelReclaim(Id("SIM-1"), "diverted"));

        var next = Ok(ReclaimPlanner.Apply(hall, Assign("SIM-3", "BA3")));
        Assert.Equal("1", Reclaim(next, "SIM-3").Belt.Value);
    }

    [Fact]
    public void Landing_twice_is_rejected()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1"));

        Assert.IsType<AlreadyAssigned>(Fail(ReclaimPlanner.Apply(hall, Assign("SIM-1", "BA1"))));
    }

    [Fact]
    public void Bags_move_along_and_completion_marks_the_rest_missing()
    {
        var hall = Apply(
            Hall(),
            Assign("SIM-1", "BA1", bags: 2),
            new MoveBag(Tag("BA000001"), BagStatus.OnBelt),
            new MoveBag(Tag("BA000001"), BagStatus.Collected),
            new CompleteReclaim(Id("SIM-1"), Landed.AddMinutes(40)));

        var reclaim = Reclaim(hall, "SIM-1");
        Assert.Equal([BagStatus.Collected, BagStatus.Missing], reclaim.Bags.Select(b => b.Status));
        Assert.IsType<ReclaimStatus.Complete>(reclaim.Status);
    }

    [Fact]
    public void Collected_bag_cannot_go_back_on_the_belt()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1", bags: 1), new MoveBag(Tag("BA000001"), BagStatus.OnBelt), new MoveBag(Tag("BA000001"), BagStatus.Collected));

        Assert.IsType<InvalidBagTransition>(Fail(ReclaimPlanner.Apply(hall, new MoveBag(Tag("BA000001"), BagStatus.OnBelt))));
    }

    [Fact]
    public void Closed_reclaim_cannot_change()
    {
        var hall = Apply(Hall(), Assign("SIM-1", "BA1"), new CancelReclaim(Id("SIM-1"), "x"));

        Assert.IsType<ReclaimAlreadyClosed>(Fail(ReclaimPlanner.Apply(hall, new RecordDelay(Id("SIM-1"), TimeSpan.FromMinutes(5)))));
    }

    [Fact]
    public void Unknown_flight_is_not_found()
    {
        Assert.IsType<ReclaimNotFound>(Fail(ReclaimPlanner.Apply(Hall(), new CancelReclaim(Id("SIM-9"), "x"))));
    }

    [Theory]
    [InlineData("BA123", true)]
    [InlineData("U2123", true)]
    [InlineData("ba1", true)]
    [InlineData("BAW123", false)]
    [InlineData("EZY1234", false)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    public void Flight_numbers_use_two_character_airline_codes(string value, bool valid)
    {
        Assert.Equal(valid, FlightNumber.Parse(value) is Result<FlightNumber>.Ok);
    }

    [Fact]
    public void Hall_rejects_duplicate_belts()
    {
        var belt = new Belt(Ok(BeltId.Parse("1")), 1);

        Assert.IsType<DuplicateBelt>(Fail(ReclaimHall.Create([belt, belt])));
    }

    [Fact]
    public void Processed_events_are_bounded()
    {
        var hall = Hall();
        var first = Guid.NewGuid();
        hall = hall.MarkProcessed(first);
        for (var i = 0; i < ReclaimHall.ProcessedEventMemory; i++)
        {
            hall = hall.MarkProcessed(Guid.NewGuid());
        }

        Assert.False(hall.HasProcessed(first));
        Assert.Equal(ReclaimHall.ProcessedEventMemory, hall.Processed.Count);
    }

    private static ReclaimHall Hall() =>
        Ok(ReclaimHall.Create([new Belt(Ok(BeltId.Parse("1")), 1), new Belt(Ok(BeltId.Parse("2")), 1)]));

    private static AssignBelt Assign(string id, string flight, int bags = 2) =>
        new(Id(id), Ok(FlightNumber.Parse(flight)), "AMS", Landed, bags);

    private static ReclaimHall Apply(ReclaimHall hall, params ReclaimCommand[] commands) =>
        commands.Aggregate(hall, (h, c) => Ok(ReclaimPlanner.Apply(h, c)));

    private static FlightReclaim Reclaim(ReclaimHall hall, string id) =>
        Assert.IsType<Option<FlightReclaim>.Some>(hall.ReclaimFor(Id(id))).Value;

    private static FlightId Id(string id) => Ok(FlightId.Parse(id));

    private static BagTag Tag(string tag) => Ok(BagTag.Parse(tag));

    private static T Ok<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;

    private static Error Fail<T>(Result<T> result) => Assert.IsType<Result<T>.Failure>(result).Error;
}
