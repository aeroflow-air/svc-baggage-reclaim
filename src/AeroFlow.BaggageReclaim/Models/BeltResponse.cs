using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Models;

/// <summary>API shape for a belt and the flights on it.</summary>
/// <param name="Id">Belt id, for example <c>3</c>.</param>
/// <param name="Capacity">How many flights the belt serves at once.</param>
/// <param name="Reclaims">Non-cancelled reclaims on the belt, earliest landing first.</param>
public sealed record BeltResponse(string Id, int Capacity, IReadOnlyList<BeltReclaimResponse> Reclaims)
{
    public static BeltResponse From(BeltView view) =>
        new(view.Belt.Id.Value, view.Belt.Capacity, view.Reclaims.ConvertAll(BeltReclaimResponse.From));
}

/// <summary>One flight's reclaim on a belt.</summary>
/// <param name="FlightId">The simulator's flight id, for example <c>SIM-00001</c>.</param>
/// <param name="FlightNumber">Flight number.</param>
/// <param name="Origin">IATA code the flight came from.</param>
/// <param name="LandedAt">When the flight landed (the belt was assigned then).</param>
/// <param name="Stage"><c>Assigned</c>, <c>OnBelt</c>, <c>LastBag</c> or <c>Complete</c>.</param>
/// <param name="BagCount">Bags expected for the flight.</param>
/// <param name="BagsOnBelt">Bags on the belt now.</param>
/// <param name="BagsCollected">Bags collected so far.</param>
/// <param name="BagsDelayed">Bags currently delayed.</param>
/// <param name="CompletedAt">When the belt cleared for this flight; null until then.</param>
public sealed record BeltReclaimResponse(
    string FlightId,
    string FlightNumber,
    string Origin,
    DateTimeOffset LandedAt,
    string Stage,
    int BagCount,
    int BagsOnBelt,
    int BagsCollected,
    int BagsDelayed,
    DateTimeOffset? CompletedAt)
{
    public static BeltReclaimResponse From(FlightReclaim reclaim) => new(
        FlightId: reclaim.Flight.Value,
        FlightNumber: reclaim.FlightNumber.Value,
        Origin: reclaim.Origin,
        LandedAt: reclaim.LandedAt,
        Stage: ReclaimQueries.Stage(reclaim).ToString(),
        BagCount: reclaim.Bags.Count,
        BagsOnBelt: reclaim.BagsOnBelt,
        BagsCollected: reclaim.BagsCollected,
        BagsDelayed: reclaim.Bags.Count(b => b.Status == BagStatus.Delayed),
        CompletedAt: reclaim.Status is ReclaimStatus.Complete complete ? complete.At : null);
}
