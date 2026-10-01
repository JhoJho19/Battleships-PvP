# PLAN.md §4.5 — InProcessTransport

## Assembly boundaries

`Battleships.Networking` depends only on `Battleships.Protocol`. It contains message serialization,
logical endpoints, deterministic fault simulation, delivery scheduling and transport logging.

`Battleships.Server` no longer depends on Networking. `BattleServer` keeps its existing typed
`Handle(...)` API and has no transport sender, callback or endpoint reference.

`Battleships.Networking.Integration` is the composition-side assembly that depends on Networking,
Protocol and Server. Its thin `BattleServerTransportAdapter` converts incoming transport deliveries
to existing `BattleServer.Handle(...)` calls and sends the resulting protocol response through
`IServerTransportSender`. Server application responsibilities remain in Server.

## Serialization and API

`IMessageSerializer` converts a registered protocol object to a `SerializedMessage` containing a
stable type ID and byte payload, and deserializes it at delivery. `XmlMessageSerializer` uses the
platform `XmlSerializer`; no package was added. `ProtocolMessageTypes` is the explicit allow-list of
top-level messages supported by the current project protocol.

The existing Protocol DTOs were not changed. Tests perform a real XML round trip for every current
request and response type, including nested snapshot arrays. Serialization happens when `Send` is
called, so later mutation of the original DTO cannot affect a pending delivery.

`InProcessTransport` exposes:

- one server receiver registration;
- registration of the fixed logical `ClientA` and `ClientB` endpoints;
- `ClientTransportEndpoint.Send(...)` for Client -> Server;
- `IServerTransportSender.Send(...)` for Server -> Client;
- endpoint-specific `NetworkSettings`;
- `ProcessPending()` and `AdvanceTimeBy(...)` for deterministic scheduling;
- `Reset()` and `Dispose()` for explicit cleanup.

Receivers get a `TransportDelivery` containing direction, endpoint identity, message type ID and a
deserialized protocol object. The transport never inspects gameplay fields.

## Endpoints and lifecycle

An `EndpointIdentity` contains a logical `ClientEndpointId` and monotonically increasing generation.
Re-registering a destroyed logical endpoint creates a new generation. A pending delivery contains no
DTO or receiver reference: only endpoint identity, direction, type ID, byte payload, delivery time and
a stable ordering sequence.

Disposing an endpoint removes its registration and all deliveries associated with that exact
generation. Removed deliveries are logged as stale. Sending to an old identity is dropped and cannot
reach a replacement endpoint. `Reset()` clears pending deliveries, endpoint/server registrations,
callbacks, time and log while retaining generation counters. `Dispose()` permanently clears the same
runtime state and rejects later use.

## Fault simulation and scheduling

Each endpoint owns independent settings and an independent `System.Random` initialized from its seed.
The same endpoint profile applies to both directions. The send-time order is:

1. accept and serialize the message, then log `SENT`;
2. reject a stale endpoint or active silent disconnect;
3. apply loss;
4. apply duplication and create one additional delivery when selected;
5. calculate `latency + uniform(-jitter, +jitter)` independently for each concrete delivery, clamp it
   to zero, and enqueue the serialized payload.

Endpoint validity and silent-disconnect state are checked again when a delayed delivery becomes due,
so destroying or disconnecting an endpoint after send still prevents delivery. No `Update`, coroutine,
real-time wait or Unity object is used. Tests advance the transport clock explicitly.

Silent disconnect emits no connection event. It only drops traffic for the selected endpoint in both
directions; the other endpoint is unaffected.

## Logging

Every entry contains direction, endpoint identity and generation, message type ID, transport time,
status and an optional drop reason. Status semantics are:

- `SENT`: the transport accepted and serialized the message for delivery;
- `DROPPED`: a concrete delivery was discarded by loss, silent disconnect or a stale endpoint;
- `DUPLICATED`: fault simulation created one additional concrete delivery;
- `RECEIVED`: a concrete delivery reached its registered receiver.

Drop reasons are `Loss`, `Disconnected` and `StaleEndpoint`. The logger does not parse request IDs or
other business fields.

## Verification

Transport EditMode tests cover both directions for both endpoints, endpoint independence, XML round
trip and reference isolation, delay, deterministic jitter, loss, duplication, silent disconnect in
both directions, destruction/recreation generations, queue cleanup, logging and integration through
the existing `BattleServer` API. Verification does not require Play Mode.

Verified in Unity 6000.3.10f1 on 2026-10-01: compilation completed with no Console errors, the
transport-focused EditMode suite passed 16/16 tests, and the full EditMode suite passed 64/64 tests.
Play Mode was not entered.
