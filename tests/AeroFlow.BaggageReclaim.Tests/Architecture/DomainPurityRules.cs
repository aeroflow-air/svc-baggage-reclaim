using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace AeroFlow.BaggageReclaim.Tests.Architecture;

/// <summary>
/// ADR-0004 / ADR-0023 domain purity, as architecture rules over compiled IL (NetArchTest.Rules + Mono.Cecil).
/// Applies to every type in <c>{root}.Domain</c> and its sub-namespaces, including compiler-generated
/// nested types (lambdas, iterators, async state machines).
/// </summary>
public static class DomainPurityRules
{
    /// <summary>Namespace prefixes the domain must not depend on, by reason.</summary>
    public static IReadOnlyList<(string Rule, string[] Prefixes)> ForbiddenDependencies(string root) =>
    [
        ("logging", ["Microsoft.Extensions.Logging", "Serilog", "System.Diagnostics.Debug", "System.Diagnostics.Trace"]),
        ("file and stream I/O", ["System.IO"]),
        ("network and HTTP", ["System.Net"]),
        ("data access", ["Microsoft.EntityFrameworkCore", "System.Data", "Microsoft.Data", "Dapper", "Npgsql"]),
        ("messaging and cloud SDKs", ["Azure", "MassTransit"]),
        ("hosting, DI, options and configuration", ["Microsoft.Extensions", "Microsoft.AspNetCore"]),
        ("ServiceDefaults", ["AeroFlow.ServiceDefaults"]),
        ("service layers outside the domain",
            [$"{root}.Messaging", $"{root}.Infrastructure", $"{root}.Persistence", $"{root}.Storage", $"{root}.Controllers"]),
        ("injected clock (pass now in as a value)", ["System.TimeProvider"]),
    ];

    /// <summary>Returns rule name to offending types. Empty means the domain is pure.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Check(Assembly assembly, string root)
    {
        var violations = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var (rule, prefixes) in ForbiddenDependencies(root))
        {
            var result = DomainTypes(assembly, root).Should().NotHaveDependencyOnAny(prefixes).GetResult();
            if (!result.IsSuccessful)
            {
                violations[rule] = Names(result);
            }
        }

        var clock = DomainTypes(assembly, root).Should().MeetCustomRule(new NoAmbientClock()).GetResult();
        if (!clock.IsSuccessful)
        {
            violations[NoAmbientClock.Rule] = Names(clock);
        }

        return violations;
    }

    public static IReadOnlyList<string> DomainTypeNames(Assembly assembly, string root) =>
        DomainTypes(assembly, root).GetTypes().Select(t => t.FullName!).ToList();

    private static PredicateList DomainTypes(Assembly assembly, string root) =>
        Types.InAssembly(assembly).That().ResideInNamespaceMatching($@"^{Regex.Escape(root)}\.Domain(\..+)?$");

    private static IReadOnlyList<string> Names(TestResult result) =>
        result.FailingTypeNames?.ToList() ?? [];

    /// <summary>
    /// Member-level check NetArchTest can't express with its built-in predicates: no call to
    /// <c>DateTime.Now/UtcNow/Today</c> or <c>DateTimeOffset.Now/UtcNow</c> anywhere in a domain type's IL.
    /// Using <c>DateTime</c>/<c>DateTimeOffset</c> as values is fine.
    /// </summary>
    private sealed class NoAmbientClock : ICustomRule
    {
        public const string Rule = "ambient clock (DateTime.Now/UtcNow/Today, DateTimeOffset.Now/UtcNow)";

        private static readonly HashSet<string> Forbidden =
        [
            "System.DateTime::get_Now", "System.DateTime::get_UtcNow", "System.DateTime::get_Today",
            "System.DateTimeOffset::get_Now", "System.DateTimeOffset::get_UtcNow",
        ];

        public bool MeetsRule(TypeDefinition type) => !ReadsClock(type);

        private static bool ReadsClock(TypeDefinition type) =>
            type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(IsClockRead)
            || type.NestedTypes.Any(ReadsClock);

        private static bool IsClockRead(Instruction instruction) =>
            instruction.Operand is MethodReference method
            && Forbidden.Contains($"{method.DeclaringType.FullName}::{method.Name}");
    }
}
