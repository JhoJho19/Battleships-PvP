using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Battleships.Client;
using Battleships.Networking;
using Battleships.Protocol;
using NUnit.Framework;

namespace Battleships.Tests.Client
{
    public sealed class BattleClientTests
    {
        private InProcessTransport transport;
        private RecordingReceiver server;
        private BattleClient client;
        private ClientTransportEndpoint endpoint;
        private ClientSessionIdentity sessionIdentity;
        private int requestNumber;
        private double nowMilliseconds;

        [SetUp]
        public void SetUp()
        {
            requestNumber = 0;
            nowMilliseconds = 0;
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            server = new RecordingReceiver();
            transport.RegisterServer(server);
            sessionIdentity = new ClientSessionIdentity();
            client = new BattleClient(sessionIdentity, 5000, () => nowMilliseconds,
                () => $"request-{++requestNumber}");
            endpoint = transport.RegisterClient(ClientEndpointId.ClientA, new NetworkSettings(), client);
            client.AttachEndpoint(endpoint);
            Deliver(new JoinResponse
            {
                RequestId = "join",
                SessionToken = "session-a",
                PlayerSlot = PlayerSlot.PlayerOne
            });
            Deliver(Snapshot(PlayerSlot.PlayerOne, 4, 9));
        }

        [TearDown]
        public void TearDown()
        {
            client?.Dispose();
            transport?.Dispose();
        }

        [Test]
        public void TwoClientStatesAreIndependentAndOneSnapshotDoesNotModifyTheOther()
        {
            var one = new ClientState();
            var two = new ClientState();
            one.ApplySnapshot(Snapshot(PlayerSlot.PlayerOne, 2, 3));
            two.ApplySnapshot(Snapshot(PlayerSlot.PlayerTwo, 7, 8));

            one.ApplySnapshot(Snapshot(PlayerSlot.PlayerOne, 9, 10));

            Assert.That(one.StateVersion, Is.EqualTo(9));
            Assert.That(two.StateVersion, Is.EqualTo(7));
            Assert.That(two.TurnId, Is.EqualTo(8));
        }

        [Test]
        public void ClientProjectionCannotContainHiddenOpponentShipData()
        {
            Assert.That(typeof(ClientState).GetProperties()
                .Any(x => x.PropertyType.Name.Contains("MatchState")), Is.False);
            var opponentValueType = typeof(ClientState).GetProperty(nameof(ClientState.OpponentShots))
                .PropertyType.GetGenericArguments().Last();
            Assert.That(opponentValueType, Is.EqualTo(typeof(ShotResultCode)));
            Assert.That(opponentValueType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Any(x => x.Name.Contains("Ship")), Is.False);
        }

        [Test]
        public void OneShotCreatesOneRequestWithConfirmedTurnAndBlocksASecondShot()
        {
            Assert.That(client.TryFire(new ClientPosition(1, 2)), Is.True);
            Assert.That(client.TryFire(new ClientPosition(2, 2)), Is.False);
            Assert.That(requestNumber, Is.EqualTo(1));
            Assert.That(transport.Log.Count(x => x.Direction == TransportDirection.ClientToServer &&
                x.Status == TransportLogStatus.Sent && x.MessageType == typeof(FireRequest).FullName),
                Is.EqualTo(1));
            transport.ProcessPending();

            var request = (FireRequest)server.Deliveries.Single().Message;
            Assert.That(request.RequestId, Is.EqualTo("request-1"));
            Assert.That(request.TurnId, Is.EqualTo(9));
            Assert.That(request.Target.X, Is.EqualTo(1));
            Assert.That(request.Target.Y, Is.EqualTo(2));
        }

        [Test]
        public void RetryPendingShotResendsTheOriginalOperationWithoutGeneratingAnId()
        {
            Assert.That(client.TryFire(new ClientPosition(1, 2)), Is.True);
            var pending = client.State.PendingShot;

            Assert.That(client.RetryPendingShot(), Is.True);
            Assert.That(client.State.PendingShot, Is.SameAs(pending));
            Assert.That(pending.RequestId, Is.EqualTo("request-1"));
            Assert.That(pending.TurnId, Is.EqualTo(9));
            Assert.That(pending.Target, Is.EqualTo(new ClientPosition(1, 2)));
            Assert.That(requestNumber, Is.EqualTo(1));

            transport.ProcessPending();
            var requests = server.Deliveries.Select(x => x.Message).OfType<FireRequest>().ToArray();
            Assert.That(requests, Has.Length.EqualTo(2));
            Assert.That(requests.Select(x => x.RequestId), Is.All.EqualTo(pending.RequestId));
            Assert.That(requests.Select(x => x.TurnId), Is.All.EqualTo(pending.TurnId));
            Assert.That(requests.Select(x => $"{x.Target.X},{x.Target.Y}"), Is.All.EqualTo("1,2"));
        }

