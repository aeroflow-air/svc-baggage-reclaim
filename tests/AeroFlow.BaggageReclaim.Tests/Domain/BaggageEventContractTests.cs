using System.Text.Json;
using AeroFlow.BaggageReclaim.Domain;
using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Domain;

/// <summary>
/// Producer-side contract tests for baggage-events (ADR-0019, platform-handbook#111): the canonical examples
/// in contracts/baggage-events must round-trip through BaggageEvent with no structural drift.
/// </summary>
public sealed class BaggageEventContractTests
{
    public static TheoryData<string, BaggageEventType> Examples => new()
    {
        { "belt-assigned", BaggageEventType.BeltAssigned },
        { "bags-on-belt", BaggageEventType.BagsOnBelt },
        { "last-bag-on-belt", BaggageEventType.LastBagOnBelt },
        { "reclaim-complete", BaggageEventType.ReclaimComplete },
        { "bag-delayed", BaggageEventType.BagDelayed },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Canonical_example_round_trips_with_the_same_structure(string name, BaggageEventType type)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "baggage-events", $"{name}.json"));
        var evt = JsonSerializer.Deserialize<BaggageEvent>(json, BaggageEvent.JsonOptions);

        Assert.NotNull(evt);
        Assert.Equal((type, "SIM-00002", "KL456", "3"), (evt!.EventType, evt.FlightId, evt.FlightNumber, evt.Belt));

        using var canonical = JsonDocument.Parse(json);
        using var roundTripped = JsonDocument.Parse(JsonSerializer.Serialize(evt, BaggageEvent.JsonOptions));
        Assert.Equal(
            canonical.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind),
            roundTripped.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind));
    }

    [Fact]
    public void Every_baggage_event_type_has_a_canonical_example()
    {
        var covered = Examples.Select(row => (BaggageEventType)row[1]!).ToHashSet();
        Assert.Equal(Enum.GetValues<BaggageEventType>().ToHashSet(), covered);
    }

    [Fact]
    public void Reclaim_complete_carries_the_counts()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "baggage-events", "reclaim-complete.json"));
        var evt = JsonSerializer.Deserialize<BaggageEvent>(json, BaggageEvent.JsonOptions)!;
        Assert.Equal((120, 119, 1), (evt.BagCount, evt.BagsCollected, evt.BagsMissing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AeroFlow.BaggageReclaim.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
