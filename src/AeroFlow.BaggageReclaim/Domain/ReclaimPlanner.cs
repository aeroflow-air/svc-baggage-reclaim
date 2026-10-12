using System.Collections.Immutable;
using System.Diagnostics;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>Pure rules for the reclaim hall. No I/O, no clock: everything it needs is in the command.</summary>
public static class ReclaimPlanner
{
    public static Result<ReclaimHall> Apply(ReclaimHall hall, ReclaimCommand command) => command switch
    {
        AssignBelt c => Assign(hall, c),
        RecordDelay c => WithOpenReclaim(hall, c.Flight, r => r with { Delay = c.Delay }),
        CancelReclaim c => WithOpenReclaim(hall, c.Flight, r => r with { Status = new ReclaimStatus.Cancelled(c.Reason) }),
        MoveBag c => Move(hall, c),
        CompleteReclaim c => WithOpenReclaim(hall, c.Flight, r => r with
        {
            Status = new ReclaimStatus.Complete(c.At),
            Bags = r.Bags.Select(b => b.Status == BagStatus.Collected ? b : b with { Status = BagStatus.Missing }).ToImmutableList(),
        }),
        _ => throw new UnreachableException($"Unknown command {command.GetType().Name}"),
    };

    /// <summary>Allowed bag moves. Delayed bags can still turn up; missing and collected are final.</summary>
    public static bool CanMove(BagStatus from, BagStatus to) => (from, to) switch
    {
        (BagStatus.Unloaded, BagStatus.OnBelt or BagStatus.Delayed or BagStatus.Missing) => true,
        (BagStatus.OnBelt, BagStatus.Collected or BagStatus.Missing) => true,
        (BagStatus.Delayed, BagStatus.OnBelt or BagStatus.Missing) => true,
        _ => false,
    };

    private static Result<ReclaimHall> Assign(ReclaimHall hall, AssignBelt c)
    {
        if (c.BagCount is < 0 or > ReclaimRules.MaxBagsPerFlight)
        {
            return new InvalidBagCount(c.BagCount);
        }

        if (hall.ReclaimFor(c.Flight) is Option<FlightReclaim>.Some existing)
        {
            return new AlreadyAssigned(c.Flight, existing.Value.Belt);
        }

        // Least busy belt with room; ties go to the first belt listed so the choice is predictable.
        var belt = hall.Belts
            .Select((b, index) => (Belt: b, Index: index, Load: hall.OpenOn(b.Id).Count()))
            .Where(x => x.Load < x.Belt.Capacity)
            .OrderBy(x => x.Load)
            .ThenBy(x => x.Index)
            .Select(x => x.Belt)
            .FirstOrNone();

        return belt.ToResult(() => new NoBeltAvailable(c.FlightNumber)).Map(b =>
        {
            var bags = Enumerable.Range(0, c.BagCount)
                .Select(i => new Bag(BagTag.For(c.FlightNumber, hall.NextBagSerial + i), c.Flight, BagStatus.Unloaded))
                .ToImmutableList();
            var reclaim = new FlightReclaim(c.Flight, c.FlightNumber, c.Origin, b.Id, c.LandedAt, bags, new ReclaimStatus.Unloading(), TimeSpan.Zero);
            return hall with
            {
                Reclaims = hall.Reclaims.SetItem(c.Flight, reclaim),
                NextBagSerial = hall.NextBagSerial + c.BagCount,
            };
        });
    }

    private static Result<ReclaimHall> Move(ReclaimHall hall, MoveBag c)
    {
        var found = hall.Reclaims.Values
            .SelectMany(r => r.Bags.Select(b => (Reclaim: r, Bag: b)))
            .FirstOrNone(x => x.Bag.Tag == c.Bag);

        return found.ToResult(() => new BagNotFound(c.Bag)).Bind(x =>
        {
            if (!x.Reclaim.Status.IsOpen)
            {
                return new ReclaimAlreadyClosed(x.Reclaim.Flight);
            }

            if (!CanMove(x.Bag.Status, c.To))
            {
                return new InvalidBagTransition(c.Bag, x.Bag.Status, c.To);
            }

            var reclaim = x.Reclaim with { Bags = x.Reclaim.Bags.Replace(x.Bag, x.Bag with { Status = c.To }) };
            return (Result<ReclaimHall>)(hall with { Reclaims = hall.Reclaims.SetItem(reclaim.Flight, reclaim) });
        });
    }

    private static Result<ReclaimHall> WithOpenReclaim(ReclaimHall hall, FlightId flight, Func<FlightReclaim, FlightReclaim> change) =>
        hall.ReclaimFor(flight)
            .ToResult(() => new ReclaimNotFound(flight))
            .Bind(r => r.Status.IsOpen
                ? (Result<ReclaimHall>)(hall with { Reclaims = hall.Reclaims.SetItem(flight, change(r)) })
                : new ReclaimAlreadyClosed(flight));
}
