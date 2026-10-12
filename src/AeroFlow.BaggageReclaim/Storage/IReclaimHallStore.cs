using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Storage;

/// <summary>Edge adapter for the reclaim hall. The domain never sees this.</summary>
public interface IReclaimHallStore
{
    ReclaimHall Current();

    /// <summary>
    /// Atomically replaces the hall with the outcome of <paramref name="transition"/>.
    /// The transition must be pure: it may be re-run if another writer got there first.
    /// </summary>
    Result<ReclaimHall> Update(Func<ReclaimHall, Result<ReclaimHall>> transition);
}
