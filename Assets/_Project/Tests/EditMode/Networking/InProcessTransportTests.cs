using System;
using System.Collections.Generic;
using System.Linq;
using Battleships.Domain;
using Battleships.Networking;
using Battleships.Networking.Integration;
using Battleships.Protocol;
using Battleships.Server;
using NUnit.Framework;

namespace Battleships.Tests.Networking
{
    public sealed class InProcessTransportTests
    {
        private InProcessTransport transport;
        private RecordingReceiver server;
        private RecordingReceiver clientA;
        private RecordingReceiver clientB;
        private ClientTransportEndpoint endpointA;
        private ClientTransportEndpoint endpointB;

        [SetUp]
        public void SetUp()
        {
            transport = CreateTransport();
            server = new RecordingReceiver();
            clientA = new RecordingReceiver();
            clientB = new RecordingReceiver();
            transport.RegisterServer(server);
            endpointA = transport.RegisterClient(ClientEndpointId.ClientA, Settings(), clientA);
            endpointB = transport.RegisterClient(ClientEndpointId.ClientB, Settings(), clientB);
        }

        [TearDown]
        public void TearDown() => transport?.Dispose();

        [Test]
        public void ClientAAndClientBDeliverToServerThroughSeparateEndpoints()
        {
            endpointA.Send(new JoinRequest { RequestId = "a" });
            endpointB.Send(new JoinRequest { RequestId = "b" });
            transport.ProcessPending();

            Assert.That(server.Deliveries.Count, Is.EqualTo(2));
            Assert.That(server.Deliveries[0].Endpoint, Is.EqualTo(endpointA.Identity));
            Assert.That(server.Deliveries[1].Endpoint, Is.EqualTo(endpointB.Identity));
            Assert.That(((JoinRequest)server.Deliveries[0].Message).RequestId, Is.EqualTo("a"));
            Assert.That(server.Deliveries.All(x => x.Direction == TransportDirection.ClientToServer), Is.True);
        }

        [Test]
        public void ServerDeliversOnlyToSelectedClientEndpoint()
        {
            transport.Send(endpointA.Identity, new JoinResponse { RequestId = "a" });
            transport.Send(endpointB.Identity, new JoinResponse { RequestId = "b" });
            transport.ProcessPending();

            Assert.That(clientA.Deliveries.Count, Is.EqualTo(1));
            Assert.That(clientB.Deliveries.Count, Is.EqualTo(1));
            Assert.That(((JoinResponse)clientA.Deliveries[0].Message).RequestId, Is.EqualTo("a"));
            Assert.That(((JoinResponse)clientB.Deliveries[0].Message).RequestId, Is.EqualTo("b"));
            Assert.That(clientA.Deliveries[0].Direction, Is.EqualTo(TransportDirection.ServerToClient));
        }

        [Test]
        public void EndpointSettingsAreIndependent()
        {
            endpointA.Configure(Settings(loss: 1));
            endpointA.Send(new JoinRequest { RequestId = "lost-a" });
            endpointB.Send(new JoinRequest { RequestId = "received-b" });
            transport.ProcessPending();

            Assert.That(server.Deliveries.Select(x => ((JoinRequest)x.Message).RequestId),
                Is.EqualTo(new[] { "received-b" }));
            Assert.That(transport.Log.Any(x => x.Endpoint == endpointA.Identity &&
                x.Status == TransportLogStatus.Dropped && x.DropReason == TransportDropReason.Loss), Is.True);
        }

        [Test]
        public void MessagesUseSerializedCopiesInsteadOfSharedDtoReferences()
        {
            var request = new FireRequest
            {
                RequestId = "copy",
                SessionToken = "session",
                TurnId = 3,
                Target = new BoardPosition { X = 1, Y = 2 }
            };
            endpointA.Send(request);
            request.RequestId = "mutated";
            request.Target.X = 99;
            transport.ProcessPending();

            var received = (FireRequest)server.Deliveries.Single().Message;
            Assert.That(received, Is.Not.SameAs(request));
            Assert.That(received.Target, Is.Not.SameAs(request.Target));
            Assert.That(received.RequestId, Is.EqualTo("copy"));
            Assert.That(received.Target.X, Is.EqualTo(1));
        }

