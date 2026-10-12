using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Binds the <c>Baggage</c> section: the outgoing topic and how fast simulated unloading runs.</summary>
public sealed class BaggageOptions
{
    public const string SectionName = "Baggage";

    public string Topic { get; set; } = "baggage-events";

    /// <summary>Turn the unloading simulation off to keep reclaims frozen at BeltAssigned.</summary>
    public bool SimulateUnloading { get; set; } = true;

    public double TickSeconds { get; set; } = 2;

    public double FirstBagAfterLandingSeconds { get; set; } = 10;

    public int BagsPerTick { get; set; } = 12;

    public int CollectedPerTick { get; set; } = 10;

    public double DelayedBagProbability { get; set; } = 0.01;

    /// <summary>Fixed seed for a repeatable run; random when unset.</summary>
    public int? Seed { get; set; }

    public UnloadingSettings ToSettings() => new(
        TimeSpan.FromSeconds(Math.Max(0, FirstBagAfterLandingSeconds)),
        Math.Max(1, BagsPerTick),
        Math.Max(1, CollectedPerTick),
        Math.Clamp(DelayedBagProbability, 0, 1));
}