        [Test]
        public void MatchingAcceptedResponseResolvesPendingAndAppliesConfirmedResult()
        {
            client.TryFire(new ClientPosition(1, 2));
            var requestId = client.State.PendingShot.RequestId;
            Deliver(new FireResponse
            {
                RequestId = requestId,
                Accepted = true,
                Result = ShotResultCode.Hit,
                CurrentPlayer = PlayerSlot.PlayerTwo,
                TurnId = 10,
                StateVersion = 5,
                TurnDeadlineUnixTimeMilliseconds = 25000,
                Target = new BoardPosition { X = 1, Y = 2 }
            });

            Assert.That(client.State.PendingShot, Is.Null);
            Assert.That(client.State.OpponentShots[new ClientPosition(1, 2)], Is.EqualTo(ShotResultCode.Hit));
            Assert.That(client.State.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(client.State.StateVersion, Is.EqualTo(5));
        }

        [Test]
        public void RejectedResponseClearsPendingWithoutInventingAResult()
        {
            client.TryFire(new ClientPosition(1, 2));
            Deliver(new FireResponse
            {
                RequestId = client.State.PendingShot.RequestId,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.AlreadyShot,
                Target = new BoardPosition { X = 1, Y = 2 }
            });

            Assert.That(client.State.PendingShot, Is.Null);
            Assert.That(client.State.OpponentShots, Is.Empty);
            Assert.That(client.State.StateVersion, Is.EqualTo(4));
        }

        [Test]
        public void EveryNewShotUsesAUniqueRequestId()
        {
            client.TryFire(new ClientPosition(1, 2));
            var first = client.State.PendingShot.RequestId;
            Deliver(new FireResponse
            {
                RequestId = first,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.AlreadyShot
            });
            client.TryFire(new ClientPosition(2, 2));

            Assert.That(client.State.PendingShot.RequestId, Is.Not.EqualTo(first));
            Assert.That(client.State.PendingShot.RequestId, Is.EqualTo("request-2"));
        }

        [Test]
        public void StaleSnapshotCannotRollBackNewerConfirmedState()
        {
            Deliver(Snapshot(PlayerSlot.PlayerOne, 8, 13));
            Deliver(Snapshot(PlayerSlot.PlayerOne, 7, 12));

            Assert.That(client.State.StateVersion, Is.EqualTo(8));
            Assert.That(client.State.TurnId, Is.EqualTo(13));
        }

        [Test]
        public void ConfirmedSnapshotUpdatesOwnBoardAndKnownOpponentShots()
        {
            var snapshot = Snapshot(PlayerSlot.PlayerOne, 6, 11);
            snapshot.OwnBoardCells[0].HasShip = true;
            snapshot.OwnBoardCells[0].HasShotResult = true;
            snapshot.OwnBoardCells[0].ShotResult = ShotResultCode.Hit;
            snapshot.OpponentShots = new[]
            {
                new OpponentShotResult
                {
                    Position = new BoardPosition { X = 3, Y = 4 },
                    Result = ShotResultCode.Miss
                }
            };
            Deliver(snapshot);

            var own = client.State.OwnBoard[new ClientPosition(0, 0)];
            Assert.That(own.HasShip, Is.True);
            Assert.That(own.ShotResult, Is.EqualTo(ShotResultCode.Hit));
            Assert.That(client.State.OpponentShots[new ClientPosition(3, 4)], Is.EqualTo(ShotResultCode.Miss));
        }

        [Test]
        public void RemainingTimeUsesProvidedTimeAndClampsToZeroWithoutChangingTurnState()
        {
            var turnId = client.State.TurnId;
            var currentPlayer = client.State.CurrentPlayer;

            Assert.That(client.State.GetRemainingTurnMilliseconds(15000), Is.EqualTo(5000));
            Assert.That(client.State.GetRemainingTurnMilliseconds(20000), Is.Zero);
            Assert.That(client.State.GetRemainingTurnMilliseconds(25000), Is.Zero);
            Assert.That(client.State.TurnId, Is.EqualTo(turnId));
            Assert.That(client.State.CurrentPlayer, Is.EqualTo(currentPlayer));
        }

        [Test]
        public void AnyValidServerMessageRefreshesConnectionLiveness()
        {
            nowMilliseconds = 4000;
            Deliver(new HeartbeatResponse());
            nowMilliseconds = 8000;

            Assert.That(client.CheckConnection(), Is.False);
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Connected));

            Deliver(new ErrorResponse { ErrorCode = ProtocolErrorCode.InvalidRequest });
            nowMilliseconds = 12000;
            Assert.That(client.CheckConnection(), Is.False);
        }

