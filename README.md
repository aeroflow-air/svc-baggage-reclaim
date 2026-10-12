# svc-baggage-reclaim

AeroFlow Air **Baggage Reclaim** service: assigns each arriving flight to a reclaim belt, tracks its bags (unloaded, on belt, collected, delayed, missing) and closes the reclaim when the belt is clear. Planned in [platform-handbook#44](https://github.com/aeroflow-air/platform-handbook/issues/44); created from [template-dotnet-service](https://github.com/aeroflow-air/template-dotnet-service).

## Local run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet run --project src/AeroFlow.BaggageReclaim
dotnet test
```

- Health: `GET /health`
- The belts, reclaim and bag API arrives with platform-handbook#69.

To run it with the whole airport, use the Aspire AppHost in [`aeroflow-local`](https://github.com/aeroflow-air/aeroflow-local) (clone this repo next to it).

## Domain

Pure, immutable and I/O-free (ADR-0004), in [`src/AeroFlow.BaggageReclaim/Domain`](src/AeroFlow.BaggageReclaim/Domain):

- `ReclaimHall`: the belts (each with a capacity in flights), every flight's `FlightReclaim`, and the ids of flight events already applied.
- `ReclaimPlanner.Apply(hall, command)` returns `Result<ReclaimHall>`. Commands: `AssignBelt` (least busy belt with room, ties to the first listed), `RecordDelay`, `CancelReclaim`, `MoveBag`, `CompleteReclaim` (uncollected bags become `Missing`).
- Bag moves: `Unloaded → OnBelt | Delayed | Missing`, `OnBelt → Collected | Missing`, `Delayed → OnBelt | Missing`.
- Errors are typed (`ValidationError` 400, `NotFoundError` 404, `RuleViolation` 409), e.g. `no_belt_available`, `already_assigned`, `reclaim_closed`.
- Flight numbers are a two-character airline code plus 1-4 digits (`BA123`, `U2123`), matching the simulator (aeroflow-local#6). Bag tags are the airline code plus six digits (`BA000123`).

## Flight events

Inside the AppHost the service listens to the flight simulator on the Service Bus topic `flight-events`, subscription `svc-baggage-reclaim`. The processor ([`Messaging/FlightEventsProcessor.cs`](src/AeroFlow.BaggageReclaim/Messaging/FlightEventsProcessor.cs)) uses the Aspire client (`AddAzureServiceBusClient("messaging")`) and only starts when a `messaging` connection string is configured, so `dotnet run`, the container and the tests run without Service Bus.

[`ReclaimEventHandler`](src/AeroFlow.BaggageReclaim/Messaging/ReclaimEventHandler.cs) handles flights **arriving** at the home airport (`FlightEvents:HomeAirport`, default `LHR`):

| Event | Effect |
|---|---|
| `Landed` | `AssignBelt` with a stable bag count per flight (60-180) |
| `Delayed` | `RecordDelay` if the flight already has a belt, otherwise ignored |
| `Cancelled` | `CancelReclaim` (frees the belt slot) if it has a belt, otherwise ignored |
| others | ignored |

**Idempotent:** the event id is stored in the hall in the same atomic update as the change (last 10,000 kept), so a redelivered message is ignored. Business rejections are logged as warnings and completed; an unreadable body is dead-lettered.

Settings (`FlightEvents` section): `Topic`, `Subscription`, `HomeAirport`, `MinBagsPerFlight`, `MaxBagsPerFlight`. The event contract is a local copy of `AeroFlow.Local.FlightEvents.FlightEvent`.

Publishing `baggage-events` (platform-handbook#68) and the API (#69) come next.

## Container

```bash
docker build -t svc-baggage-reclaim .
docker run --rm -p 8080:8080 svc-baggage-reclaim
```