        [Test]
        public void XmlSerializerRoundTripsNestedProtocolData()
        {
            var serializer = new XmlMessageSerializer(ProtocolMessageTypes.All);
            var original = new MatchSnapshot
            {
                BoardSize = 6,
                PlayerSlot = PlayerSlot.PlayerTwo,
                OwnBoardCells = new[]
                {
                    new OwnBoardCell
                    {
                        Position = new BoardPosition { X = 4, Y = 5 },
                        HasShip = true,
                        HasShotResult = true,
                        ShotResult = ShotResultCode.Sunk
                    }
                },
                OpponentShots = new[]
                {
                    new OpponentShotResult
                    {
                        Position = new BoardPosition { X = 2, Y = 1 },
                        Result = ShotResultCode.Hit
                    }
                },
                StateVersion = 8
            };

            var serialized = serializer.Serialize(original);
            var copy = (MatchSnapshot)serializer.Deserialize(serialized.MessageType, serialized.Payload);

            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(copy.OwnBoardCells[0].Position.X, Is.EqualTo(4));
            Assert.That(copy.OpponentShots[0].Result, Is.EqualTo(ShotResultCode.Hit));
            Assert.That(copy.StateVersion, Is.EqualTo(8));
        }

        [Test]
        public void DelayRequiresExplicitTimeAdvance()
        {
            endpointA.Configure(Settings(latency: 100));
            endpointA.Send(new JoinRequest { RequestId = "delayed" });
            transport.ProcessPending();
            Assert.That(server.Deliveries, Is.Empty);

            transport.AdvanceTimeBy(99);
            Assert.That(server.Deliveries, Is.Empty);
            transport.AdvanceTimeBy(1);
            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
        }

        [Test]
        public void JitterIsDeterministicForASeed()
        {
            const int seed = 73;
            const double latency = 100;
            const double jitter = 25;
            var expectedDelay = latency + (new Random(seed).NextDouble() * 2 - 1) * jitter;
            endpointA.Configure(Settings(latency, jitter, seed: seed));
            endpointA.Send(new JoinRequest { RequestId = "jitter" });

            transport.AdvanceTimeBy(expectedDelay - 0.001);
            Assert.That(server.Deliveries, Is.Empty);
            transport.AdvanceTimeBy(0.001);
            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
        }

        [Test]
        public void LossDropsTheDeliveryAfterSentWasLogged()
        {
            endpointA.Configure(Settings(loss: 1));
            endpointA.Send(new JoinRequest { RequestId = "lost" });
            transport.ProcessPending();

            Assert.That(server.Deliveries, Is.Empty);
            CollectionAssert.AreEqual(
                new[] { TransportLogStatus.Sent, TransportLogStatus.Dropped },
                transport.Log.Select(x => x.Status));
            Assert.That(transport.Log.Last().DropReason, Is.EqualTo(TransportDropReason.Loss));
        }

        [Test]
        public void DuplicationCreatesTwoConcreteDeliveries()
        {
            endpointA.Configure(Settings(duplicate: 1));
            endpointA.Send(new JoinRequest { RequestId = "duplicate" });
            transport.ProcessPending();

            Assert.That(server.Deliveries.Count, Is.EqualTo(2));
            Assert.That(transport.Log.Count(x => x.Status == TransportLogStatus.Duplicated), Is.EqualTo(1));
            Assert.That(transport.Log.Count(x => x.Status == TransportLogStatus.Received), Is.EqualTo(2));
        }

