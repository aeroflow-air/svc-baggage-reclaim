# ServiceDefaults: logging conventions and event catalogue

Status: the package lives in this repository at `src/AeroFlow.ServiceDefaults` (version 0.1.0). Design: [ADR-0022](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0022-observability-on-the-golden-path.md) and the [observability design page](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/design/observability.md).

This is the golden-path observability package, promoted from `AeroFlow.Local.ServiceDefaults` in aeroflow-local. There is no `lib-*` repo and no NuGet feed in the organisation yet, so the package is housed here — the place ADR-0014 already pointed to ("until published from the template"). The recommended long-term home is a new `lib-service-defaults` repository that publishes `AeroFlow.ServiceDefaults` and `AeroFlow.ServiceDefaults.Testing` to GitHub Packages. This record does not create that repo.

The template service calls `AddServiceDefaults()` in `Program.cs`. Existing `svc-*` services are a later follow-up.

## What the package emits

After `builder.AddServiceDefaults()`, the package writes the standard lines. A squad does not log HTTP requests, message publish/consume, or unexpected failures itself. It only adds **business events**.

Every platform line carries the same fields, as a logging scope:

| Field | Source |
| --- | --- |
| `service.name` | `Observability:ServiceName`, else `OTEL_SERVICE_NAME`, else the host application name |
| `service.version` | `Observability:ServiceVersion`, else the entry assembly informational version |
| `deployment.environment` | `Observability:Environment`, else the host environment name |
| `aeroflow.squad` | `Observability:Squad` (defaults to `unknown` if unset — set it) |
| `trace.id` | Current `Activity` when one is present |
| `flight.id` | When present: `X-Flight-Id` header, or the `aeroflow.flight_id` activity tag / baggage, or the value passed to `IObservabilityLog` |

Flight id is an operational identifier, not PII. It belongs on traces and logs, never as a metric dimension.

HTTP request logs are automatic. Messaging and unhandled-operation failures go through `IObservabilityLog`, which processors and publishers inject and call at the edge. Domain code still does no logging (ADR-0004).

## Event catalogue

Platform event ids are reserved below 10,000. Business events start at `PlatformEventIds.BusinessEventsStart` (10,000). Do not reuse a platform id.

| Event id | Name | Level | When | Extra state |
| --- | --- | --- | --- | --- |
| 1000 | HttpRequestCompleted | Information | An HTTP request finished with a non-5xx status | `Method`, `Path` (no query string), `StatusCode`, `DurationMs` |
| 1001 | HttpRequestFailed | Warning | The request finished with a 5xx status | Same as 1000 |
| 1002 | HttpRequestUnhandled | Error | An unhandled exception escaped the pipeline | `Method`, `Path`, `DurationMs`, plus the exception |
| 2000 | MessagePublished | Information | A message was sent | `Topic`, `Subject`, `MessageId` |
| 2001 | MessagePublishFailed | Error | Send failed | Same as 2000, plus the exception |
| 2002 | MessageConsumed | Information | A message was handled | `Topic`, `Subscription`, `Subject`, `MessageId`, `Outcome` (`applied`, `rejected`, `ignored`, `dead-lettered`) |
| 2003 | MessageConsumeFailed | Error | Handling threw | Same as 2002 without outcome, plus the exception |
| 3000 | OperationFailed | Error | An unexpected failure at the edge (for example the outbox dispatcher) | `Operation`, plus the exception |
| 10000+ | *(service business events)* | as declared | Domain-meaningful things the edge chooses to record | Named properties; include `flight.id` in a scope or as a parameter when you have one |

Health probes (`/health`, `/alive`) are not request-logged and are excluded from traces. They are mapped in every environment.

The category for all platform lines is `AeroFlow.ServiceDefaults`.

## Business events: conventions and example

- Use source-generated `[LoggerMessage]` with a **fixed event id** at or above 10,000, a static message template, and named properties. CA1848 and CA2254 are errors in any project that imports this package's props.
- Put the methods in a `sealed partial` static class next to the edge that calls them (a controller, processor or worker). Do not log from `Domain`.
- Reuse the canonical field names in `LogFields` when the value is one of those (`flight.id` can be a parameter named `flightId` plus a scope, or a call to `IObservabilityLog` which already adds the scope).
- Do not log PII, secrets, tokens, connection strings or request bodies. Flight number and flight id are allowed.
- Keep event ids stable once shipped; add a new method for a new event.

```csharp
using Microsoft.Extensions.Logging;

namespace AeroFlow.ServiceTemplate;

internal static partial class BookingLog
{
    [LoggerMessage(
        EventId = 10_000,
        Level = LogLevel.Information,
        Message = "Booking {BookingId} confirmed on flight {FlightNumber}")]
    public static partial void BookingConfirmed(ILogger logger, string bookingId, string flightNumber);

    [LoggerMessage(
        EventId = 10_001,
        Level = LogLevel.Information,
        Message = "Booking {BookingId} was not found")]
    public static partial void BookingNotFound(ILogger logger, string bookingId);
}
```

## Configuration

```json
{
  "Observability": {
    "ServiceName": "template-dotnet-service",
    "ServiceVersion": "0.1.0",
    "Environment": "local",
    "Squad": "platform"
  }
}
```

All four properties are optional; the defaults above apply. Set `Squad` — `unknown` is only a fallback so the field is never missing.

Exporters: set `OTEL_EXPORTER_OTLP_ENDPOINT` for the Aspire dashboard (local) and `APPLICATIONINSIGHTS_CONNECTION_STRING` for Azure Monitor. Squads do not wire exporters in code.

## CI enforcement

Projects that import `src/AeroFlow.ServiceDefaults/build/AeroFlow.ServiceDefaults.props` (this repository does, via `Directory.Build.props`; NuGet consumers get `buildTransitive`) treat as errors:

- **CA1848** — use `[LoggerMessage]` rather than `ILogger.Log*` with a runtime template
- **CA2254** — the message template must be a compile-time constant
- **RS0030** — `System.Console` and `Debug.WriteLine(string)` are banned (`BannedSymbols.txt`)

## Contract tests

`AeroFlow.ServiceDefaults.Testing` ships `LogCollector` and `ObservabilityLogAssertions`. A service test boots the app, registers the collector, exercises an endpoint or `IObservabilityLog`, and asserts the required fields:

```csharp
host.Logs
    .RequireHttpRequest("GET", "/ping", 200)
    .RequireRequiredFields(host.Resource)
    .RequireFlightId("AF204");
```

## Versioning and publish

The package version is **0.1.0**, set in the project file. `dotnet pack src/AeroFlow.ServiceDefaults/AeroFlow.ServiceDefaults.csproj` produces the nupkg; nothing is published yet, because the organisation has no GitHub Packages (NuGet) workflow. When `lib-service-defaults` exists, versions should follow the same tagged-release habit as `aeroflow-workflows`.
