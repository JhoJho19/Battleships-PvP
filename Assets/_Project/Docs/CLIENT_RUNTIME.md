# PLAN.md §4.6 and §4.7 — Two Clients, Minimal UI and Turn Timer

## Client projection

`BattleClient` owns one `ClientTransportEndpoint`, one `ClientState`, request ID creation, one pending
shot and concise client events. It sends protocol requests only through the endpoint and implements
`ITransportMessageReceiver` for messages addressed to that endpoint. It has no Server or Domain
reference.

`ClientState` stores only the player's identity, own-board projection, confirmed opponent shot
results, turn/status/version/deadline/winner values and the current pending shot. Snapshots are deep
copied into client-owned structures. A snapshot with an older `StateVersion` is ignored. A rejected
matching `FireResponse` clears pending without creating a shot result.

`ClientConnectionMonitor` represents only endpoint registration in this stage. Heartbeats, silent
disconnect detection, reconnect and resume recovery are not implemented.

## Server push snapshots

`BattleServer.GetSnapshot(sessionToken)` is a read-only application API. It resolves the session and
returns only the recipient-specific `MatchSnapshot` produced by the existing `SnapshotBuilder`; it
does not expose `MatchState`.

`BattleServerTransportAdapter` retains the infrastructure association from endpoint identity to
session token. After the second successful join and after an accepted fire operation it requests a
separate snapshot for each registered session and sends it to the corresponding endpoint. The
shooting endpoint still receives its normal `FireResponse` first. Session ownership and gameplay
decisions remain in Server.

## Presentation and composition

`BattleRuntimeComposition` creates one Server, adapter and `InProcessTransport`, then registers two
independent clients with normal zero-fault settings. Client requests schedule transport processing no
earlier than the next Unity tick. This keeps Pending observable without adding UI delay to Client or
Server. The composition owns and disposes all runtime registrations.

`ClientView`, `BoardView`, `CellView`, `ServerStatusView` and `TransportLogView` have separate
presentation responsibilities. Four runtime cell containers overlay the existing board regions in
the two prepared `PlayerWindow` images. Cells are generated from configured board size; the scene
does not contain manually authored gameplay cells.

Cell states reuse the prepared sprites: `ui_atlas_020` (unknown), `ui_atlas_021` (pending),
`ui_atlas_023`/`024` (hover/pressed), and `Ship`, `Miss`, `Hit`, `Sunk`. Opponent boards never render
`Ship`.

The timer formats the confirmed absolute server deadline as a visual countdown. `ClientState`
calculates `max(0, deadline - now)` from a caller-supplied time, so the calculation is deterministic
and reads no system clock itself. It never validates a shot, switches turns or processes server
deadlines.

Stage 4.7 adds a separate cancellable runtime loop that checks the authoritative server deadline
periodically. It does not wait for a fixed 15-second delay: an accepted shot may replace the absolute
deadline at any time. When a real timeout transition occurs, the server adapter sends a newly built
personalized snapshot to both registered clients. A silently disconnected endpoint may drop its
copy, but cannot pause or reset the server timer.

## Verification

Verified in Unity 6000.3.10f1 on 2026-10-01: the affected Server/Networking/Client EditMode
assemblies passed 54/54 tests and the full EditMode suite passed 77/77 tests. In Play Mode, after the
two initial joins and with no gameplay request, the server advanced the turn and both client states
received the same updated turn/version/deadline projection. The Console contained no errors.

The transport log view formats the existing `InProcessTransport.Log` entries and does not inspect
gameplay DTO contents.

## Stage 4.8 debug controls

The two existing `ClientDebug` hierarchies are bound by separate `ClientDebugView` instances. Each
view owns Endpoint, Connected, Last Request Id and Recent Events plus the prepared latency, jitter,
loss, duplicate, Network Log, Disconnect, Connect and Recreate Client controls. Recent Events remains
client-local and is not populated from the shared transport log.

