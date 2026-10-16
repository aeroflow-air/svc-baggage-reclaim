using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Models;

/// <summary>API shape for one flight's reclaim, with every bag.</summary>
/// <param name="FlightId">The simulator's flight id, for example <c>SIM-00001</c>.</param>
/// <param name="FlightNumber">Flight number.</param>
/// <param name="Origin">IATA code the flight came from.</param>
/// <param name="BeltId">Belt the flight's bags go to.</param>
/// <param name="LandedAt">When the flight landed.</param>
/// <param name="Status"><c>Unloading</c>, <c>Complete</c> or <c>Cancelled</c>.</param>
/// <param name="Stage"><c>Assigned</c>, <c>OnBelt</c>, <c>LastBag</c> or <c>Complete</c>.</param>
/// <param name="DelayMinutes">Delay recorded against the flight, in whole minutes.</param>
/// <param name="CompletedAt">When the belt cleared for this flight; null until then.</param>
/// <param name="CancelReason">Why the reclaim was cancelled; null unless cancelled.</param>
/// <param name="Bags">Every bag on the flight, in tag order.</param>
public sealed record ReclaimResponse(
    string FlightId,
    string FlightNumber,
    string Origin,
    string BeltId,
    DateTimeOffset LandedAt,
    string Status,
    string Stage,
    int DelayMinutes,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    IReadOnlyList<ReclaimBagResponse> Bags)
{
    public static ReclaimResponse From(FlightReclaim reclaim) => new(
        FlightId: reclaim.Flight.Value,
        FlightNumber: reclaim.FlightNumber.Value,
        Origin: reclaim.Origin,
        BeltId: reclaim.Belt.Value,
        LandedAt: reclaim.LandedAt,
        Status: StatusName(reclaim.Status),
        Stage: ReclaimQueries.Stage(reclaim).ToString(),
        DelayMinutes: (int)reclaim.Delay.TotalMinutes,
        CompletedAt: reclaim.Status is ReclaimStatus.Complete complete ? complete.At : null,
        CancelReason: reclaim.Status is ReclaimStatus.Cancelled cancelled ? cancelled.Reason : null,
        Bags: reclaim.Bags.OrderBy(b => b.Tag.Value, StringComparer.Ordinal).Select(ReclaimBagResponse.From).ToList());

    public static string StatusName(ReclaimStatus status) => status switch
    {
        ReclaimStatus.Unloading => "Unloading",
        ReclaimStatus.Complete => "Complete",
        ReclaimStatus.Cancelled => "Cancelled",
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}

/// <summary>One bag within a reclaim.</summary>
/// <param name="Tag">Bag tag, for example <c>BA000123</c>.</param>
/// <param name="Status"><c>Unloaded</c>, <c>OnBelt</c>, <c>Collected</c>, <c>Delayed</c> or <c>Missing</c>.</param>
public sealed record ReclaimBagResponse(string Tag, string Status)
{
    public static ReclaimBagResponse From(Bag bag) => new(bag.Tag.Value, bag.Status.ToString());
}

/// <summary>API shape for a single bag and where it is.</summary>
/// <param name="Tag">Bag tag, for example <c>BA000123</c>.</param>
/// <param name="Status"><c>Unloaded</c>, <c>OnBelt</c>, <c>Collected</c>, <c>Delayed</c> or <c>Missing</c>.</param>
/// <param name="FlightId">Flight the bag arrived on.</param>
/// <param name="FlightNumber">Flight number.</param>
/// <param name="BeltId">Belt the bag goes to.</param>
/// <param name="ReclaimStatus">The flight's reclaim status: <c>Unloading</c>, <c>Complete</c> or <c>Cancelled</c>.</param>
public sealed record BagResponse(string Tag, string Status, string FlightId, string FlightNumber, string BeltId, string ReclaimStatus)
{
    public static BagResponse From(BagLocation location) => new(
        Tag: location.Bag.Tag.Value,
        Status: location.Bag.Status.ToString(),
        FlightId: location.Reclaim.Flight.Value,
        FlightNumber: location.Reclaim.FlightNumber.Value,
        BeltId: location.Reclaim.Belt.Value,
        ReclaimStatus: ReclaimResponse.StatusName(location.Reclaim.Status));
}
