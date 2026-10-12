using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Storage;

/// <summary>
/// Thread-safe in-memory store: one immutable <see cref="ReclaimHall"/> swapped with compare-and-swap.
/// State is lost on restart, which is fine for v1 (no <c>store</c> capability yet).
/// </summary>
public sealed class InMemoryReclaimHallStore(ReclaimHall seed) : IReclaimHallStore
{
    private ReclaimHall _hall = seed;

    public ReclaimHall Current() => Volatile.Read(ref _hall);

    public Result<ReclaimHall> Update(Func<ReclaimHall, Result<ReclaimHall>> transition)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _hall);
            var outcome = transition(snapshot);
            if (outcome is not Result<ReclaimHall>.Ok ok)
            {
                return outcome;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _hall, ok.Value, snapshot), snapshot))
            {
                return outcome;
            }
        }
    }
}
