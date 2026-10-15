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
- `Messaging/`: `FlightEventsProcessor` → `ReclaimEventHandler`; `UnloadingWorker` drives ticks with `TimeProvider`; `ServiceBusBaggageEventPublisher`.
- `Controllers/` + `Models/`: read-only HTTP edge (`GET /belts` via `ReclaimQueries`); the rest of the API is platform-handbook#69.
- `Storage/`: `IReclaimHallStore`, in-memory implementation.

## Events

- Subscribes to `flight-events` (owned by the flight simulator). Never publish to it.
- Publishes only to its own topic, **`baggage-events`**, through `IBaggageEventPublisher` (MessageId = event id, Subject = event type, CorrelationId = flight id).
- Handlers must be **idempotent**: Service Bus can redeliver, so applying the same event twice must not change the hall twice. Applied `EventId`s are remembered in the hall in the same atomic update as the change, so a redelivery is `Ignored`; keep that for new handlers.
- Late or out-of-order events of a known type return an `Ignored` / `Rejected` outcome with a reason; don't throw for them. An **unknown `FlightEventType`** value is different: it fails JSON deserialisation and `FlightEventsProcessor` dead-letters it as `unreadable`. Adding an event type to `flight-events` therefore needs this service updated first.
- **Known gap, don't copy it:** publishing is at most once. The hall is updated before events are sent. If sending `BeltAssigned` fails, the redelivered `Landed` is `Ignored` as already applied and the event is never re-sent, even though its id is deterministic (the flight event's id). If a tick's publish fails (`UnloadingWorker`), the hall has already moved on and those events, which have random `Guid.NewGuid` ids, are lost (see `docs/service.md`). The fix (outbox or deterministic ids) is tracked in [platform-handbook#84](https://github.com/aeroflow-air/platform-handbook/issues/84); new publishing code should follow that, not this pattern.

## Tests and docs

- New domain functions and handlers need unit tests in `tests/`, including failure and duplicate-delivery paths.
  Domain tests need no mocks.
- Keep `docs/service.md` current: update it in the same PR when inputs, outputs, settings or known failure
  modes (with log signatures) change.
