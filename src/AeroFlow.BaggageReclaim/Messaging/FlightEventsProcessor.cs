using System.Text.Json;
using AeroFlow.BaggageReclaim.Domain;
using Azure.Messaging.ServiceBus;
using AeroFlow.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace AeroFlow.BaggageReclaim.Messaging;

/// <summary>
/// Hosted service that reads the svc-baggage-reclaim subscription on the <c>flight-events</c> topic and hands each
/// event to <see cref="ReclaimEventHandler"/>. Business rejections (no belt free, reclaim already closed) are logged and
/// completed so they are not retried; a body that cannot be read is dead-lettered.
/// </summary>
public sealed partial class FlightEventsProcessor(
    ServiceBusClient client,
    ReclaimEventHandler handler,
    OutboxDispatcher outbox,
    IOptions<FlightEventsOptions> options,
    ILogger<FlightEventsProcessor> logger,
    IObservabilityLog observability) : IHostedService, IAsyncDisposable
{
    // Field needed by the .NET 8 [LoggerMessage] generator (it doesn't read primary-ctor params).
    private readonly ILogger _logger = logger;
    private ServiceBusProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        _processor = client.CreateProcessor(o.Topic, o.Subscription, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1,
        });
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
        LogStarted(o.Topic, o.Subscription);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        FlightEventMessage? evt;
        try
        {
            evt = args.Message.Body.ToObjectFromJson<FlightEventMessage>(FlightEventMessage.JsonOptions);
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, args.Message.MessageId);
            Consumed(args, MessageOutcomes.DeadLettered, flightId: null);
            await args.DeadLetterMessageAsync(args.Message, "unreadable", ex.Message, args.CancellationToken);
            return;
        }

        if (evt is null)
        {
            Consumed(args, MessageOutcomes.DeadLettered, flightId: null);
            await args.DeadLetterMessageAsync(args.Message, "unreadable", "empty body", args.CancellationToken);
            return;
        }

        switch (handler.Handle(evt))
        {
            case HandleOutcome.Applied a:
                LogApplied(a.Command, evt.FlightNumber, evt.EventType);
                Consumed(args, MessageOutcomes.Applied, evt.FlightId, evt);
                // Any BeltAssigned is already in the outbox with the hall change; completing the message is safe.
                outbox.Notify();

                break;
            case HandleOutcome.Rejected r:
                LogRejected(evt.EventType, evt.FlightNumber, r.Error.Code, r.Error.Message);
                Consumed(args, MessageOutcomes.Rejected, evt.FlightId, evt);
                break;
            case HandleOutcome.Ignored i:
                LogIgnored(evt.EventType, evt.FlightNumber, i.Reason);
                Consumed(args, MessageOutcomes.Ignored, evt.FlightId, evt);
                break;
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(args.Exception, args.ErrorSource.ToString());
        observability.OperationFailed("flight-events-processor", args.Exception, flightId: null);
        return Task.CompletedTask;
    }

    private void Consumed(ProcessMessageEventArgs args, string outcome, string? flightId, FlightEventMessage? evt = null)
    {
        var o = options.Value;
        observability.MessageConsumed(
            o.Topic,
            o.Subscription,
            evt?.EventType.ToString() ?? args.Message.Subject,
            evt?.EventId.ToString() ?? args.Message.MessageId,
            outcome,
            flightId);
    }

    [LoggerMessage(EventId = 10_000, Level = LogLevel.Information, Message = "Listening for flight events on {Topic}/{Subscription}")]
    private partial void LogStarted(string topic, string subscription);

    [LoggerMessage(EventId = 10_001, Level = LogLevel.Information, Message = "{Command} for {FlightNumber} after {EventType}")]
    private partial void LogApplied(string command, string flightNumber, FlightEventType eventType);

    [LoggerMessage(EventId = 10_002, Level = LogLevel.Warning, Message = "{EventType} for {FlightNumber} rejected: {Code} {Message}")]
    private partial void LogRejected(FlightEventType eventType, string flightNumber, string code, string message);

    [LoggerMessage(EventId = 10_003, Level = LogLevel.Debug, Message = "{EventType} for {FlightNumber} ignored: {Reason}")]
    private partial void LogIgnored(FlightEventType eventType, string flightNumber, string reason);

    [LoggerMessage(EventId = 10_004, Level = LogLevel.Warning, Message = "Dead-lettered unreadable flight event {MessageId}")]
    private partial void LogUnreadable(Exception ex, string messageId);

    [LoggerMessage(EventId = 10_005, Level = LogLevel.Warning, Message = "Service Bus processor error ({Source})")]
    private partial void LogProcessorError(Exception ex, string source);
}
