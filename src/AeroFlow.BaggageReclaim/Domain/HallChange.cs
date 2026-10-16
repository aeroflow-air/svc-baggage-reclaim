using System.Collections.Immutable;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>
/// A change to the hall and the baggage events it produced, in order: the result of an unloading tick or of applying
/// a flight event. The store saves both in one atomic update, the events going to the outbox (ADR-0017).
/// </summary>
public sealed record HallChange(ReclaimHall Hall, ImmutableList<BaggageEvent> Events);
