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
        UpdateWithEvents(hall => transition(hall).Map(next => new UnloadingStep(next, []))).Map(step => step.Hall);

    public Result<UnloadingStep> UpdateWithEvents(Func<ReclaimHall, Result<UnloadingStep>> transition)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var outcome = transition(snapshot.Hall);
            if (outcome is not Result<UnloadingStep>.Ok ok)
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

    public void MarkPublished(Guid eventId)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var outbox = snapshot.Outbox.RemoveAll(e => e.EventId == eventId);
            if (outbox.Count == snapshot.Outbox.Count
                || ReferenceEquals(Interlocked.CompareExchange(ref _state, snapshot with { Outbox = outbox }, snapshot), snapshot))
            {
                return;
            }
        }
    }

    private sealed record State(ReclaimHall Hall, ImmutableList<BaggageEvent> Outbox);
}
