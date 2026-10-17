# svc-baggage-reclaim

Assigns reclaim belts to landed flights at LHR and simulates bags through to collection.

## Conventions (read before changing code)

This repo follows [ADR-0004: functional-style C#](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0004-functional-style-csharp.md).
The checklist is [C# coding standards](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/standards/coding-standards.md). In short:

- Domain logic is **pure static functions** composed with `Bind` / `Map` / `Match` (like `ReclaimPlanner`).
- Domain types are **immutable `sealed record`s** with immutable collections; change means returning a new value.
- States are **closed sum types**, ids are **value types** built via `Parse` / `Create`, never raw strings.
- Business failures return **`Result<T>` with typed error records** from `Domain/Errors.cs`
  (`ValidationError` / `NotFoundError` / `RuleViolation` → 400 / 404 / 409). No exceptions for business rules or control flow;
  `throw` only for bugs (`UnreachableException`) or invalid startup data.
- Absence is **`Option<T>`**, not `null`. Nullable types only in HTTP request/response records and wire messages.
- **No side effects in the domain**: no I/O, logging, `Console`, messaging, storage, static mutable state or
  `DateTimeOffset.UtcNow`. Time comes from `TimeProvider` at the edge.
- BCL only: the local `Result` / `Option` are deliberately small. No LanguageExt.
- Edges: async all the way (no `.Result` / `.Wait()`), flow `CancellationToken`, `[LoggerMessage]` structured logging.

## Where things live

- `src/AeroFlow.BaggageReclaim/Domain`: pure core. `ReclaimPlanner` (pure rules, no I/O, no clock) and `UnloadingSimulator` (one tick in, new hall and events out), `ReclaimQueries` (pure read projections); `Reclaim`, `ReclaimCommand`, `BaggageEvent`, `Identifiers`, `Errors.cs`, `Result`, `Option`.
- `Messaging/`: `FlightEventsProcessor` → `ReclaimEventHandler`; `UnloadingWorker` drives ticks with `TimeProvider`; `OutboxDispatcher` → `ServiceBusBaggageEventPublisher`.
- `Controllers/` + `Models/`: HTTP edge via `ReclaimQueries` (`GET /belts`, `/belts/{id}`, `/flights/{flightId}/reclaim`, `/bags/{bagTag}`); domain errors become ProblemDetails through `ProblemResults.Problem` (400 / 404 / 409 plus `code`). OpenAPI + Scalar behind `ApiDocs:Enabled`. `POST /flights/{flightId}/reclaim/complete` (svc-baggage-reclaim#9) applies `CompleteReclaim` + a `ReclaimComplete` outbox event in one `UpdateWithEvents`, and needs the `Ops.Reclaim.Write` policy (`Auth/`, ADR-0018): Entra JWT via Microsoft.Identity.Web when `AzureAd:ClientId` is set, fake handler (`Auth:UseFake`, Development/tests) minting the same claims locally. Authorisation stays at the edge; the domain never sees callers.
- `Storage/`: `IReclaimHallStore`, in-memory implementation.

## Events

- Subscribes to `flight-events` (owned by the flight simulator). Never publish to it.
- Publishes only to its own topic, **`baggage-events`**, through the **outbox** ([ADR-0017](https://github.com/aeroflow-air/platform-handbook/blob/main/docs/decisions/0017-reliable-event-publishing.md)): the handler and `UnloadingWorker` save each hall change and its events in one `IReclaimHallStore.UpdateWithEvents`, and `OutboxDispatcher` sends them via `IBaggageEventPublisher` (MessageId = event id, Subject = event type, CorrelationId = flight id). Never call the publisher directly.
- Event ids are **deterministic**: `BaggageEvent.CausedBy(flightEventId, type)` for events caused by a flight event, `BaggageEvent.TickId(...)` for tick events. Never use `Guid.NewGuid` for an event id. Delivery is at least once.
- Handlers must be **idempotent**: Service Bus can redeliver, so applying the same event twice must not change the hall twice. Applied `EventId`s are remembered in the hall in the same atomic update as the change, so a redelivery is `Ignored`; keep that for new handlers.
- Late or out-of-order events of a known type return an `Ignored` / `Rejected` outcome with a reason; don't throw for them. An **unknown `FlightEventType`** value is different: it fails JSON deserialisation and `FlightEventsProcessor` dead-letters it as `unreadable`. Adding an event type to `flight-events` therefore needs this service updated first.

## Tests and docs

- New domain functions and handlers need unit tests in `tests/`, including failure and duplicate-delivery paths.
  Domain tests need no mocks.
- Keep `docs/service.md` current: update it in the same PR when inputs, outputs, settings or known failure
  modes (with log signatures) change.
