using AeroFlow.BaggageReclaim.Domain;
using AeroFlow.BaggageReclaim.Storage;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Runs <see cref="UnloadingSimulator"/> on a timer and publishes what each tick produced.</summary>
public sealed partial class UnloadingWorker(
    IReclaimHallStore store,
    IBaggageEventPublisher publisher,
    IOptions<BaggageOptions> options,
    TimeProvider clock,
    ILogger<UnloadingWorker> logger) : BackgroundService
{
    private readonly ILogger _logger = logger;
    private readonly Random _random = options.Value.Seed is { } seed ? new Random(seed) : new Random();

    /// <summary>One tick: advance the hall atomically, then publish. Public so tests can drive it without a timer.</summary>
    public async Task<IReadOnlyList<BaggageEvent>> TickAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.ToSettings();
        var now = clock.GetUtcNow();
        UnloadingStep? step = null;
        store.Update(hall =>
        {
            step = UnloadingSimulator.Tick(hall, now, settings, _random.NextDouble, Guid.NewGuid);
            return step.Hall;
        });

        var events = step?.Events ?? [];
        if (events.Count > 0)
        {
            await publisher.PublishAsync(events, cancellationToken);
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
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTickFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Baggage unloading tick failed; will retry next tick")]
    private partial void LogTickFailed(Exception ex);
}
