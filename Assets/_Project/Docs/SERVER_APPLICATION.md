# PLAN.md §4.4 and §4.7 — Server Application Layer and Turn Timer

## Responsibilities and API

`BattleServer` owns one two-player match and coordinates pure C# application calls:

- `ServerResult<JoinResponse> Handle(JoinRequest)`
- `ServerResult<MatchSnapshot> Handle(ResumeRequest)`
- `FireResponse Handle(FireRequest)`
- `FireHandlingResult HandleWithStateChange(FireRequest)` for the transport adapter
- `ServerResult<HeartbeatResponse> Handle(HeartbeatRequest)`
- `bool ProcessDeadlines()`

`ServerResult<T>` has either a non-null `Response` or a non-null `ErrorResponse`.
The wrapper belongs to Server; Protocol DTOs are unchanged. The caller must serialize application
calls; this stage adds no parallel dispatch, scheduling, endpoints, callbacks or delivery.

`MatchService` privately owns the complete Domain match plus application metadata and coordinates
`GameRules.Fire` and `GameRules.ExpireTurn`. The only Domain addition is the latter operation:
it changes the current player unless a winner already exists. Domain owns the turn transition rule;
Server owns time and decides when to invoke it.

`SessionManager` issues opaque random session tokens, maps tokens to stable player identity and an
internal match ID, and reserves Player One for the first join. It stores no transport identity.
The second join creates the match and starts the first deadline. Before that, resume/fire returns
`MatchNotReady`. A third independent join returns `InvalidRequest`.

Initial join has no player/session identity in the existing protocol. Consequently its `RequestId`
must be unique among initial joins in this server runtime (e.g. a generated UUID). A retry returns
the original token/slot and cannot reserve a second slot. This is bootstrap correlation, not
authentication. Resume resolves a token and always returns a fresh snapshot; heartbeat validates the
token and returns the existing empty acknowledgment. Neither advances authoritative state.

## Request cache and ownership

`RequestCache` stores all Fire results with a valid session and non-empty request ID, including
rejections, keyed by `(SessionToken, RequestId)`. Lookup precedes turn/deadline checks and Domain calls.
Retries always return the first result, even after later operations or changes to request content.
Changing the payload of a retry does not create a new operation.

Cache entries are retained for the current match/session runtime; there is no persistence or
distributed cache, and no last-request-only eviction. Successful and rejected responses are copied
both into and out of the cache, including their mutable target position. Returned protocol DTOs
cannot mutate server state or cached responses. Invalid sessions/empty IDs are not cached.

## Versions and deadlines

The match starts at `StateVersion = 1`, `TurnId = 1`, deadline = server time + turn duration.

| Operation | StateVersion | TurnId | Deadline |
| --- | --- | --- | --- |
| Accepted non-terminal shot | +1 | +1 | server processing time + duration |
| Terminal shot | +1 | unchanged | retained; inactive after victory |
| Processed timeout | +1 | +1 | server processing time + duration |
| Rejected/stale/duplicate request or state read | unchanged | unchanged | unchanged |

`IServerClock.UnixTimeMilliseconds` is injected. `SystemServerClock` uses a UTC origin and a
monotonic Stopwatch; deterministic tests use a manually advanced clock. Duration must be positive.
Deadline checks use `now >= deadline`. `MatchService` has one timeout-transition mechanism shared by
`ProcessDeadlines()` and new Fire request processing. A new request first brings the authoritative
turn up to date, then performs turn/player/shot validation. A request for the turn that expired at
that boundary returns `TurnExpired`; the board is not mutated. `ProcessDeadlines()` reports whether
an actual transition occurred and does nothing before readiness, before the deadline, or after a win.

`RequestCache` lookup still happens before this processing. A duplicate request returns its original
cached response and cannot advance an expired turn. `FireHandlingResult.StateChanged` lets the
transport adapter distinguish a real shot/timeout transition from a cached or rejected operation.

Each call processes at most the current expired turn and creates a fresh 15-second deadline from the
current server time. A late invocation does not retroactively synthesize turns; repeated calls at
that time do nothing until the new deadline.

In stage 4.7 the runtime calls the adapter's deadline processing from a cancellable UniTask loop.
The adapter broadcasts personalized snapshots only when a real authoritative transition occurred.
This loop is independent of client requests and client connection state.

## Snapshot safety

`SnapshotBuilder` creates a detached player-specific `MatchSnapshot`. OwnBoardCells contains the
recipient's full own board and received shot results. OpponentShots is constructed only from cells
with an actual recorded shot and its element type has only Position/Result. No opponent fleet,
HasShip flags, complete MatchState, Board or Ship object is returned.

## Verification

Server EditMode tests cover acceptance, duplicate/retry of older requests, cache isolation between
sessions, cached rejection, mutable DTO isolation, invalid/stale/out-of-turn/repeated shots, typed
results, join/bootstrap/session semantics, player projections, structural opponent data limits,
exact deadline boundary, timeout renewal and terminal-shot version/deadline behavior.
Focused Domain tests cover ExpireTurn, including a completed match and null input.
Existing game-rule tests are reused. No Play Mode, transport, client, UI or later stage is added.

Verified in Unity 6000.3.10f1 on 2026-10-01: compilation completed with no Console errors,
the affected Server/Networking/Client EditMode assemblies passed 54/54 tests, and the full EditMode
suite passed 77/77 tests. A short Play Mode integration check observed autonomous turn/version
changes without gameplay requests and matching authoritative state on both clients. No packages were
installed.
