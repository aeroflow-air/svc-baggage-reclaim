using AeroFlow.BaggageReclaim.Domain;
using Azure.Messaging.ServiceBus;
using AeroFlow.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>Publishes to the <c>baggage-events</c> topic: JSON body, Subject = event type, CorrelationId = flight id.</summary>
public sealed partial class ServiceBusBaggageEventPublisher(
    ServiceBusClient client,
    IOptions<BaggageOptions> options,
    ILogger<ServiceBusBaggageEventPublisher> logger,
    IObservabilityLog observability) : IBaggageEventPublisher, IAsyncDisposable
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
        var topic = options.Value.Topic;
        foreach (var evt in events)
        {
            try
            {
                await _sender.SendMessageAsync(ToMessage(evt), cancellationToken);
            }
            catch (Exception ex)
            {
                observability.MessagePublishFailed(topic, evt.EventType.ToString(), evt.EventId.ToString(), ex, evt.FlightId);
                throw;
            }

            observability.MessagePublished(topic, evt.EventType.ToString(), evt.EventId.ToString(), evt.FlightId);
            LogPublished(evt.EventType, evt.FlightNumber, evt.Belt);
        }
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();

    [LoggerMessage(EventId = 10_006, Level = LogLevel.Information, Message = "Published {EventType} for {FlightNumber} on belt {Belt}")]
    private partial void LogPublished(BaggageEventType eventType, string flightNumber, string belt);
}
