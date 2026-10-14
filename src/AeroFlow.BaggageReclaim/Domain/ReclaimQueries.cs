using System.Collections.Immutable;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>Where a flight's reclaim is, as a reader of the belt would see it.</summary>
public enum ReclaimStage
{
    /// <summary>Belt assigned, no bags out of the hold yet.</summary>
    Assigned,

    /// <summary>Bags are coming onto the belt.</summary>
    OnBelt,

    /// <summary>Nothing left in the hold; bags still on the belt.</summary>
    LastBag,

    /// <summary>Belt clear for this flight.</summary>
    Complete,
}

/// <summary>A belt and the reclaims currently on it (open or complete, never cancelled), earliest landing first.</summary>
public sealed record BeltView(Belt Belt, ImmutableList<FlightReclaim> Reclaims);

/// <summary>Pure read-side projections over the reclaim hall.</summary>
public static class ReclaimQueries
{
    /// <summary>Every belt in hall order with its non-cancelled reclaims, earliest landing first.</summary>
    public static ImmutableList<BeltView> Belts(ReclaimHall hall) =>
        hall.Belts
            .Select(belt => new BeltView(
                belt,
                hall.Reclaims.Values
                    .Where(r => r.Belt == belt.Id && r.Status is not ReclaimStatus.Cancelled)
                    .OrderBy(r => r.LandedAt)
                    .ThenBy(r => r.FlightNumber.Value, StringComparer.Ordinal)
                    .ToImmutableList()))
            .ToImmutableList();

    /// <summary>
    /// Stage of a reclaim, derived from its bags: complete once closed, last bag once nothing is unloaded or delayed
    /// (and at least one bag came out), on belt once any bag has left the hold, otherwise assigned.
    /// </summary>
    public static ReclaimStage Stage(FlightReclaim reclaim) => reclaim switch
    {
        { Status: ReclaimStatus.Complete } => ReclaimStage.Complete,
        _ when !reclaim.Bags.Any(b => b.Status is not BagStatus.Unloaded) => ReclaimStage.Assigned,
        _ when !reclaim.Bags.Any(b => b.Status is BagStatus.Unloaded or BagStatus.Delayed) => ReclaimStage.LastBag,
        _ => ReclaimStage.OnBelt,
    };
}
