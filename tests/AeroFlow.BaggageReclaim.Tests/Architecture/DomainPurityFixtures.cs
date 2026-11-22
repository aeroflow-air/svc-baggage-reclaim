using System.Data.Common;
using AeroFlow.ServiceDefaults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Deliberately impure "domain" types that prove each rule in DomainPurityRules fails when it should.
// They live in the test assembly only and are never part of a service.
namespace AeroFlow.BaggageReclaim.Tests.Architecture.Fixtures.Domain
{
    public sealed class UsesLogger(ILogger<UsesLogger> logger)
    {
        public ILogger Logger { get; } = logger;
    }

    public static class UsesFileSystem
    {
        public static bool HasFile(string path) => File.Exists(path);
    }

    public sealed class UsesHttp(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }

    public sealed class UsesDatabase(DbConnection connection)
    {
        public DbConnection Connection { get; } = connection;
    }

    public sealed class UsesOptions(IOptions<PureWindow> options)
    {
        public PureWindow Window { get; } = options.Value;
    }

    public sealed class UsesServiceDefaults(ObservabilityOptions options)
    {
        public ObservabilityOptions Options { get; } = options;
    }

    public sealed class UsesMessagingLayer(Messaging.Outbox outbox)
    {
        public Messaging.Outbox Outbox { get; } = outbox;
    }

    public sealed class UsesTimeProvider(TimeProvider clock)
    {
        public DateTimeOffset Now => clock.GetUtcNow();
    }

    public static class ReadsUtcNow
    {
        public static bool IsPast(DateTime value) => value < DateTime.UtcNow;
    }

    public static class ReadsClockInLambda
    {
        public static IEnumerable<DateTimeOffset> Past(IEnumerable<DateTimeOffset> values) =>
            values.Where(v => v < DateTimeOffset.Now);
    }

    public sealed record PureWindow(DateTimeOffset Opens, DateTimeOffset Closes)
    {
        public bool IsOpenAt(DateTimeOffset now) => now >= Opens && now < Closes;
    }
}

namespace AeroFlow.BaggageReclaim.Tests.Architecture.Fixtures.Messaging
{
    public sealed class Outbox;
}
