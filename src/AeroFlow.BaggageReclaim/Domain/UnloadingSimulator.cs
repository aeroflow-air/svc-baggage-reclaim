using System.Collections.Immutable;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>How fast the simulated hall moves. Bound from the <c>Baggage</c> configuration section.</summary>
public sealed record UnloadingSettings(
    TimeSpan FirstBagAfterLanding,
    int BagsPerTick,
    int CollectedPerTick,
    double DelayedBagProbability);


/// <summary>
/// Pure simulation of bags coming off aircraft. Each tick, for every open reclaim whose first-bag time has passed:
/// up to <see cref="UnloadingSettings.BagsPerTick"/> unloaded bags reach the belt (a few may be delayed instead),
/// then passengers collect up to <see cref="UnloadingSettings.CollectedPerTick"/> bags already on the belt.
/// Emits <c>BagsOnBelt</c> on the first bag, <c>BagDelayed</c> per delayed bag, <c>LastBagOnBelt</c> when nothing is
/// left in the hold and <c>ReclaimComplete</c> once the belt is empty. Randomness is passed in, so tests are exact. Event ids are deterministic
/// (<see cref="BaggageEvent.TickId(FlightReclaim, BaggageEventType)"/>, ADR-0017): the same flight, landing and event always get the same id.
/// </summary>
public static class UnloadingSimulator
{
    public static HallChange Tick(ReclaimHall hall, DateTimeOffset now, UnloadingSettings settings, Func<double> random)
    {
        var events = ImmutableList.CreateBuilder<BaggageEvent>();
        var open = hall.Reclaims.Values
            .Where(r => r.Status.IsOpen && now >= r.LandedAt + r.Delay + settings.FirstBagAfterLanding)
            .OrderBy(r => r.LandedAt)
            .ThenBy(r => r.Flight.Value, StringComparer.Ordinal)
            .ToList();

        foreach (var before in open)
        {
            var reclaim = before;
            var hadBagOnBelt = reclaim.Bags.Any(b => b.Status is BagStatus.OnBelt or BagStatus.Collected);
            var hadUnloaded = reclaim.Bags.Any(b => b.Status == BagStatus.Unloaded);

            // Collect first, from bags already on the belt, so a bag spends at least one tick on the belt.
            var bags = reclaim.Bags.ToBuilder();
            var collected = 0;
            for (var i = 0; i < bags.Count && collected < settings.CollectedPerTick; i++)
            {
                if (bags[i].Status == BagStatus.OnBelt)
                {
                    bags[i] = bags[i] with { Status = BagStatus.Collected };
                    collected++;
                }
            }

            var moved = 0;
            for (var i = 0; i < bags.Count && moved < settings.BagsPerTick; i++)
            {
                if (bags[i].Status != BagStatus.Unloaded)
                {
                    continue;
                }

                moved++;
                if (random() < settings.DelayedBagProbability)
                {
                    bags[i] = bags[i] with { Status = BagStatus.Delayed };
                    events.Add(BaggageEvent.For(reclaim, BaggageEventType.BagDelayed, now, BaggageEvent.TickId(reclaim, bags[i].Tag)) with
                    {
                        BagTag = bags[i].Tag.Value,
                        Reason = "not loaded at origin",
                    });
                }
                else
                {
                    bags[i] = bags[i] with { Status = BagStatus.OnBelt };
                }
            }

            reclaim = reclaim with { Bags = bags.ToImmutable() };
            var hasBagOnBelt = reclaim.Bags.Any(b => b.Status is BagStatus.OnBelt or BagStatus.Collected);
            var hasUnloaded = reclaim.Bags.Any(b => b.Status == BagStatus.Unloaded);

            if (!hadBagOnBelt && hasBagOnBelt)
            {
                events.Add(BaggageEvent.For(reclaim, BaggageEventType.BagsOnBelt, now, BaggageEvent.TickId(reclaim, BaggageEventType.BagsOnBelt)) with { BagCount = reclaim.Bags.Count });
            }

            if (hadUnloaded && !hasUnloaded && hasBagOnBelt)
            {
                events.Add(BaggageEvent.For(reclaim, BaggageEventType.LastBagOnBelt, now, BaggageEvent.TickId(reclaim, BaggageEventType.LastBagOnBelt)));
            }

            hall = hall with { Reclaims = hall.Reclaims.SetItem(reclaim.Flight, reclaim) };

            if (!hasUnloaded && reclaim.Bags.All(b => b.Status is not BagStatus.OnBelt))
            {
                hall = ReclaimPlanner.Apply(hall, new CompleteReclaim(reclaim.Flight, now)).Match(h => h, _ => hall);
                var done = hall.Reclaims[reclaim.Flight];
                events.Add(BaggageEvent.For(done, BaggageEventType.ReclaimComplete, now, BaggageEvent.TickId(done, BaggageEventType.ReclaimComplete)) with
                {
                    BagCount = done.Bags.Count,
                    BagsCollected = done.BagsCollected,
                    BagsMissing = done.Bags.Count(b => b.Status == BagStatus.Missing),
                });
            }
        }

        return new HallChange(hall, events.ToImmutable());
    }
}
