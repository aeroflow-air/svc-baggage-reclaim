using System.Collections.Immutable;

namespace AeroFlow.BaggageReclaim.Domain;

public static class ReclaimRules
{
    public const int MaxBagsPerFlight = 600;
}

/// <summary>Where a bag is between the aircraft hold and the passenger.</summary>
public enum BagStatus
{
    Unloaded,
    OnBelt,
    Collected,
    Delayed,
    Missing,
}

public sealed record Bag(BagTag Tag, FlightId Flight, BagStatus Status);

/// <summary>A reclaim belt and how many flights it can serve at once.</summary>
public sealed record Belt(BeltId Id, int Capacity);

/// <summary>Lifecycle of one flight's reclaim.</summary>
public abstract record ReclaimStatus
{
    private ReclaimStatus()
    {
    }

    /// <summary>Belt assigned, bags coming off the aircraft.</summary>
    public sealed record Unloading : ReclaimStatus;

    public sealed record Complete(DateTimeOffset At) : ReclaimStatus;

    public sealed record Cancelled(string Reason) : ReclaimStatus;

    public bool IsOpen => this is Unloading;
}

/// <summary>One arriving flight's reclaim: its belt, its bags and where it is in the lifecycle.</summary>
public sealed record FlightReclaim(
    FlightId Flight,
    FlightNumber FlightNumber,
    string Origin,
    BeltId Belt,
    DateTimeOffset LandedAt,
    ImmutableList<Bag> Bags,
    ReclaimStatus Status,
    TimeSpan Delay)
{
    public int BagsOnBelt => Bags.Count(b => b.Status == BagStatus.OnBelt);

    public int BagsCollected => Bags.Count(b => b.Status == BagStatus.Collected);
}

/// <summary>
/// The whole reclaim hall: belts, every flight's reclaim, and which flight events have already been applied.
/// Immutable; every change goes through <see cref="ReclaimPlanner"/> and returns a new plan.
/// </summary>
public sealed record ReclaimHall
{
    /// <summary>How many applied event ids to remember for de-duplication.</summary>
    public const int ProcessedEventMemory = 10_000;

    private ReclaimHall(ImmutableList<Belt> belts)
    {
        Belts = belts;
    }

    public ImmutableList<Belt> Belts { get; }

    public ImmutableDictionary<FlightId, FlightReclaim> Reclaims { get; init; } = ImmutableDictionary<FlightId, FlightReclaim>.Empty;

    /// <summary>Applied event ids, oldest first (bounded by <see cref="ProcessedEventMemory"/>).</summary>
    public ImmutableQueue<Guid> ProcessedOrder { get; init; } = ImmutableQueue<Guid>.Empty;

    public ImmutableHashSet<Guid> Processed { get; init; } = ImmutableHashSet<Guid>.Empty;

    public int NextBagSerial { get; init; } = 1;

    public static Result<ReclaimHall> Create(IEnumerable<Belt> belts)
    {
        var list = belts.ToImmutableList();
        if (list.Count == 0)
        {
            return new NoBelts();
        }

        if (list.FirstOrNone(b => b.Capacity < 1) is Option<Belt>.Some bad)
        {
            return new InvalidBeltCapacity(bad.Value.Capacity);
        }

        var duplicate = list.GroupBy(b => b.Id).FirstOrNone(g => g.Count() > 1);
        return duplicate.Match<Result<ReclaimHall>>(g => new DuplicateBelt(g.Key), () => new ReclaimHall(list));
    }

    public Option<FlightReclaim> ReclaimFor(FlightId flight) => Reclaims.Find(flight);

    public IEnumerable<FlightReclaim> OpenOn(BeltId belt) => Reclaims.Values.Where(r => r.Belt == belt && r.Status.IsOpen);

    public bool HasProcessed(Guid eventId) => Processed.Contains(eventId);

    /// <summary>Remember an applied event id, forgetting the oldest beyond <see cref="ProcessedEventMemory"/>.</summary>
    public ReclaimHall MarkProcessed(Guid eventId)
    {
        if (Processed.Contains(eventId))
        {
            return this;
        }

        var order = ProcessedOrder.Enqueue(eventId);
        var set = Processed.Add(eventId);
        while (set.Count > ProcessedEventMemory)
        {
            order = order.Dequeue(out var oldest);
            set = set.Remove(oldest);
        }

        return this with { ProcessedOrder = order, Processed = set };
    }
}