`ClientDebugController` validates the text inputs, clamps milliseconds to `>= 0`, clamps percentage
inputs to `0..100`, converts percentages to transport rates in `0..1` and replaces the immutable
settings of only its own endpoint. Empty and non-numeric values become zero. Disconnect and Connect
only toggle `SilentlyDisconnected`; they do not unregister the endpoint, alter the server session or
recover previously dropped messages. The Connected display describes this manual delivery switch,
not heartbeat state.

Runtime transport time advances through a cancellable UniTask pump only while deliveries are pending.
This makes configured latency and jitter observable without a permanent `Update` loop. Scene restart
explicitly cancels runtime tasks, unbinds views, disposes clients, registrations and transport queues,
then reloads the active scene.

The Recreate Client buttons call the composition-level recreation entry point and show that recovery
is reserved for stage 4.9. They intentionally do not destroy or replace the client yet because correct
recreation requires independent `SessionToken` storage and Resume recovery.

Stage 4.8 verification in Unity 6000.3.10f1 passed the controller-focused EditMode suite 4/4 and the
full EditMode suite 81/81. Play Mode verified Pending under latency, jittered delivery, real loss and
duplicate log entries, endpoint-local silent disconnect/connect, continued server deadlines and the
other endpoint, endpoint-local log suppression/resumption, and scene restart with a pending delayed
delivery. The new runtime initialized cleanly and no old delivery reached it. No gameplay/lifecycle
errors were logged; the final Play Mode run exited with no Console errors or warnings.

## Stage 4.9 reconnect and client recreation

`ClientSessionIdentity` owns `SessionToken` and the stable player slot outside the disposable
`BattleClient`, `ClientState` and `ClientConnectionMonitor`. The composition owns one identity holder
per logical client for the lifetime of the server runtime; it is neither static nor persisted.

`ClientConnectionMonitor` owns the explicit `Connected`, `ConnectionLost` and `Resuming` states.
Every valid Server -> Client protocol message refreshes its last-message time. The runtime uses the
existing `GameConfig` values: one heartbeat per second and connection loss after five seconds without
a server message. Heartbeat is sent through the normal serialized, fault-enabled endpoint. Gameplay
requests are rejected inside `BattleClient` unless the monitor is `Connected`.

Disconnect still changes only `SilentlyDisconnected`. Connect restores delivery on the same endpoint,
moves the monitor to `Resuming` and sends `ResumeRequest` with the retained token. It does not Join or
clear a pending shot. Only a personalized resume snapshot that passes the existing
`incoming StateVersion >= local StateVersion` check reconciles the state, clears pending and returns
the monitor to `Connected`. Older snapshots remain ignored. No shot or Resume retry is synthesized.
The in-flight resume intent survives a liveness timeout, so a valid delayed snapshot can still
synchronize after the monitor has entered `ConnectionLost`. Receiving an ordinary heartbeat alone
never marks a lost/resuming client synchronized. Before the initial Join has established a token,
Connect can restore delivery but cannot Resume, and recreation is unavailable.

Recreation cancels the old heartbeat token, removes callbacks and UI bindings, unregisters the old
endpoint, disposes the old client runtime, registers a higher endpoint generation, constructs fresh
client state/monitor/presentation bindings, enables delivery and sends Resume. Latency, jitter, loss,
duplication, random seed and the per-endpoint log setting survive recreation. The existing UI hierarchy
is rebound rather than rebuilt.

`BattleServerTransportAdapter` associates a server session with logical `ClientEndpointId` plus the
currently active `EndpointIdentity`. Activating a higher generation removes the stale routing entry;
messages and snapshot broadcasts are accepted only for the active generation. The underlying
`BattleServer`, match, sessions, turn, version and deadline are not recreated or reset.

### Stage 4.9 verification (2026-10-02)

Unity 6000.3.10f1 compiled the implementation without errors. The targeted Client, Networking and
Presentation EditMode assemblies passed 49/49; the final full EditMode suite passed 98/98.
Deterministic tests cover liveness, heartbeat routing, resume, retained pending operations,
equal/newer/older versions, delayed resume after timeout, old generation callbacks, disposed clients,
both sequential recreation orders, server deadlines and a processed shot whose response was lost.

