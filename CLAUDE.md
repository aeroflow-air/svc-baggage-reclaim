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

- `src/AeroFlow.BaggageReclaim/Domain`: pure core. `ReclaimPlanner` (pure rules, no I/O, no clock) and `UnloadingSimulator` (one tick in, new hall and events out); `Reclaim`, `ReclaimCommand`, `BaggageEvent`, `Identifiers`, `Errors.cs`, `Result`, `Option`.
- `Messaging/`: `FlightEventsProcessor` → `ReclaimEventHandler`; `UnloadingWorker` drives ticks with `TimeProvider`; `ServiceBusBaggageEventPublisher`.
- `Storage/`: `IReclaimHallStore`, in-memory implementation.

## Events

- Subscribes to `flight-events` (owned by the flight simulator). Never publish to it.
- Publishes only to its own topic, **`baggage-events`**, through `IBaggageEventPublisher`.
- Handlers must be **idempotent**: Service Bus can redeliver, so applying the same event twice must be safe. Unknown, late or out-of-order events return an `Ignored` / `Rejected` outcome with a reason; they are not thrown (that sends them to the dead-letter queue). Here applied `EventId`s are remembered in the hall in the same atomic update as the change, so a redelivery is ignored; keep that for new handlers.
- Known gap: a publish that fails mid-unload is not retried (see `docs/service.md`).

## Tests and docs

- New domain functions and handlers need unit tests in `tests/`, including failure and duplicate-delivery paths.
  Domain tests need no mocks.
- Keep `docs/service.md` current: update it in the same PR when inputs, outputs, settings or known failure
  modes (with log signatures) change.
