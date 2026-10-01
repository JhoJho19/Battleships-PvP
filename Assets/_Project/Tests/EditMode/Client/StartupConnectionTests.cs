using System;
using System.Linq;
using Battleships.Client;
using Battleships.Domain;
using Battleships.Networking;
using Battleships.Networking.Integration;
using Battleships.Presentation;
using Battleships.Protocol;
using Battleships.Server;
using NUnit.Framework;

namespace Battleships.Tests.Client
{
    public sealed class StartupConnectionTests
    {
        private InProcessTransport transport;
        private BattleServer server;
        private BattleServerTransportAdapter adapter;
        private Clock clock;
        private readonly ClientSessionIdentity[] identities = new ClientSessionIdentity[2];
        private readonly BattleClient[] clients = new BattleClient[2];
        private readonly ClientTransportEndpoint[] endpoints = new ClientTransportEndpoint[2];
        private readonly ClientDebugController[] controls = new ClientDebugController[2];
        private int requestNumber;
        private double clientTime;

        [SetUp]
        public void SetUp()
        {
            requestNumber = 0;
            clientTime = 0;
            clock = new Clock { UnixTimeMilliseconds = 100000 };
            server = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1), new Random(104), clock);
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            adapter = new BattleServerTransportAdapter(server, transport);
            transport.RegisterServer(adapter);
            for (var i = 0; i < 2; i++)
            {
                identities[i] = new ClientSessionIdentity();
                Create(i);
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < 2; i++)
            {
                controls[i]?.Dispose();
                clients[i]?.Dispose();
            }
            transport?.Dispose();
        }

        [Test]
        public void StartupHasTwoDisconnectedRuntimesWithoutSessionsOrDeadline()
        {
            Assert.That(clients[0], Is.Not.SameAs(clients[1]));
            foreach (var client in clients)
            {
                Assert.That(client.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
                Assert.That(client.SessionIdentity.SessionToken, Is.Null);
                Assert.That(client.State.HasIdentity, Is.False);
                Assert.That(client.State.TurnDeadlineUnixTimeMilliseconds, Is.Zero);
                Assert.That(client.TryFire(new ClientPosition(0, 0)), Is.False);
                Assert.That(client.SendHeartbeat(), Is.False);
            }
            clientTime = 60000;
            Assert.That(clients[0].CheckConnection(), Is.False);
            Assert.That(adapter.Status.ConnectedClients, Is.Zero);
            Assert.That(adapter.Status.MatchReady, Is.False);
            Assert.That(adapter.ProcessDeadlines(), Is.False);
            Assert.That(transport.Log, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void JoinOrderAssignsRolesAndSecondJoinStartsDeadlineFromServerNow(int first)
        {
            Connect(first);
            Assert.That(identities[first].PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(clients[first].Connection.IsConnected, Is.True);
            Assert.That(adapter.Status.ConnectedClients, Is.EqualTo(1));
            Assert.That(adapter.Status.MatchReady, Is.False);
            Assert.That(clients[first].State.IsYourTurn, Is.False);
            Assert.That(clients[first].State.TurnDeadlineUnixTimeMilliseconds, Is.Zero);
            Assert.That(server.GetSnapshot(identities[first].SessionToken).Error.ErrorCode,
                Is.EqualTo(ProtocolErrorCode.MatchNotReady));
            clock.UnixTimeMilliseconds = 200000;
            Assert.That(adapter.ProcessDeadlines(), Is.False);
            Connect(1 - first);
            Assert.That(identities[1 - first].PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(adapter.Status.ConnectedClients, Is.EqualTo(2));
            Assert.That(adapter.Status.MatchReady, Is.True);
            foreach (var client in clients)
            {
                Assert.That(client.State.MatchStatus, Is.EqualTo(ClientMatchStatus.InProgress));
                Assert.That(client.State.CurrentPlayer, Is.EqualTo(PlayerSlot.PlayerOne));
                Assert.That(client.State.TurnId, Is.EqualTo(1));
                Assert.That(client.State.StateVersion, Is.EqualTo(1));
                Assert.That(client.State.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(215000));
            }
        }

        [Test]
        public void RepeatedConnectRetriesSameInitialJoinWhenResponseWasLost()
        {
            endpoints[0].Configure(new NetworkSettings(lossRate: 1));
            controls[0].Connect();
            Assert.That(clients[0].Connection.State, Is.EqualTo(ClientConnectionState.Connecting));
            Assert.That(clients[0].TryFire(new ClientPosition(0, 0)), Is.False);
            var requestId = clients[0].LastRequestId;
            // The server processed the request, but its response did not reach this runtime.
            var joined = server.Handle(new JoinRequest { RequestId = requestId }).Response;
            endpoints[0].Configure(new NetworkSettings(silentlyDisconnected: true));
            Connect(0);
            Assert.That(clients[0].LastRequestId, Is.EqualTo(requestId));
            Assert.That(identities[0].SessionToken, Is.EqualTo(joined.SessionToken));
            Assert.That(adapter.Status.MatchReady, Is.False);
            Connect(1);
            Assert.That(identities[1].PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingSessionReconnectOrRecreationUsesResumeAndPreservesDeadline(bool recreate)
        {
            Connect(1);
            Connect(0);
            var token = identities[1].SessionToken;
            var deadline = clients[1].State.TurnDeadlineUnixTimeMilliseconds;
            var generation = endpoints[1].Identity.Generation;
            controls[1].Disconnect();
            clock.UnixTimeMilliseconds += 1000;
            Assert.That(server.GetSnapshot(token).Response.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(deadline));
            var joinCount = JoinCount();
            if (recreate) Recreate(1);
            else Connect(1);
            Assert.That(JoinCount(), Is.EqualTo(joinCount));
            Assert.That(identities[1].SessionToken, Is.EqualTo(token));
            Assert.That(clients[1].State.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(clients[1].Connection.IsConnected, Is.True);
            Assert.That(clients[1].State.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(deadline));
            Assert.That(endpoints[1].Identity.Generation,
                recreate ? Is.GreaterThan(generation) : Is.EqualTo(generation));
            controls[1].Disconnect();
            clock.UnixTimeMilliseconds = deadline;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.ProcessPending();
            Assert.That(server.GetSnapshot(token).Response.TurnId, Is.EqualTo(2));
            Assert.That(server.GetSnapshot(token).Response.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(deadline + 15000));
        }

        [Test]
        public void InitialSnapshotArrivingBeforeJoinResponseWaitsForConfirmedIdentity()
        {
            endpoints[0].Configure(new NetworkSettings(lossRate: 1));
            controls[0].Connect();
            var joined = server.Handle(new JoinRequest { RequestId = clients[0].LastRequestId }).Response;
            server.Handle(new JoinRequest { RequestId = "second-server-join" });
            endpoints[0].Configure(new NetworkSettings());
            transport.Send(endpoints[0].Identity, server.GetSnapshot(joined.SessionToken).Response);
            transport.ProcessPending();
            Assert.That(clients[0].State.HasIdentity, Is.False);
            Assert.That(clients[0].State.TurnDeadlineUnixTimeMilliseconds, Is.Zero);
            Assert.That(clients[0].TryFire(new ClientPosition(0, 0)), Is.False);
            transport.Send(endpoints[0].Identity, joined);
            transport.ProcessPending();
            Assert.That(clients[0].Connection.IsConnected, Is.True);
            Assert.That(clients[0].State.HasIdentity, Is.True);
            Assert.That(clients[0].State.MatchStatus, Is.EqualTo(ClientMatchStatus.InProgress));
            Assert.That(clients[0].State.TurnDeadlineUnixTimeMilliseconds, Is.EqualTo(115000));
        }

        [Test]
        public void RecreationBeforeSecondJoinRestoresWaitingSessionAndLaterReceivesSnapshot()
        {
            Connect(1);
            var token = identities[1].SessionToken;
            Recreate(1);
            Assert.That(clients[1].Connection.IsConnected, Is.True);
            Assert.That(clients[1].State.HasIdentity, Is.True);
            Assert.That(clients[1].State.MatchStatus, Is.EqualTo(ClientMatchStatus.WaitingForPlayers));
            Assert.That(clients[1].State.TurnDeadlineUnixTimeMilliseconds, Is.Zero);
            Connect(0);
            Assert.That(identities[1].SessionToken, Is.EqualTo(token));
            Assert.That(clients[1].State.MatchStatus, Is.EqualTo(ClientMatchStatus.InProgress));
            Assert.That(clients[1].State.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
        }

        private int JoinCount() => transport.Log.Count(x =>
            x.Status == TransportLogStatus.Sent && x.MessageType == typeof(JoinRequest).FullName);

        private void Create(int index)
        {
            clients[index] = new BattleClient(identities[index], 5000, () => clientTime,
                () => "startup-" + ++requestNumber);
            endpoints[index] = transport.RegisterClient(index == 0 ? ClientEndpointId.ClientA : ClientEndpointId.ClientB,
                new NetworkSettings(silentlyDisconnected: true), clients[index]);
            adapter.ActivateEndpoint(endpoints[index].Identity);
            clients[index].AttachEndpoint(endpoints[index]);
            controls[index] = new ClientDebugController(clients[index], endpoints[index]);
        }

        private void Connect(int index)
        {
            controls[index].Connect();
            transport.ProcessPending();
        }

        private void Recreate(int index)
        {
            controls[index].Dispose();
            adapter.DeactivateEndpoint(endpoints[index].Identity);
            clients[index].Dispose();
            Create(index);
            Connect(index);
        }

        private sealed class Clock : IServerClock
        {
            public long UnixTimeMilliseconds { get; set; }
        }
    }
}