Play Mode verification used the existing controls through their actual Button.onClick/InputField
events via editor automation, with runtime observations read through reflection. Silent disconnect
initially left the application `Connected`, then became `ConnectionLost`; gameplay was blocked and
the other client/server continued through authoritative timeouts. Connect synchronized missed turns
on the same client/endpoint. Recreation of each client and sequential recreation in both orders
preserved tokens and the same server instance. Immediate before/after recreation checks confirmed
unchanged StateVersion, TurnId and deadline, a cancelled old heartbeat token, cleared callbacks and
receiver, fresh runtime objects, higher generation, retained UI cells and preserved latency/jitter.

With 100% loss, Resume was logged as dropped by normal transport loss and Fire was rejected during
Resuming. Restoring loss to zero and explicitly pressing Connect synchronized over configured
latency. A UI shot followed by silent disconnect retained Pending through ConnectionLost and Connect;
the accepted resume snapshot reconciled it without fabricating or resending a shot. The processed-shot
/lost-response case is additionally covered by deterministic integration tests. Console contained
zero errors and warnings during verification and after exiting Play Mode. Temporary background
execution used for editor automation was restored; no scene or prefab was saved or redesigned.

### Files changed for 4.9

- Added: `Scripts/Client/ClientSessionIdentity.cs`,
  `Tests/EditMode/Client/ReconnectIntegrationTests.cs` and their Unity metadata.
- Client: `BattleClient.cs`, `ClientState.cs`, `ClientConnectionMonitor.cs`.
- Composition/routing: `Runtime/BattleRuntimeComposition.cs`,
  `Networking/Integration/BattleServerTransportAdapter.cs`.
- Existing presentation: `ClientDebugController.cs`, `ClientDebugView.cs`, `ClientView.cs`,
  `BoardView.cs` (rebind existing cells).
- Tests: `BattleClientTests.cs`, `Battleships.Client.Tests.asmdef`,
  `ClientDebugControllerTests.cs`.
- Documentation: `CLIENT_RUNTIME.md`, `DOCUMENTATION.md`.

No automatic Resume retry, disconnect forfeit, disk persistence, restart persistence or additional
4.10 reliability mechanism was added. With a lost Resume and no synchronizing snapshot, the user
can press Connect again. Liveness uses monotonic runtime time, checked on the one-second heartbeat
schedule; the five-second threshold is therefore detected on the first check after it expires.

## Stage 4.10 duplicate, loss and out-of-order guarantees

The existing server and transport architecture already supplies the authoritative guarantees. A
`FireRequest` is looked up in `RequestCache` before deadline or turn validation, so an operation that
was already processed returns its original detached `FireResponse` even after the turn changes. It
does not call gameplay rules, advance a deadline or increment `TurnId`/`StateVersion` again. A request
that never reached the server has no cache entry; if its original `TurnId` is stale when it is retried,
normal authoritative validation rejects it without changing the match. Transport loss never invokes
server gameplay code, and duplicate delivery continues to use the real serialized transport path.

`PendingShot` now retains the complete replayable user operation identity: its `RequestId`, original
`TurnId` and target. `BattleClient.RetryPendingShot()` is an explicit application-level retry. It is
available only while the client is connected and a shot is pending, reconstructs the same
`FireRequest`, does not call the request ID factory and does not replace the pending object. There is
no automatic retry policy and no new UI control, ACK protocol or reliable transport layer.

Client response handling remains request- and version-gated. A `FireResponse` can resolve only the
currently matching pending `RequestId`; later duplicates cannot affect a subsequent pending shot. If
a matching response is older than confirmed client state, it may resolve pending but cannot apply its
shot result, turn, deadline or version. Snapshots with a lower `StateVersion` remain ignored, equal
versions are safe to reapply, and newer versions advance the confirmed projection. Resume keeps the
existing endpoint-generation protection and authoritative snapshot reconciliation.

### Stage 4.10 verification (2026-10-02)

The deterministic reliability integration suite passed 10/10 and the complete EditMode suite passed
109/109 in Unity 6000.3.10f1. Coverage includes lost request then first processing on retry, processed
request plus lost response and cached retry, both retry branches after a turn change, duplicate
request/response delivery, stale and equal snapshots, an old matching response after newer confirmed
state, rapid clicks, the deadline boundary and Resume followed by delayed old traffic. Assertions
cover board projections, current player, `TurnId`, `StateVersion`, deadline, pending state, transport
delivery counts and unchanged authoritative state.