        [Test]
        public void SilentDisconnectIsDetectedOnlyAfterTimeoutAndBlocksGameplay()
        {
            endpoint.Configure(new NetworkSettings(silentlyDisconnected: true));

            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Connected));
            Assert.That(client.TryFire(new ClientPosition(1, 2)), Is.True);
            nowMilliseconds = 5000;
            Assert.That(client.CheckConnection(), Is.False);
            nowMilliseconds = 5001;

            Assert.That(client.CheckConnection(), Is.True);
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.ConnectionLost));
            Assert.That(client.TryFire(new ClientPosition(2, 2)), Is.False);
        }

        [Test]
        public void HeartbeatUsesNormalTransportPathAndRetainedSessionToken()
        {
            Assert.That(client.SendHeartbeat(), Is.True);
            transport.ProcessPending();

            var heartbeat = (HeartbeatRequest)server.Deliveries.Single().Message;
            Assert.That(heartbeat.SessionToken, Is.EqualTo("session-a"));
            Assert.That(sessionIdentity.SessionToken, Is.EqualTo("session-a"));
        }

        [Test]
        public void ResumeKeepsPendingUntilValidSnapshotSynchronizes()
        {
            Assert.That(client.TryFire(new ClientPosition(1, 2)), Is.True);
            var pending = client.State.PendingShot;

            client.Resume();
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Resuming));
            Assert.That(client.State.PendingShot, Is.SameAs(pending));
            Assert.That(client.TryFire(new ClientPosition(2, 2)), Is.False);
            transport.ProcessPending();
            var resume = server.Deliveries.Select(x => x.Message).OfType<ResumeRequest>().Single();
            Assert.That(resume.SessionToken, Is.EqualTo("session-a"));

            Deliver(Snapshot(PlayerSlot.PlayerOne, 3, 8));
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Resuming));
            Assert.That(client.State.PendingShot, Is.SameAs(pending));

            Deliver(Snapshot(PlayerSlot.PlayerOne, 4, 9));
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Connected));
            Assert.That(client.State.PendingShot, Is.Null);
            Assert.That(client.State.StateVersion, Is.EqualTo(4));
        }

        [Test]
        public void ResumeTimesOutWithoutAddingAutomaticRetry()
        {
            client.Resume();
            transport.ProcessPending();
            server.Deliveries.Clear();
            nowMilliseconds = 5001;

            Assert.That(client.CheckConnection(), Is.True);
            Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.ConnectionLost));
            Assert.That(server.Deliveries, Is.Empty);
        }

        [Test]
        public void DelayedValidResumeSnapshotSynchronizesEvenAfterLivenessTimeout()
        {
            client.TryFire(new ClientPosition(1, 2));
            client.Resume();
            nowMilliseconds = 5001;
            client.CheckConnection();
            Assert.That(client.State.PendingShot, Is.Not.Null);
            Deliver(Snapshot(PlayerSlot.PlayerOne, 4, 9));
            Assert.That(client.Connection.IsConnected, Is.True);
            Assert.That(client.State.PendingShot, Is.Null);
        }

        [Test]
        public void LostConnectionBlocksNewShotWithNoPendingOperation()
        {
            nowMilliseconds = 5001;
            client.CheckConnection();
            Assert.That(client.State.PendingShot, Is.Null);
            Assert.That(client.TryFire(new ClientPosition(1, 2)), Is.False);
            Assert.That(server.Deliveries, Is.Empty);
        }

        [Test]
        public void RegularSnapshotsAndEqualVersionsKeepConnectionAliveAndNeverRollBack()
        {
            for (var i = 1; i <= 5; i++)
            {
                nowMilliseconds = i * 4000;
                Deliver(Snapshot(PlayerSlot.PlayerOne, 4 + i, 9 + i));
                Assert.That(client.CheckConnection(), Is.False);
            }
            client.Resume();
            Deliver(Snapshot(PlayerSlot.PlayerOne, 9, 14));
            Deliver(Snapshot(PlayerSlot.PlayerOne, 8, 13));
            Assert.That(client.Connection.IsConnected, Is.True);
            Assert.That(client.State.StateVersion, Is.EqualTo(9));
            Assert.That(client.State.TurnId, Is.EqualTo(14));
        }

        private void Deliver(object message)
        {
            transport.Send(endpoint.Identity, message);
            transport.ProcessPending();
        }

        private static MatchSnapshot Snapshot(PlayerSlot recipient, long version, long turnId)
        {
            var cells = new List<OwnBoardCell>();
            for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
                cells.Add(new OwnBoardCell { Position = new BoardPosition { X = x, Y = y } });
            return new MatchSnapshot
            {
                PlayerSlot = recipient,
                BoardSize = 6,
                OwnBoardCells = cells.ToArray(),
                OpponentShots = new OpponentShotResult[0],
                CurrentPlayer = PlayerSlot.PlayerOne,
                StateVersion = version,
                TurnId = turnId,
                TurnDeadlineUnixTimeMilliseconds = 20000
            };
        }

        private sealed class RecordingReceiver : ITransportMessageReceiver
        {
            public List<TransportDelivery> Deliveries { get; } = new List<TransportDelivery>();
            public void Receive(TransportDelivery delivery) => Deliveries.Add(delivery);
        }
    }
}
