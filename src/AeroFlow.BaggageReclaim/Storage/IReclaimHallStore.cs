using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Storage;

/// <summary>Edge adapter for the reclaim hall and its outbox of unsent baggage events (ADR-0017). The domain never sees this.</summary>
public interface IReclaimHallStore
{
    ReclaimHall Current();

    /// <summary>
    /// Atomically replaces the hall with the outcome of <paramref name="transition"/>.
    /// The transition must be pure: it may be re-run if another writer got there first.
    /// </summary>
    Result<ReclaimHall> Update(Func<ReclaimHall, Result<ReclaimHall>> transition);

    /// <summary>
    /// As <see cref="Update"/>, but the transition also returns the baggage
    /// events the change produced, and they are added to the outbox in the same atomic step.
    /// </summary>
    Result<UnloadingStep> UpdateWithEvents(Func<ReclaimHall, Result<UnloadingStep>> transition);

    /// <summary>Baggage events stored but not yet published, oldest first.</summary>
    IReadOnlyList<BaggageEvent> Pending();

    /// <summary>Removes a published event from the outbox. Unknown ids are ignored.</summary>
    void MarkPublished(Guid eventId);
}