Play Mode used the existing debug controls. With Duplicate at 100%, one logical shot produced a
`DUPLICATED` transport entry and two server deliveries while increasing authoritative state once;
the rapid second cell click did not create another pending operation. A lost request remained pending
and did not run gameplay logic. High latency/jitter left delayed traffic in the queue; Resume applied
the current snapshot, and later processing of old traffic did not roll back the client. A controlled
lost-response scenario kept the same pending `RequestId`/`TurnId`; explicit runtime retry resolved it
from the cache while the server version remained unchanged after the first processing. The Console
contained no errors or warnings before or after leaving Play Mode. No scene or prefab was saved.

### Files changed for 4.10

- Client: `BattleClient.cs`, `ClientState.cs`.
- Tests: `BattleClientTests.cs`, new `ReliabilityIntegrationTests.cs` and metadata.
- Documentation: `CLIENT_RUNTIME.md`, `DOCUMENTATION.md`.

Protocol DTOs, Server, `RequestCache`, `InProcessTransport`, the integration adapter, runtime
composition and UI were not changed for stage 4.10.

## Stage 4.11 EditMode test coverage

Stage 4.11 consolidates the deterministic automated coverage created by the preceding milestones.
It does not introduce another test-only architecture or change production behavior.

| Requirement | Deterministic EditMode coverage |
| --- | --- |
| `miss`, `hit`, `sunk` | `GameRulesTests.MissRecordsResultAndPassesTurn`, `HitDoesNotGrantAnotherTurn`, `LastCellOfShipReturnsSunkAndPassesTurn`, `SingleCellShipIsSunkImmediately` |
| Turn changes after hit and miss | `MissRecordsResultAndPassesTurn`, `HitDoesNotGrantAnotherTurn` |
| Win after the final enemy ship cell | `SinkingEntireEnemyFleetWinsAndRejectsFurtherShots` for both players |
| Out-of-turn rejection | `WrongPlayerShotLeavesBothBoardsUnchanged` and server rejection coverage |
| Valid server placement | exact fleet assertions plus straight, in-bounds, non-overlapping and non-touching placement across 64 fixed seeds |
| Duplicate `FireRequest` | real serialized duplication path in `DuplicateFireRequestThroughTransportMutatesAuthoritativeStateOnce` |
| Lost `FireResponse` and retry | `LostFireResponseRetryReturnsCachedResponseWithoutSecondMutation` |
| Out-of-order `StateVersion` | delayed older/equal/newer snapshot and old matching response coverage in `ReliabilityIntegrationTests` |
| Reconnect and snapshot | same-runtime Resume, recreation, missed timeout and processed-pending-shot cases in `ReconnectIntegrationTests` |
| Fast double click with delayed response | `RapidDoubleClickDuringFixedLatencyCreatesOneRequestAndOneMutation` |

The stage 4.11 addition uses a fixed 100 ms transport latency. Before the first request reaches the
server, a second `TryFire()` is rejected locally. The test asserts one generated request identity,
one transport send, one concrete server delivery, one authoritative board/version/turn mutation,
continued pending state until the delayed response is due, and one final confirmed client result.

### Stage 4.11 verification (2026-10-02)

Unity 6000.3.10f1 compiled without errors. Explicit `GameRulesTests` passed 18/18, the reliability
integration suite passed 11/11, and the complete EditMode suite passed 110/110. The first filtered
Domain assembly run did not start within the Unity runner's 120-second initialization window; the
same tests were then run by their fully qualified class name and passed. Console review and
`git diff --check` completed without project errors. Play Mode was not entered because this milestone
is exclusively deterministic EditMode coverage.

Only `ReliabilityIntegrationTests.cs`, `CLIENT_RUNTIME.md` and `DOCUMENTATION.md` changed for stage
4.11. Domain, Protocol, Server, Client, Transport, runtime composition and UI production code remain
unchanged.
