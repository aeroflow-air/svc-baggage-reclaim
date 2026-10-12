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

Inside the AppHost the service listens to the flight simulator on the Service Bus topic `flight-events`, subscription `baggage-reclaim`. The processor ([`Messaging/FlightEventsProcessor.cs`](src/AeroFlow.BaggageReclaim/Messaging/FlightEventsProcessor.cs)) uses the Aspire client (`AddAzureServiceBusClient("messaging")`) and only starts when a `messaging` connection string is configured, so `dotnet run`, the container and the tests run without Service Bus.

[`ReclaimEventHandler`](src/AeroFlow.BaggageReclaim/Messaging/ReclaimEventHandler.cs) handles flights **arriving** at the home airport (`FlightEvents:HomeAirport`, default `LHR`):

| Event | Effect |
|---|---|
| `Landed` | `AssignBelt` with a stable bag count per flight (60-180) |
| `Delayed` | `RecordDelay` if the flight already has a belt, otherwise ignored |
| `Cancelled` | `CancelReclaim` (frees the belt slot) if it has a belt, otherwise ignored |
| others | ignored |

**Idempotent:** the event id is stored in the hall in the same atomic update as the change (last 10,000 kept), so a redelivered message is ignored. Business rejections are logged as warnings and completed; an unreadable body is dead-lettered.

Settings (`FlightEvents` section): `Topic`, `Subscription`, `HomeAirport`, `MinBagsPerFlight`, `MaxBagsPerFlight`. The event contract is a local copy of `AeroFlow.Local.FlightEvents.FlightEvent`.

## Baggage events (published)

After `Landed`, an [`UnloadingWorker`](src/AeroFlow.BaggageReclaim/Messaging/UnloadingWorker.cs) ticks the pure [`UnloadingSimulator`](src/AeroFlow.BaggageReclaim/Domain/UnloadingSimulator.cs): once the first-bag time has passed (plus any recorded delay), bags move from the hold to the belt, passengers collect them, and the reclaim closes when the belt is empty. Each step is published to the Service Bus topic **`baggage-events`**. The JSON body is a [`BaggageEvent`](src/AeroFlow.BaggageReclaim/Domain/BaggageEvent.cs), `Subject` is the event type, `CorrelationId` the flight id and `MessageId` the event id.

| Event | When | Extra fields |
|---|---|---|
| `BeltAssigned` | the `Landed` event is applied (event id = the flight event's id) | |
| `BagsOnBelt` | first bag reaches the belt | `bagCount` |
| `BagDelayed` | a bag didn't make it (`DelayedBagProbability`) | `bagTag`, `reason` |
| `LastBagOnBelt` | nothing left in the hold | |
| `ReclaimComplete` | belt empty; uncollected or delayed bags become missing | `bagCount`, `bagsCollected`, `bagsMissing` |

Events are only published when the `messaging` connection exists. Without it the simulation still runs in memory and nothing leaves the process.

Settings (`Baggage` section, e.g. `Baggage__TickSeconds=1`): `Topic` (`baggage-events`), `SimulateUnloading` (true), `TickSeconds` (2), `FirstBagAfterLandingSeconds` (10), `BagsPerTick` (12), `CollectedPerTick` (10), `DelayedBagProbability` (0.01) and `Seed` (random). With the defaults, a 120-bag flight goes from landing to `ReclaimComplete` in about 35 seconds.

The API (#69) comes next.

## Container

```bash
docker build -t svc-baggage-reclaim .
docker run --rm -p 8080:8080 svc-baggage-reclaim
```
