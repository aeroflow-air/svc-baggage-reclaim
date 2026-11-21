using AeroFlow.ServiceDefaults;
using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>
/// Runs <see cref="UnloadingSimulator"/> on a timer. Each tick's events go into the outbox in the same atomic update
/// as the hall, and <see cref="OutboxDispatcher"/> sends them (ADR-0017).
/// </summary>
public sealed partial class UnloadingWorker(
    IReclaimHallStore store,
    OutboxDispatcher outbox,
    IOptions<BaggageOptions> options,
    TimeProvider clock,
    ILogger<UnloadingWorker> logger,
    IObservabilityLog? observability = null) : BackgroundService
{
    private readonly ILogger _logger = logger;
    private readonly Random _random = options.Value.Seed is { } seed ? new Random(seed) : new Random();

    /// <summary>One tick: advance the hall and queue its events atomically, then wake the dispatcher. Public for tests.</summary>
    public IReadOnlyList<BaggageEvent> Tick()
    {
        var settings = options.Value.ToSettings();
        var now = clock.GetUtcNow();
        var events = store.UpdateWithEvents(hall => (Result<HallChange>)UnloadingSimulator.Tick(hall, now, settings, _random.NextDouble))
            .Match(step => step.Events, _ => []);
        if (events.Count > 0)
        {
            outbox.Notify();
        }

        return events;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.1, options.Value.TickSeconds)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Tick();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTickFailed(ex);
                observability?.OperationFailed("baggage-unloading-tick", ex, flightId: null);
            }
        }
    }

    [LoggerMessage(EventId = 10_011, Level = LogLevel.Warning, Message = "Baggage unloading tick failed; will retry next tick")]
    private partial void LogTickFailed(Exception ex);
}
