# PLAN.md §4.6 — Two Clients and Minimal UI

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

The timer formats the confirmed server deadline as a visual countdown. It never switches turns or
processes server deadlines. Server-side timeout processing remains stage 4.7.

The transport log view formats the existing `InProcessTransport.Log` entries and does not inspect
gameplay DTO contents.
