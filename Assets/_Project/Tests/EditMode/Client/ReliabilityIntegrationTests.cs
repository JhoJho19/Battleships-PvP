using System;
using System.Collections.Generic;
using System.Linq;
using Battleships.Client;
using Battleships.Domain;
using Battleships.Networking;
using Battleships.Networking.Integration;
using Battleships.Protocol;
using Battleships.Server;
using NUnit.Framework;

namespace Battleships.Tests.Client
{
    public sealed class ReliabilityIntegrationTests
    {
        private InProcessTransport transport;
        private BattleServer server;
        private BattleServerTransportAdapter adapter;
        private ManualClock serverClock;
        private ClientSessionIdentity identityA;
        private ClientSessionIdentity identityB;
        private BattleClient clientA;
        private BattleClient clientB;
        private ClientTransportEndpoint endpointA;
        private ClientTransportEndpoint endpointB;
        private int clientRequestNumber;
        private int serverReadNumber;
        private double clientTime;

        [SetUp]
        public void SetUp()
        {
            serverClock = new ManualClock { UnixTimeMilliseconds = 100000 };
            server = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1),
                new Random(104), serverClock, 15000);
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            adapter = new BattleServerTransportAdapter(server, transport);
            transport.RegisterServer(adapter);

            identityA = new ClientSessionIdentity();
            identityB = new ClientSessionIdentity();
            clientA = CreateClient(ClientEndpointId.ClientA, identityA, out endpointA);
            clientB = CreateClient(ClientEndpointId.ClientB, identityB, out endpointB);
            clientA.Join();
            clientB.Join();
            transport.ProcessPending();

