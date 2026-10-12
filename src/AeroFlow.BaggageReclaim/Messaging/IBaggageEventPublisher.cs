using AeroFlow.BaggageReclaim.Domain;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Sends baggage events to the outside world. Service Bus when <c>messaging</c> exists; otherwise a no-op.</summary>
public interface IBaggageEventPublisher
{
    Task PublishAsync(IReadOnlyCollection<BaggageEvent> events, CancellationToken cancellationToken);
}

/// <summary>Used when no <c>messaging</c> connection is configured: the hall still runs, nothing leaves the process.</summary>
public sealed class NoOpBaggageEventPublisher : IBaggageEventPublisher
{
    public Task PublishAsync(IReadOnlyCollection<BaggageEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
}
