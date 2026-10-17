using AeroFlow.BaggageReclaim.Auth;
using AeroFlow.BaggageReclaim.Messaging;
using AeroFlow.BaggageReclaim.Storage;
using System.Reflection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: use the host defaults (console JSON in containers when
// configured via environment). Prefer composing here over a shared logging package.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddReclaimAuth(builder.Configuration);

// API docs: Swashbuckle generates the OpenAPI document at /openapi/v1.json and Scalar renders it at /scalar
// (same as svc-flight-status). Gated by ApiDocs:Enabled: true in appsettings.Development.json, off by default.
var apiDocsEnabled = builder.Configuration.GetValue("ApiDocs:Enabled", false);
if (apiDocsEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "AeroFlow Baggage Reclaim API",
            Version = "v1",
            Description = "The LHR reclaim hall: belts, each arriving flight's reclaim, and single bags. Reads are anonymous; completing a reclaim needs the Ops.Reclaim.Write role (ADR-0018).",
        });
        var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xml))
        {
            options.IncludeXmlComments(xml);
        }
    });
}

// One in-memory reclaim hall shared by the event listener and the HTTP API (platform-handbook#69, svc-baggage-reclaim#9).
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
// Outbox (ADR-0017), registered before the processors so it stops after them and drains last: baggage events are stored with the hall change and sent from here, so a failed send is retried.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

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
app.UseAuthentication();
app.UseAuthorization();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapHealthChecks("/health");
app.MapControllers();

if (apiDocsEnabled)
{
    app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.WithTitle("AeroFlow Baggage Reclaim API"));
}

app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
