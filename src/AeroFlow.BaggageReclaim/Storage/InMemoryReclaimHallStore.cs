using System.Collections.Immutable;
using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Storage;

/// <summary>
/// Thread-safe in-memory store: one immutable snapshot of the hall and its outbox, swapped with compare-and-swap.
/// Because the hall and the outbox change in one swap, a change can never be kept while its events are dropped
/// (ADR-0017). State is lost on restart, which is fine for v1 (no <c>store</c> capability yet).
/// </summary>
public sealed class InMemoryReclaimHallStore(ReclaimHall seed) : IReclaimHallStore
{
    private State _state = new(seed, []);

    public ReclaimHall Current() => Volatile.Read(ref _state).Hall;

    public Result<ReclaimHall> Update(Func<ReclaimHall, Result<ReclaimHall>> transition) =>
        UpdateWithEvents(hall => transition(hall).Map(next => new HallChange(next, []))).Map(step => step.Hall);

    public Result<HallChange> UpdateWithEvents(Func<ReclaimHall, Result<HallChange>> transition)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var outcome = transition(snapshot.Hall);
            if (outcome is not Result<HallChange>.Ok ok)
            {
                return outcome;
            }

            var next = new State(ok.Value.Hall, snapshot.Outbox.AddRange(ok.Value.Events));
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, snapshot), snapshot))
            {
                return outcome;
            }
        }
    }

    public IReadOnlyList<BaggageEvent> Pending() => Volatile.Read(ref _state).Outbox;

    public bool MarkPublished(BaggageEvent sent)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            if (snapshot.Outbox.IsEmpty || !ReferenceEquals(snapshot.Outbox[0], sent))
            {
                return false;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, snapshot with { Outbox = snapshot.Outbox.RemoveAt(0) }, snapshot), snapshot))
            {
                return true;
            }
        }
    }

    private sealed record State(ReclaimHall Hall, ImmutableList<BaggageEvent> Outbox);
}