            Assert.That(clientA.State.StateVersion, Is.EqualTo(1));
            Assert.That(clientB.State.StateVersion, Is.EqualTo(1));
        }

        [TearDown]
        public void TearDown()
        {
            clientA?.Dispose();
            clientB?.Dispose();
            transport?.Dispose();
        }

        [Test]
        public void LostFireRequestRetryIsTheFirstServerProcessingOfTheSameOperation()
        {
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(lossRate: 1));

            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var pending = clientA.State.PendingShot;
            var requestCount = clientRequestNumber;
            transport.ProcessPending();

            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
            AssertAuthoritativeUnchanged(before, Snapshot(identityA.SessionToken));
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.Zero);

            endpointA.Configure(new NetworkSettings());
            Assert.That(clientA.RetryPendingShot(), Is.True);
            transport.ProcessPending();

            var after = Snapshot(identityA.SessionToken);
            Assert.That(clientRequestNumber, Is.EqualTo(requestCount));
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.EqualTo(1));
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(after.OpponentShots, Has.Length.EqualTo(1));
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));
        }

        [Test]
        public void LostFireResponseRetryReturnsCachedResponseWithoutSecondMutation()
        {
            var before = Snapshot(identityA.SessionToken);
            LoseProcessedResponse(new ClientPosition(0, 0), out var pending, out var processed);
            var requestCount = clientRequestNumber;

            Assert.That(processed.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
            Assert.That(clientA.State.StateVersion, Is.EqualTo(before.StateVersion));

            endpointA.Configure(new NetworkSettings());
            Assert.That(clientA.RetryPendingShot(), Is.True);
            transport.ProcessPending();

            var afterRetry = Snapshot(identityA.SessionToken);
            Assert.That(clientRequestNumber, Is.EqualTo(requestCount));
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.EqualTo(2));
            AssertAuthoritativeUnchanged(processed, afterRetry);
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(processed.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(processed.TurnId));
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));
        }

        [Test]
        public void ProcessedRequestRetryAfterTurnChangedStillReturnsTheOriginalCachedResponse()
        {
            LoseProcessedResponse(new ClientPosition(0, 0), out var pending, out var processed);
            Assert.That(clientB.TryFire(new ClientPosition(0, 0)), Is.True);
            transport.ProcessPending();
            var afterLaterTurn = Snapshot(identityA.SessionToken);
            Assert.That(afterLaterTurn.StateVersion, Is.EqualTo(processed.StateVersion + 1));
            Assert.That(afterLaterTurn.TurnId, Is.EqualTo(processed.TurnId + 1));

            endpointA.Configure(new NetworkSettings());
            Assert.That(clientA.RetryPendingShot(), Is.True);
            transport.ProcessPending();

            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(processed.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(processed.TurnId));
            Assert.That(clientA.State.OpponentShots[pending.Target],
                Is.EqualTo(processed.OpponentShots.Single().Result));
            AssertAuthoritativeUnchanged(afterLaterTurn, Snapshot(identityA.SessionToken));
        }

        [Test]
        public void NeverReceivedRequestRetryAfterTurnChangedIsRejectedAsStale()
        {
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(lossRate: 1));
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var pending = clientA.State.PendingShot;
            transport.ProcessPending();

            serverClock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.ProcessPending();
            var afterTimeout = Snapshot(identityA.SessionToken);

            endpointA.Configure(new NetworkSettings());
            Assert.That(clientA.RetryPendingShot(), Is.True);
            transport.ProcessPending();

            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.EqualTo(1));
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots, Is.Empty);
            Assert.That(pending.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(afterTimeout.TurnId, Is.EqualTo(before.TurnId + 1));
            AssertAuthoritativeUnchanged(afterTimeout, Snapshot(identityA.SessionToken));
        }

        [Test]
        public void DuplicateFireRequestThroughTransportMutatesAuthoritativeStateOnce()
        {
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(duplicateRate: 1));

            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            transport.ProcessPending();

            var after = Snapshot(identityA.SessionToken);
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.EqualTo(2));
            Assert.That(transport.Log.Any(x => x.Endpoint == endpointA.Identity &&
                x.Direction == TransportDirection.ClientToServer &&
                x.MessageType == typeof(FireRequest).FullName &&
                x.Status == TransportLogStatus.Duplicated), Is.True);
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(after.OpponentShots, Has.Length.EqualTo(1));
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));
        }

        [Test]
        public void DuplicateAndLateFireResponsesDoNotResolveOrChangeANewerPendingOperation()
        {
            endpointA.Configure(new NetworkSettings(duplicateRate: 1));
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var firstRequestId = clientA.State.PendingShot.RequestId;
            transport.ProcessPending();
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));

            endpointA.Configure(new NetworkSettings());
            Assert.That(clientB.TryFire(new ClientPosition(0, 0)), Is.True);
            transport.ProcessPending();
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            Assert.That(clientA.TryFire(new ClientPosition(1, 0)), Is.True);
            var secondPending = clientA.State.PendingShot;

            endpointA.Configure(new NetworkSettings());
            transport.Send(endpointA.Identity, new FireResponse
            {
                RequestId = firstRequestId,
                Accepted = true,
                Target = new BoardPosition { X = 0, Y = 0 },
                Result = clientA.State.OpponentShots[new ClientPosition(0, 0)],
                CurrentPlayer = PlayerSlot.PlayerTwo,
                TurnId = 2,
                StateVersion = 2
            });
            transport.ProcessPending();

            Assert.That(clientA.State.PendingShot, Is.Not.Null);
            Assert.That(clientA.State.PendingShot.RequestId, Is.EqualTo(secondPending.RequestId));
            Assert.That(clientA.State.PendingShot.Target, Is.EqualTo(secondPending.Target));
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));

            transport.AdvanceTimeBy(100);
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(2));
        }

        [Test]
        public void RapidDoubleClickDuringFixedLatencyCreatesOneRequestAndOneMutation()
        {
            var before = Snapshot(identityA.SessionToken);
            var requestCount = clientRequestNumber;
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));

            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var pending = clientA.State.PendingShot;
            Assert.That(clientA.TryFire(new ClientPosition(1, 0)), Is.False);

            Assert.That(clientRequestNumber, Is.EqualTo(requestCount + 1));
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
            Assert.That(SentFireRequests(endpointA.Identity), Is.EqualTo(1));
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.Zero);

            transport.AdvanceTimeBy(99);
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
            AssertAuthoritativeUnchanged(before, Snapshot(identityA.SessionToken));

            transport.AdvanceTimeBy(1);
            var processed = Snapshot(identityA.SessionToken);
            Assert.That(ReceivedFireRequests(endpointA.Identity), Is.EqualTo(1));
            Assert.That(processed.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(processed.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(processed.OpponentShots, Has.Length.EqualTo(1));
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));

            transport.AdvanceTimeBy(99);
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
            transport.AdvanceTimeBy(1);

            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(processed.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(processed.TurnId));
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));
            Assert.That(clientRequestNumber, Is.EqualTo(requestCount + 1));
            Assert.That(SentFireRequests(endpointA.Identity), Is.EqualTo(1));
            AssertAuthoritativeUnchanged(processed, Snapshot(identityA.SessionToken));
        }

        [Test]
        public void NewerAndEqualSnapshotsWinOverAnOlderDelayedSnapshot()
        {
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            transport.Send(endpointA.Identity, ClientSnapshot(5, 5));
            endpointA.Configure(new NetworkSettings());
            transport.Send(endpointA.Identity, ClientSnapshot(6, 6));
            transport.Send(endpointA.Identity, ClientSnapshot(6, 6));

            transport.ProcessPending();
            Assert.That(clientA.State.StateVersion, Is.EqualTo(6));
            Assert.That(clientA.State.TurnId, Is.EqualTo(6));
            transport.AdvanceTimeBy(100);

            Assert.That(clientA.State.StateVersion, Is.EqualTo(6));
            Assert.That(clientA.State.TurnId, Is.EqualTo(6));
        }

        [Test]
        public void OldMatchingFireResponseCanResolvePendingWithoutRollingBackNewerState()
        {
            endpointA.Configure(new NetworkSettings(lossRate: 1));
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var pending = clientA.State.PendingShot;
            endpointA.Configure(new NetworkSettings());
            transport.Send(endpointA.Identity, ClientSnapshot(5, 5));
            transport.ProcessPending();

            transport.Send(endpointA.Identity, new FireResponse
            {
                RequestId = pending.RequestId,
                Accepted = true,
                Target = new BoardPosition { X = pending.Target.X, Y = pending.Target.Y },
                Result = ShotResultCode.Hit,
                CurrentPlayer = PlayerSlot.PlayerTwo,
                TurnId = 4,
                StateVersion = 4,
                TurnDeadlineUnixTimeMilliseconds = 999
            });
            transport.ProcessPending();

            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(5));
            Assert.That(clientA.State.TurnId, Is.EqualTo(5));
            Assert.That(clientA.State.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(20000));
            Assert.That(clientA.State.OpponentShots, Is.Empty);
        }

        [Test]
        public void DelayedFireAtDeadlineProducesOnlyTheTimeoutTransition()
        {
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);

            serverClock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.AdvanceTimeBy(100);
            transport.AdvanceTimeBy(100);

            var after = Snapshot(identityA.SessionToken);
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(after.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(after.OpponentShots, Is.Empty);
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(adapter.ProcessDeadlines(), Is.False);
        }

        [Test]
        public void ResumeSnapshotIsNotRolledBackByOldDelayedSnapshotOrResponse()
        {
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var oldRequestId = clientA.State.PendingShot.RequestId;
            transport.ProcessPending();
            var current = Snapshot(identityA.SessionToken);

            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            transport.Send(endpointA.Identity, ClientSnapshot(1, 1));
            transport.Send(endpointA.Identity, new FireResponse
            {
                RequestId = oldRequestId,
                Accepted = true,
                Target = new BoardPosition { X = 0, Y = 0 },
                Result = clientA.State.OpponentShots[new ClientPosition(0, 0)],
                CurrentPlayer = PlayerSlot.PlayerTwo,
                TurnId = current.TurnId,
                StateVersion = current.StateVersion,
                TurnDeadlineUnixTimeMilliseconds = current.TurnDeadlineUnixTimeMilliseconds
            });

            clientTime = 5001;
            Assert.That(clientA.CheckConnection(), Is.True);
            endpointA.Configure(new NetworkSettings());
            clientA.Resume();
            transport.ProcessPending();
            Assert.That(clientA.Connection.IsConnected, Is.True);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(current.StateVersion));

            transport.AdvanceTimeBy(100);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(current.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(current.TurnId));
            Assert.That(clientA.State.OpponentShots, Has.Count.EqualTo(1));
            Assert.That(clientA.State.PendingShot, Is.Null);
        }

        private BattleClient CreateClient(ClientEndpointId endpointId, ClientSessionIdentity identity,
            out ClientTransportEndpoint endpoint)
        {
            var client = new BattleClient(identity, 5000, () => clientTime,
                () => $"client-request-{++clientRequestNumber}");
            endpoint = transport.RegisterClient(endpointId, new NetworkSettings(), client);
            adapter.ActivateEndpoint(endpoint.Identity);
            client.AttachEndpoint(endpoint);
            return client;
        }

        private void LoseProcessedResponse(ClientPosition target, out PendingShot pending,
            out MatchSnapshot authoritative)
        {
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            Assert.That(clientA.TryFire(target), Is.True);
            pending = clientA.State.PendingShot;
            transport.AdvanceTimeBy(100);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100, silentlyDisconnected: true));
            transport.AdvanceTimeBy(100);
            authoritative = Snapshot(identityA.SessionToken);
            Assert.That(clientA.State.PendingShot, Is.SameAs(pending));
        }

        private int ReceivedFireRequests(EndpointIdentity endpoint) => transport.Log.Count(x =>
            x.Endpoint == endpoint && x.Direction == TransportDirection.ClientToServer &&
            x.MessageType == typeof(FireRequest).FullName && x.Status == TransportLogStatus.Received);

        private int SentFireRequests(EndpointIdentity endpoint) => transport.Log.Count(x =>
            x.Endpoint == endpoint && x.Direction == TransportDirection.ClientToServer &&
            x.MessageType == typeof(FireRequest).FullName && x.Status == TransportLogStatus.Sent);

        private MatchSnapshot Snapshot(string token) => server.Handle(new ResumeRequest
        {
            RequestId = "server-read-" + ++serverReadNumber,
            SessionToken = token
        }).Response;

        private static MatchSnapshot ClientSnapshot(long version, long turnId)
        {
            var cells = new List<OwnBoardCell>();
            for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
                cells.Add(new OwnBoardCell { Position = new BoardPosition { X = x, Y = y } });
            return new MatchSnapshot
            {
                PlayerSlot = PlayerSlot.PlayerOne,
                BoardSize = 6,
                OwnBoardCells = cells.ToArray(),
                OpponentShots = Array.Empty<OpponentShotResult>(),
                CurrentPlayer = PlayerSlot.PlayerOne,
                TurnId = turnId,
                StateVersion = version,
                TurnDeadlineUnixTimeMilliseconds = 20000
            };
        }

        private static void AssertAuthoritativeUnchanged(MatchSnapshot before, MatchSnapshot after)
        {
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(after.CurrentPlayer, Is.EqualTo(before.CurrentPlayer));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
            Assert.That(after.HasWinner, Is.EqualTo(before.HasWinner));
            Assert.That(after.Winner, Is.EqualTo(before.Winner));
            CollectionAssert.AreEqual(before.OwnBoardCells.Select(CellKey), after.OwnBoardCells.Select(CellKey));
            CollectionAssert.AreEqual(before.OpponentShots.Select(ShotKey), after.OpponentShots.Select(ShotKey));
        }

        private static string CellKey(OwnBoardCell cell) =>
            $"{cell.Position.X},{cell.Position.Y}:{cell.HasShip}:{cell.HasShotResult}:{cell.ShotResult}";

        private static string ShotKey(OpponentShotResult shot) =>
            $"{shot.Position.X},{shot.Position.Y}:{shot.Result}";

        private sealed class ManualClock : IServerClock
        {
            public long UnixTimeMilliseconds { get; set; }
        }
    }
}
