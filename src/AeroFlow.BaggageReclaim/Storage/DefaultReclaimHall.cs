using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Storage;

/// <summary>The demo reclaim hall: seven belts, each able to take two flights at once.</summary>
public static class DefaultReclaimHall
{
    public static ReclaimHall Create() =>
        ReclaimHall.Create(new[] { "1", "2", "3", "4", "5", "6", "7" }.Select(id => new Belt(Ok(BeltId.Parse(id)), Capacity: 2)))
            .Match(hall => hall, error => throw new InvalidOperationException($"Invalid demo hall: {error.Message}"));

    // Seed data is hard-coded, so an invalid value is a programming error rather than a business outcome.
    private static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new InvalidOperationException(error.Message));
}
