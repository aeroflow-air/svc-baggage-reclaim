using System.Threading.Channels;
using AeroFlow.BaggageReclaim.Storage;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Binds the <c>Outbox</c> section.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the outbox is retried when nothing wakes the dispatcher, for example after a failed send.</summary>
    public double RetrySeconds { get; set; } = 5;
}

/// <summary>
/// Sends the baggage events waiting in the store's outbox, oldest first, and removes each one once it is sent
/// (ADR-0017). A failed send leaves it and everything after it in the outbox, so order is kept and the next attempt
/// retries from there. Delivery is at least once: a send that succeeded but reported failure is sent again with the
/// same <c>MessageId</c>; duplicate detection on the topic (set in the aeroflow-local AppHost) and consumers drop it.
/// </summary>
public sealed partial class OutboxDispatcher(
    IReclaimHallStore store,
    IBaggageEventPublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _draining = new(1, 1);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    /// <summary>Asks the dispatcher to drain the outbox now instead of waiting for the retry interval.</summary>
    public void Notify() => _wake.Writer.TryWrite(true);

    /// <summary>
    /// Sends everything pending, stopping at the first failure. Returns how many were sent. Only one drain runs at a
    /// time, and it stops if the head changed under it, so overlapping callers can't re-send or reorder. Public for tests.
    /// </summary>
    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        await _draining.WaitAsync(cancellationToken);
        try
        {
            var sent = 0;
            foreach (var evt in store.Pending())
            {
                await publisher.PublishAsync([evt], cancellationToken);
                if (!store.MarkPublished(evt))
                {
                    break;
                }

                sent++;
            }

            return sent;
        }
        finally
        {
            _draining.Release();
        }
    }

    /// <summary>
    /// Stops the loop, then makes one best-effort drain within the host's shutdown timeout so a routine deploy doesn't
    /// drop pending events. Registered before the message processor, so it stops after it (hosts stop in reverse order).
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await DrainAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogShutdownDrainFailed(ex);
        }

        if (store.Pending().Count is var left and > 0)
        {
            LogPendingAtShutdown(left);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retry = TimeSpan.FromSeconds(Math.Max(0.1, options.Value.RetrySeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Includes a publisher timeout (OperationCanceledException) that isn't a shutdown: never stop the host,
                // or the in-memory outbox would be lost.
                if (ex is ServiceBusException { IsTransient: false } permanent)
                {
                    // Won't fix itself (too large, topic missing): holds back everything behind it.
                    LogPermanentFailure(permanent, permanent.Reason.ToString(), store.Pending().Count);
                }
                else if (ex is UnauthorizedAccessException)
                {
                    // Rejected credentials surface as this, not as a ServiceBusException.
                    LogPermanentFailure(ex, "Unauthorized", store.Pending().Count);
                }
                else
                {
                    LogPublishFailed(ex, store.Pending().Count, retry.TotalSeconds);
                }

                // Back off for the full interval: wake-ups from new changes don't cut it short while the bus is down.
                await Task.Delay(retry, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                _wake.Reader.TryRead(out _);
                continue;
            }

            // Wait for a wake-up or the retry interval, whichever comes first.
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var wake = _wake.Reader.ReadAsync(waiting.Token).AsTask();
            await Task.WhenAny(wake, Task.Delay(retry, clock, waiting.Token));
            await waiting.CancelAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing baggage events failed; {Pending} pending in the outbox, retrying in {RetrySeconds}s")]
    private partial void LogPublishFailed(Exception ex, int pending, double retrySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Permanent failure publishing baggage events ({Reason}); {Pending} pending in the outbox are blocked behind it")]
    private partial void LogPermanentFailure(Exception ex, string reason, int pending);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Final drain of baggage events at shutdown failed")]
    private partial void LogShutdownDrainFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping with {Pending} baggage events still in the outbox; they are lost with the in-memory state")]
    private partial void LogPendingAtShutdown(int pending);
}
