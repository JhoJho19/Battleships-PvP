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
        private int requestNumber;

        [SetUp]
        public void SetUp()
        {
            requestNumber = 0;
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            server = new RecordingReceiver();
            transport.RegisterServer(server);
            client = new BattleClient(() => $"request-{++requestNumber}");
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
            transport.ProcessPending();

            var request = (FireRequest)server.Deliveries.Single().Message;
            Assert.That(request.RequestId, Is.EqualTo("request-1"));
            Assert.That(request.TurnId, Is.EqualTo(9));
            Assert.That(request.Target.X, Is.EqualTo(1));
            Assert.That(request.Target.Y, Is.EqualTo(2));
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
