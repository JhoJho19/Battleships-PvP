# AGENTS.md

This file contains mandatory rules for AI agents working on the project.

`PLAN.md` describes the goals, architecture, and implementation order. This file defines constraints that must not be violated when making changes.

## 1. Before starting work

1. Read `PLAN.md`.
2. Check the `Docs` folder and read the documentation related to the current task.
3. Study the existing implementation before creating new classes, systems, or abstractions.
4. Check which tests already cover the behavior being changed.
5. Do not silently change the architecture or scope. If a deviation from `PLAN.md` is required, first describe the reason, consequences, and the minimal necessary change.

## 2. Tools

- Unity CLI - the primary interface for compilation, running tests, collecting logs, and automated checks.
- Coplay MCP - use for actions that require direct interaction with the Unity Editor.
- UniTask - use for new asynchronous code when an async approach is required.

Rules:

1. Do not install new packages, plugins, or assets unless necessary.
2. Do not replace existing working coroutines with UniTask only for the sake of consistency.
3. When working with multiple Unity instances, make changes only in the main project instance. Clone/secondary instances are used only for running and verification.
4. Do not enter Play Mode unless it is required for the current task or explicitly requested by the developer.
5. Do not edit `.unity` or `.prefab` YAML manually when the change can be safely performed through the Coplay MCP.

## 3. General change rules

1. Implement the minimal solution required by the current task and the specification.
2. Do not add functionality "just in case".
3. Do not intentionally leave the project in a broken intermediate state.
4. Do not hide unfinished functionality behind stubs that look like completed implementations.
5. Do not rewrite large working parts of the project without a clear need.
6. Prefer small, verifiable changes.
7. Update the relevant documentation when behavior changes.
8. Do not change the public protocol, snapshot format, or idempotency semantics without checking all dependent tests.
9. Do not use Singlton.

## 4. Architecture invariants

The following rules must not be violated without an explicit developer decision.

1. `Domain` must not depend on Unity, UI, or any transport implementation.
2. `Client` must not directly reference `BattleServer`, `MatchService`, server-side `MatchState`, or other server services.
3. Client <-> Server communication must happen only through protocol messages and the transport abstraction.
4. Even when running in the same process, the client must not call server gameplay methods directly.
5. The transport must pass serialized payloads. Client and Server must never share a reference to the same message or domain object.
6. The server is the single source of truth.
7. Only the server places ships, validates turn ownership and deadlines, determines `miss`, `hit`, and `sunk`, switches turns, and determines the winner.
8. The client stores only its permitted projection of the game state.
9. The client must never receive positions of unrevealed enemy ships.
10. UI reads only its own client's `ClientState` and must not access server state or another client.

## 5. Protocol invariants

1. `RequestId`, `TurnId`, and `StateVersion` serve different purposes and must not replace each other.
2. `RequestId` identifies a user operation.
3. A retry of the same request must use the same `RequestId`.
4. The server must check `RequestCache` before changing match state.
5. A duplicate `RequestId` must not call `GameRules.Fire()` again.
6. `TurnId` is used to reject actions from an outdated turn.
7. `StateVersion` is incremented only by the server.
8. The client must not apply state older than the latest state already accepted.
9. `SessionToken` identifies the player independently of the client runtime instance and transport endpoint.
10. The client must not tell the server the shot result, winner, or authoritative turn.

## 6. InProcessTransport

1. Fault simulation must exist only in the transport layer.
2. Delay, jitter, loss, and duplication logic must not be spread across `BattleClient`, `BattleServer`, or `GameRules`.
3. Each client has its own transport endpoint and network settings.
4. Silent disconnect stops message delivery for the selected client without generating a regular disconnect event.
5. The client must detect connection loss independently at the application level.
6. The network log must distinguish at least `SENT`, `RECEIVED`, `DROPPED`, and `DUPLICATED`.
7. Fault simulation should be reproducible when possible using fixed parameters or a seed.
8. Delayed messages must not be delivered to a destroyed runtime client instance.
9. The transport must not retain direct references to destroyed Unity objects.
10. Gameplay code must not depend on the transport being in-process.

## 7. Shot handling

1. One user click must create no more than one new operation.
2. While a `FireRequest` is awaiting a result, the UI must show `Pending`.
3. A pending request must block a second independent shot.
4. Retrying the same request must reuse the same `RequestId`.
5. The server must reject a shot that is out of turn, has a stale `TurnId`, is expired, has coordinates outside the board, targets an already processed cell, or is sent after the match has ended.
6. A rejected request must not change match state or consume the turn.
7. The shot result is determined only by the server.

## 8. Reconnect and client recreation

1. `BattleServer` and server-side `MatchState` must live independently of client runtime instances.
2. Destroying any client must not destroy the server or the match.
3. `SessionToken` must be stored separately from the client instance being destroyed.
4. A new client instance with a valid `SessionToken` must return to the existing player session.
5. After reconnect/recreation, the client receives an authoritative snapshot.
6. The snapshot is built separately for the specific player and must not contain hidden opponent data.
7. A snapshot must not roll back newer client state.
8. "Recreate client" must actually destroy the runtime client components/state and create them again. Recreation must not be simulated by changing a single flag.
9. It must be possible to recreate both clients sequentially while preserving server match state.
10. Client disconnect must not pause the server-side turn timer.
11. Disconnect alone must not end the match with the other player winning.

## 9. Lifecycle

1. Timers, delayed deliveries, async tasks, callbacks, and event subscriptions must have explicit cleanup or cancellation.
2. After client recreation, old callbacks must not modify the new client instance.
3. After scene reload, old callbacks must not access destroyed objects.
4. Do not suppress lifecycle errors with `try/catch` without fixing the underlying cause.
5. Use a runtime generation / instance id when necessary to reject callbacks belonging to an old runtime.
6. Scene restart must safely clean up the server, clients, transport queues, and subscriptions.

## 10. Tests

1. A networking guarantee change is considered complete only after the corresponding edge case has been verified, when the scenario can reasonably be automated.
2. Network EditMode tests must not depend on uncontrolled randomness.
3. When fixing a bug, add a reproducing test first whenever practical.
4. Do not weaken asserts just to make a test pass.
5. Do not remove tests without explaining why.
6. After changing `GameRules`, run the related domain tests.
7. After changing protocol/reliability/reconnect behavior, run the corresponding network tests.
8. Before completing a meaningful milestone, run the full EditMode test suite.

Priority network scenarios:

- duplicate `FireRequest`;
- lost `FireResponse` + retry;
- out-of-order `StateVersion`;
- reconnect + snapshot;
- fast double click / delayed response.

## 11. UI and debug panel

1. Two independent client UIs must be visible in the scene at the same time.
2. Client A UI works only with Client A.
3. Client B UI works only with Client B.
4. Debug settings are applied separately to the transport endpoint of the corresponding client.
5. The shared restart scene button may destroy the entire runtime.
6. Visual polish is secondary to correct architecture and networking guarantees.

## 12. Optional Mirror integration

Mirror is not part of the required implementation.

If `MirrorTransport` is added:

1. It must implement the same transport contract as `InProcessTransport`.
2. `Domain`, `Server`, `Client`, and protocol semantics must not be rewritten around Mirror.
3. Mirror-specific APIs must remain inside the networking infrastructure layer.
4. The real networking stack must preserve the required debug/fault semantics.
5. The base in-process implementation must not be degraded for the sake of optional Mirror integration.

