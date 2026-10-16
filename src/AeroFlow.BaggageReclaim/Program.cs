using AeroFlow.BaggageReclaim.Messaging;
using AeroFlow.BaggageReclaim.Storage;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: use the host defaults (console JSON in containers when
// configured via environment). Prefer composing here over a shared logging package.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// One in-memory reclaim hall shared by the event listener and the read API (GET /belts; rest of the API is #69).
builder.Services.AddSingleton<IReclaimHallStore>(_ => new InMemoryReclaimHallStore(DefaultReclaimHall.Create()));

// Flight events (platform-handbook#67): react to the simulator on the flight-events topic. Only wired when a
// "messaging" connection string exists (the aeroflow-local AppHost supplies it via WithReference), so a plain
// `dotnet run`, the container and the tests still start without Service Bus.
builder.Services.Configure<FlightEventsOptions>(builder.Configuration.GetSection(FlightEventsOptions.SectionName));
builder.Services.AddSingleton<ReclaimEventHandler>();

// Baggage events (platform-handbook#68): simulated unloading runs either way; events only leave the process
// (to the baggage-events topic) when the same "messaging" connection exists.
builder.Services.Configure<BaggageOptions>(builder.Configuration.GetSection(BaggageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(FlightEventsOptions.ConnectionName)))
{
    builder.AddAzureServiceBusClient(FlightEventsOptions.ConnectionName);
    builder.Services.AddSingleton<IBaggageEventPublisher, ServiceBusBaggageEventPublisher>();
    builder.Services.AddHostedService<FlightEventsProcessor>();
}
else
{
    builder.Services.AddSingleton<IBaggageEventPublisher, NoOpBaggageEventPublisher>();
}

// Outbox (ADR-0017): baggage events are stored with the hall change and sent from here, so a failed send is retried.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

if (builder.Configuration.GetValue(BaggageOptions.SectionName + ":" + nameof(BaggageOptions.SimulateUnloading), true))
{
    builder.Services.AddHostedService<UnloadingWorker>();
}

// ---------------------------------------------------------------------------
// OpenTelemetry stub — enable when the squad wires an exporter (OTLP / App Insights).
// Keep this commented until you have somewhere to send traces/metrics; no dead deps.
//
// builder.Services.AddOpenTelemetry()
//     .WithTracing(t => t
//         .AddAspNetCoreInstrumentation()
//         .AddHttpClientInstrumentation()
//         // .AddOtlpExporter()
//     )
//     .WithMetrics(m => m
//         .AddAspNetCoreInstrumentation()
//         .AddRuntimeInstrumentation()
//         // .AddOtlpExporter()
//     );
// ---------------------------------------------------------------------------

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
