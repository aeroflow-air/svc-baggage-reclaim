using AeroFlow.BaggageReclaim.Domain;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Publishes to the <c>baggage-events</c> topic: JSON body, Subject = event type, CorrelationId = flight id.</summary>
public sealed partial class ServiceBusBaggageEventPublisher(
    ServiceBusClient client,
    IOptions<BaggageOptions> options,
    ILogger<ServiceBusBaggageEventPublisher> logger) : IBaggageEventPublisher, IAsyncDisposable
{
    private readonly ILogger _logger = logger;
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.Topic);

    public static ServiceBusMessage ToMessage(BaggageEvent evt) =>
        new(BinaryData.FromObjectAsJson(evt, BaggageEvent.JsonOptions))
        {
            MessageId = evt.EventId.ToString(),
            Subject = evt.EventType.ToString(),
            CorrelationId = evt.FlightId,
            ContentType = "application/json",
        };

    public async Task PublishAsync(IReadOnlyCollection<BaggageEvent> events, CancellationToken cancellationToken)
    {
        foreach (var evt in events)
        {
            await _sender.SendMessageAsync(ToMessage(evt), cancellationToken);
            LogPublished(evt.EventType, evt.FlightNumber, evt.Belt);
        }
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {EventType} for {FlightNumber} on belt {Belt}")]
    private partial void LogPublished(BaggageEventType eventType, string flightNumber, string belt);
}