        [Test]
        public void SilentDisconnectBlocksBothDirectionsWithoutAffectingOtherClient()
        {
            endpointA.Configure(Settings(disconnected: true));
            endpointA.Send(new JoinRequest { RequestId = "a-up" });
            transport.Send(endpointA.Identity, new JoinResponse { RequestId = "a-down" });
            endpointB.Send(new JoinRequest { RequestId = "b-up" });
            transport.Send(endpointB.Identity, new JoinResponse { RequestId = "b-down" });
            transport.ProcessPending();

            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
            Assert.That(server.Deliveries[0].Endpoint, Is.EqualTo(endpointB.Identity));
            Assert.That(clientA.Deliveries, Is.Empty);
            Assert.That(clientB.Deliveries.Count, Is.EqualTo(1));
            Assert.That(transport.Log.Count(x => x.Endpoint == endpointA.Identity &&
                x.Status == TransportLogStatus.Dropped &&
                x.DropReason == TransportDropReason.Disconnected), Is.EqualTo(2));
        }

        [Test]
        public void DisconnectAfterSchedulingDropsBothDirectionsAtDeliveryTime()
        {
            endpointA.Configure(Settings(latency: 50));
            endpointA.Send(new JoinRequest { RequestId = "up" });
            transport.Send(endpointA.Identity, new JoinResponse { RequestId = "down" });
            endpointA.Configure(Settings(disconnected: true));
            transport.AdvanceTimeBy(50);

            Assert.That(server.Deliveries, Is.Empty);
            Assert.That(clientA.Deliveries, Is.Empty);
            Assert.That(transport.Log.Count(x => x.Status == TransportLogStatus.Dropped &&
                x.DropReason == TransportDropReason.Disconnected), Is.EqualTo(2));
        }

        [Test]
        public void DestroyingEndpointRemovesItsPendingDeliveries()
        {
            endpointA.Configure(Settings(latency: 100));
            endpointA.Send(new JoinRequest { RequestId = "old" });
            transport.Send(endpointA.Identity, new JoinResponse { RequestId = "old" });
            Assert.That(transport.PendingCount, Is.EqualTo(2));

            endpointA.Dispose();
            Assert.That(transport.PendingCount, Is.Zero);
            transport.AdvanceTimeBy(100);
            Assert.That(server.Deliveries, Is.Empty);
            Assert.That(clientA.Deliveries, Is.Empty);
            Assert.That(transport.Log.Count(x => x.Status == TransportLogStatus.Dropped &&
                x.DropReason == TransportDropReason.StaleEndpoint), Is.EqualTo(2));
        }

        [Test]
        public void OldDeliveryCannotReachRecreatedEndpointWithNewGeneration()
        {
            endpointA.Configure(Settings(latency: 100));
            transport.Send(endpointA.Identity, new JoinResponse { RequestId = "old" });
            var oldIdentity = endpointA.Identity;
            endpointA.Dispose();

            var replacement = new RecordingReceiver();
            endpointA = transport.RegisterClient(ClientEndpointId.ClientA, Settings(), replacement);
            Assert.That(endpointA.Identity.Generation, Is.GreaterThan(oldIdentity.Generation));
            transport.AdvanceTimeBy(100);
            Assert.That(replacement.Deliveries, Is.Empty);

            transport.Send(oldIdentity, new JoinResponse { RequestId = "stale-address" });
            Assert.That(transport.Log.Last().DropReason, Is.EqualTo(TransportDropReason.StaleEndpoint));
        }

        [Test]
        public void ResetClearsQueueRegistrationsCallbacksAndLog()
        {
            endpointA.Configure(Settings(latency: 100));
            endpointA.Send(new JoinRequest { RequestId = "pending" });
            Assert.That(transport.PendingCount, Is.EqualTo(1));
            Assert.That(transport.Log, Is.Not.Empty);

            transport.Reset();

            Assert.That(transport.PendingCount, Is.Zero);
            Assert.That(transport.Log, Is.Empty);
            Assert.That(transport.CurrentTimeMilliseconds, Is.Zero);
            Assert.Throws<InvalidOperationException>(() =>
                transport.Configure(endpointA.Identity, Settings()));
        }

        [Test]
        public void DisposeClearsRuntimeAndRejectsFurtherUse()
        {
            endpointA.Configure(Settings(latency: 100));
            endpointA.Send(new JoinRequest { RequestId = "pending" });
            transport.Dispose();

            Assert.That(transport.PendingCount, Is.Zero);
            Assert.That(transport.Log, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() => transport.ProcessPending());
        }

