using Xunit;

namespace AeroFlow.BaggageReclaim.Tests.Architecture;

/// <summary>
/// ADR-0004 / ADR-0023: the domain is pure. See <see cref="DomainPurityRules"/> for what is checked,
/// and AGENTS.md for what is still review-only (null, throw, randomness, async).
/// </summary>
public sealed class DomainPurityTests
{
    private const string ServiceRoot = "AeroFlow.BaggageReclaim";
    private const string FixtureRoot = "AeroFlow.BaggageReclaim.Tests.Architecture.Fixtures";

    [Fact]
    public void Service_has_a_domain_namespace_so_the_rules_are_not_vacuous()
    {
        Assert.NotEmpty(DomainPurityRules.DomainTypeNames(typeof(Program).Assembly, ServiceRoot));
    }

    [Fact]
    public void Service_domain_is_pure()
    {
        var violations = DomainPurityRules.Check(typeof(Program).Assembly, ServiceRoot);

        Assert.True(
            violations.Count == 0,
            "Domain purity (ADR-0004) violated. Move I/O, logging and clock reads to the edge:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"- {v.Key}: {string.Join(", ", v.Value)}")));
    }

    [Theory]
    [InlineData("logging", "UsesLogger")]
    [InlineData("file and stream I/O", "UsesFileSystem")]
    [InlineData("network and HTTP", "UsesHttp")]
    [InlineData("data access", "UsesDatabase")]
    [InlineData("hosting, DI, options and configuration", "UsesOptions")]
    [InlineData("ServiceDefaults", "UsesServiceDefaults")]
    [InlineData("service layers outside the domain", "UsesMessagingLayer")]
    [InlineData("injected clock (pass now in as a value)", "UsesTimeProvider")]
    [InlineData("ambient clock (DateTime.Now/UtcNow/Today, DateTimeOffset.Now/UtcNow)", "ReadsUtcNow")]
    [InlineData("ambient clock (DateTime.Now/UtcNow/Today, DateTimeOffset.Now/UtcNow)", "ReadsClockInLambda")]
    public void Rules_catch_each_kind_of_impurity(string rule, string fixtureType)
    {
        var violations = DomainPurityRules.Check(typeof(DomainPurityTests).Assembly, FixtureRoot);

        Assert.True(violations.ContainsKey(rule), $"Rule '{rule}' found nothing.");
        Assert.Contains(violations[rule], name => name.EndsWith("." + fixtureType, StringComparison.Ordinal)
            || name.Contains("." + fixtureType + "/", StringComparison.Ordinal));
    }

    [Fact]
    public void Rules_pass_a_pure_type()
    {
        var violations = DomainPurityRules.Check(typeof(DomainPurityTests).Assembly, FixtureRoot);

        Assert.DoesNotContain(violations.Values.SelectMany(v => v), name => name.Contains(".PureWindow", StringComparison.Ordinal));
    }
}
