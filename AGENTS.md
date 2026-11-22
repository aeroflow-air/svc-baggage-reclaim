# svc-baggage-reclaim

Assigns reclaim belts to landed flights at LHR and simulates bags through to collection. Owner: platform squad. Standards: [ADR-0004](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0004-functional-style-csharp.md), [coding standards](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/standards/coding-standards.md).

## Where things live
- `Domain/`: pure core. `ReclaimPlanner` (rules), `UnloadingSimulator` (one tick in, new hall and events out),
  `ReclaimQueries` (read projections), `BaggageEvent`, `Identifiers`, `EventIds`, `Errors.cs`, local `Result`/`Option`.
- `Messaging/`: `FlightEventsProcessor` → `ReclaimEventHandler`; `UnloadingWorker` drives ticks with `TimeProvider`;
  `OutboxDispatcher` → `ServiceBusBaggageEventPublisher`. `Storage/`: `IReclaimHallStore`, in memory.
- `Controllers/` + `Models/`: HTTP edge (`/belts`, `/flights/{id}/reclaim`, `/bags/{tag}`). `Auth/`: `Ops.Reclaim.Write`
  policy (ADR-0018), Entra JWT, or the fake handler in Development/tests.

## Non-negotiables (* = enforced by build or CI in this repo)
- `[ApiController]` controllers, not minimal APIs. Errors leave as ProblemDetails 400/404/409 with `code` via
  `ProblemResults`. Write endpoints need `[Authorize(Policy = AuthPolicies.ReclaimWrite)]`; the domain never sees callers.
- The domain is pure: static functions, `sealed record`s, `Result<T>` with typed errors, `Option<T>` not `null`.
  * `DomainPurityTests`: nothing in `Domain/` uses logging, I/O, HTTP, data access, Microsoft.Extensions/ASP.NET,
  ServiceDefaults, other layers, `TimeProvider` or `DateTime(Offset).Now`; ticks pass `now` in.
  Review-only: `throw` (only `UnreachableException`), `null`, `Guid.NewGuid`/`Random`.
- * `AddServiceDefaults()` + `MapDefaultEndpoints()`: OTel, request and messaging logs, `/health`, `/alive`
  (`HealthEndpointTests`, `ObservabilityContractTests`). Don't write your own HTTP or messaging log lines.
- * `[LoggerMessage]` only (CA1848/CA2254 are build errors). Never `Console`/`Debug.WriteLine` (RS0030, from
  `AeroFlow.ServiceDefaults` 0.1.1). Business event ids start at 10,000. No PII, secrets or bodies in logs.
- Options use `Configure<T>` with no startup validation yet; new options use `ValidateOnStart()`. Workflows pin
  `aeroflow-workflows` tags (`dotnet-ci.yml@v0.7.0` with `github-packages: true` and `packages: read`; `v0.7.0`
  for skills), never `@main`, with job `permissions`.

## Events (ADR-0013/0017/0019)
- Consumes `flight-events` on subscription `baggage-reclaim` (never publish to it). Publishes only `baggage-events`,
  via the outbox: handlers and `UnloadingWorker` save each hall change and its events in one `UpdateWithEvents`;
  `OutboxDispatcher` sends them (MessageId = event id, Subject = type, CorrelationId = flight id). Never publish directly.
- Event ids are deterministic: `BaggageEvent.CausedBy(flightEventId, type)` or `BaggageEvent.TickId(...)`, never
  `Guid.NewGuid`. Handlers are idempotent: applied ids are kept in the hall in the same atomic update, so a redelivery
  is `Ignored`; late or out-of-order events return `Ignored`/`Rejected`, never throw. Unknown `FlightEventType` is
  dead-lettered as `unreadable`. Examples in `contracts/baggage-events/`; changes are additive.
- Tests cover handlers, publishing and contracts, but nothing blocks a direct publish. * `workload.yml` checks `workload.yaml`.

## Build, test, docs, skills
`dotnet build -c Release && dotnet test`; domain tests need no mocks. Update `docs/service.md` with behaviour changes.
Plain Markdown in `.claude/skills/<name>/SKILL.md`, pinned in `.agent-skills.yaml`; read each `description`, follow the
Steps. Don't edit `aeroflow-*` folders (* CI drift check; CODEOWNERS). Change them in [agent-skills](https://github.com/aeroflow-air/agent-skills).