        [Test]
        public void IntegrationAdapterCallsExistingBattleServerApiAndReturnsProtocolResponses()
        {
            transport.Dispose();
            transport = CreateTransport();
            clientA = new RecordingReceiver();
            clientB = new RecordingReceiver();
            endpointA = transport.RegisterClient(ClientEndpointId.ClientA, Settings(), clientA);
            endpointB = transport.RegisterClient(ClientEndpointId.ClientB, Settings(), clientB);
            var battleServer = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1),
                new Random(41), new ManualClock(), 15000);
            var adapter = new BattleServerTransportAdapter(battleServer, transport);
            transport.RegisterServer(adapter);

            endpointA.Send(new JoinRequest { RequestId = "join-a" });
            endpointB.Send(new JoinRequest { RequestId = "join-b" });
            transport.ProcessPending();

            var responseA = (JoinResponse)clientA.Deliveries.Single(x => x.Message is JoinResponse).Message;
            var responseB = (JoinResponse)clientB.Deliveries.Single(x => x.Message is JoinResponse).Message;
            Assert.That(responseA.RequestId, Is.EqualTo("join-a"));
            Assert.That(responseB.RequestId, Is.EqualTo("join-b"));
            Assert.That(responseA.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(responseB.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(responseA.SessionToken, Is.Not.EqualTo(responseB.SessionToken));
            Assert.That(((MatchSnapshot)clientA.Deliveries.Single(x => x.Message is MatchSnapshot).Message)
                .PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(((MatchSnapshot)clientB.Deliveries.Single(x => x.Message is MatchSnapshot).Message)
                .PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));

            clientA.Deliveries.Clear();
            endpointA.Send(new ResumeRequest { RequestId = "resume", SessionToken = responseA.SessionToken });
            endpointA.Send(new HeartbeatRequest { SessionToken = responseA.SessionToken });
            transport.ProcessPending();
            var snapshot = (MatchSnapshot)clientA.Deliveries.Single(x => x.Message is MatchSnapshot).Message;
            Assert.That(clientA.Deliveries.Single(x => x.Message is HeartbeatResponse).Message,
                Is.TypeOf<HeartbeatResponse>());

            clientA.Deliveries.Clear();
            clientB.Deliveries.Clear();
            endpointA.Send(new FireRequest
            {
                RequestId = "fire",
                SessionToken = responseA.SessionToken,
                TurnId = snapshot.TurnId,
                Target = new BoardPosition { X = 0, Y = 0 }
            });
            transport.ProcessPending();
            Assert.That(clientA.Deliveries.Single(x => x.Message is FireResponse).Message,
                Is.TypeOf<FireResponse>());
            Assert.That(clientA.Deliveries.Any(x => x.Message is MatchSnapshot), Is.True);
            Assert.That(clientB.Deliveries.Any(x => x.Message is MatchSnapshot), Is.True);
            Assert.That(((MatchSnapshot)clientB.Deliveries.Single(x => x.Message is MatchSnapshot).Message)
                .StateVersion, Is.EqualTo(snapshot.StateVersion + 1));

            clientB.Deliveries.Clear();
            endpointB.Send(new ResumeRequest { RequestId = "invalid", SessionToken = "unknown" });
            transport.ProcessPending();
            Assert.That(((ErrorResponse)clientB.Deliveries.Single().Message).ErrorCode,
                Is.EqualTo(ProtocolErrorCode.InvalidSession));
        }

        [Test]
        public void DeadlineTickBroadcastsOnlyAfterAnAutonomousTimeout()
        {
            var clock = SetUpIntegratedServer(out var adapter);
            var before = LatestSnapshot(clientA);
            clientA.Deliveries.Clear();
            clientB.Deliveries.Clear();

            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds - 1;
            Assert.That(adapter.ProcessDeadlines(), Is.False);
            transport.ProcessPending();
            Assert.That(clientA.Deliveries, Is.Empty);
            Assert.That(clientB.Deliveries, Is.Empty);

            clock.UnixTimeMilliseconds += 1;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.ProcessPending();

            var afterA = LatestSnapshot(clientA);
            var afterB = LatestSnapshot(clientB);
            Assert.That(afterA.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(afterB.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(afterA.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(afterB.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(afterB.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(afterB.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(clock.UnixTimeMilliseconds + 15000));

            clientA.Deliveries.Clear();
            clientB.Deliveries.Clear();
            Assert.That(adapter.ProcessDeadlines(), Is.False);
            transport.ProcessPending();
            Assert.That(clientA.Deliveries, Is.Empty);
            Assert.That(clientB.Deliveries, Is.Empty);
        }

        [Test]
        public void DisconnectedActiveClientDoesNotPauseTimeout()
        {
            var clock = SetUpIntegratedServer(out var adapter);
            var before = LatestSnapshot(clientA);
            clientA.Deliveries.Clear();
            clientB.Deliveries.Clear();
            endpointA.Configure(Settings(disconnected: true));

            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.ProcessPending();

            Assert.That(clientA.Deliveries, Is.Empty);
            var connectedSnapshot = LatestSnapshot(clientB);
            Assert.That(connectedSnapshot.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(connectedSnapshot.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(transport.Log.Any(x => x.Endpoint == endpointA.Identity &&
                x.Status == TransportLogStatus.Dropped &&
                x.DropReason == TransportDropReason.Disconnected), Is.True);
        }

        [Test]
        public void FireAtDeadlineCannotCreateADoubleTurnTransition()
        {
            var clock = SetUpIntegratedServer(out var adapter);
            var before = LatestSnapshot(clientA);
            var join = (JoinResponse)clientA.Deliveries.Single(x => x.Message is JoinResponse).Message;
            clientA.Deliveries.Clear();
            clientB.Deliveries.Clear();
            clock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;

            endpointA.Send(new FireRequest
            {
                RequestId = "deadline-fire",
                SessionToken = join.SessionToken,
                TurnId = before.TurnId,
                Target = new BoardPosition { X = 0, Y = 0 }
            });
            transport.ProcessPending();

            var response = (FireResponse)clientA.Deliveries.Single(x => x.Message is FireResponse).Message;
            Assert.That(response.Accepted, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(ProtocolErrorCode.TurnExpired));
            var after = LatestSnapshot(clientB);
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId + 1));
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion + 1));
            Assert.That(after.OpponentShots, Is.Empty);
            Assert.That(adapter.ProcessDeadlines(), Is.False);
        }

        private ManualClock SetUpIntegratedServer(out BattleServerTransportAdapter adapter)
        {
            transport.Dispose();
            transport = CreateTransport();
            clientA = new RecordingReceiver();
            clientB = new RecordingReceiver();
            endpointA = transport.RegisterClient(ClientEndpointId.ClientA, Settings(), clientA);
            endpointB = transport.RegisterClient(ClientEndpointId.ClientB, Settings(), clientB);
            var clock = new ManualClock();
            var battleServer = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1),
                new Random(41), clock, 15000);
            adapter = new BattleServerTransportAdapter(battleServer, transport);
            transport.RegisterServer(adapter);
            endpointA.Send(new JoinRequest { RequestId = "join-a" });
            endpointB.Send(new JoinRequest { RequestId = "join-b" });
            transport.ProcessPending();
            return clock;
        }

        private static MatchSnapshot LatestSnapshot(RecordingReceiver receiver) =>
            (MatchSnapshot)receiver.Deliveries.Last(x => x.Message is MatchSnapshot).Message;

        private static InProcessTransport CreateTransport() =>
            new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));

        private static NetworkSettings Settings(double latency = 0, double jitter = 0,
            double loss = 0, double duplicate = 0, bool disconnected = false, int seed = 0) =>
            new NetworkSettings(latency, jitter, loss, duplicate, disconnected, seed);

        private sealed class RecordingReceiver : ITransportMessageReceiver
        {
            public List<TransportDelivery> Deliveries { get; } = new List<TransportDelivery>();
            public void Receive(TransportDelivery delivery) => Deliveries.Add(delivery);
        }

        private sealed class ManualClock : IServerClock
        {
            public long UnixTimeMilliseconds { get; set; } = 1000;
        }
    }
}
