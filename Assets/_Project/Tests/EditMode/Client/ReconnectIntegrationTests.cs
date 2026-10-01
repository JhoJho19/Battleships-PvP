using System;
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
    public sealed class ReconnectIntegrationTests
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
        private int requestNumber;
        private double clientTime;

        [SetUp]
        public void SetUp()
        {
            requestNumber = 0;
            clientTime = 0;
            serverClock = new ManualClock { UnixTimeMilliseconds = 100000 };
            server = new BattleServer(new GameRulesConfig(6, 3, 2, 2, 1),
                new Random(104), serverClock, 15000);
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            adapter = new BattleServerTransportAdapter(server, transport);
            transport.RegisterServer(adapter);

            identityA = new ClientSessionIdentity();
            identityB = new ClientSessionIdentity();
            clientA = CreateClient(ClientEndpointId.ClientA, identityA, new NetworkSettings(), out endpointA);
            clientB = CreateClient(ClientEndpointId.ClientB, identityB, new NetworkSettings(), out endpointB);
            clientA.Join();
            clientB.Join();
            transport.ProcessPending();

            Assert.That(identityA.HasSession, Is.True);
            Assert.That(identityB.HasSession, Is.True);
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

        [TestCase(false)]
        [TestCase(true)]
        public void SequentialRecreationInEitherOrderPreservesMatchSessionsAndDeadline(bool reverse)
        {
            var tokenA = identityA.SessionToken;
            var tokenB = identityB.SessionToken;
            var before = Snapshot(tokenA);
            var firstOldGeneration = reverse ? endpointB.Identity.Generation : endpointA.Identity.Generation;

            if (reverse)
            {
                RecreateB();
                RecreateA();
            }
            else
            {
                RecreateA();
                RecreateB();
            }

            Assert.That(identityA.SessionToken, Is.EqualTo(tokenA));
            Assert.That(identityB.SessionToken, Is.EqualTo(tokenB));
            Assert.That(clientA.State.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerOne));
            Assert.That(clientB.State.PlayerSlot, Is.EqualTo(PlayerSlot.PlayerTwo));
            Assert.That(clientA.Connection.State, Is.EqualTo(ClientConnectionState.Connected));
            Assert.That(clientB.Connection.State, Is.EqualTo(ClientConnectionState.Connected));
            Assert.That(clientA.State.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(clientB.State.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(clientB.State.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(clientA.State.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
            Assert.That(clientB.State.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
            Assert.That((reverse ? endpointB : endpointA).Identity.Generation,
                Is.GreaterThan(firstOldGeneration));
            Assert.That(server.Handle(new JoinRequest { RequestId = "third-player" }).IsSuccess, Is.False);
        }

        [Test]
        public void RecreatedEndpointPreservesFaultSettingsEnablesDeliveryAndRejectsOldGeneration()
        {
            endpointA.Configure(new NetworkSettings(120, 30, 0.2, 0.3, true, 77));
            var oldIdentity = endpointA.Identity;
            var oldClient = clientA;
            var token = identityA.SessionToken;

            var previous = endpointA.Settings;
            oldClient.Dispose();
            clientA = CreateClient(ClientEndpointId.ClientA, identityA,
                EnabledCopy(previous), out endpointA);
            clientA.Resume();
            DrainTransport();

            Assert.That(endpointA.Identity.Generation, Is.GreaterThan(oldIdentity.Generation));
            Assert.That(endpointA.Settings.LatencyMilliseconds, Is.EqualTo(120));
            Assert.That(endpointA.Settings.JitterMilliseconds, Is.EqualTo(30));
            Assert.That(endpointA.Settings.LossRate, Is.EqualTo(0.2));
            Assert.That(endpointA.Settings.DuplicateRate, Is.EqualTo(0.3));
            Assert.That(endpointA.Settings.RandomSeed, Is.EqualTo(77));
            Assert.That(endpointA.Settings.SilentlyDisconnected, Is.False);
            Assert.That(identityA.SessionToken, Is.EqualTo(token));

            transport.Send(oldIdentity, new HeartbeatResponse());
            transport.ProcessPending();
            Assert.That(transport.Log.Last().DropReason, Is.EqualTo(TransportDropReason.StaleEndpoint));
        }

        [Test]
        public void ResumeAfterProcessedPendingShotReconcilesAuthoritativeStateWithoutSecondShot()
        {
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            Assert.That(clientA.TryFire(new ClientPosition(0, 0)), Is.True);
            var pendingRequestId = clientA.State.PendingShot.RequestId;

            transport.AdvanceTimeBy(100);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100,
                silentlyDisconnected: true));
            transport.AdvanceTimeBy(100);

            var authoritative = Snapshot(identityA.SessionToken);
            Assert.That(authoritative.StateVersion, Is.EqualTo(2));
            Assert.That(clientA.State.PendingShot, Is.Not.Null);
            Assert.That(clientA.State.PendingShot.RequestId, Is.EqualTo(pendingRequestId));

            var previousSettings = endpointA.Settings;
            RecreateA(EnabledCopy(previousSettings));

            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.StateVersion, Is.EqualTo(authoritative.StateVersion));
            Assert.That(clientA.State.TurnId, Is.EqualTo(authoritative.TurnId));
            Assert.That(clientA.State.OpponentShots.Count, Is.EqualTo(1));
            Assert.That(Snapshot(identityA.SessionToken).StateVersion,
                Is.EqualTo(authoritative.StateVersion));
        }

        [Test]
        public void ResumeAndHeartbeatDoNotResetAuthoritativeDeadline()
        {
            var before = Snapshot(identityA.SessionToken);

            clientA.SendHeartbeat();
            transport.ProcessPending();
            clientA.Resume();
            transport.ProcessPending();
            RecreateA();

            var after = Snapshot(identityA.SessionToken);
            Assert.That(after.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(after.TurnId, Is.EqualTo(before.TurnId));
            Assert.That(after.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(before.TurnDeadlineUnixTimeMilliseconds));
        }

        [Test]
        public void ConnectOnSameRuntimeReconcilesProcessedShotAndKeepsTokenAndEndpoint()
        {
            var oldClient = clientA;
            var oldEndpoint = endpointA.Identity;
            var token = identityA.SessionToken;
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            clientA.TryFire(new ClientPosition(0, 0));
            transport.AdvanceTimeBy(100);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100,
                silentlyDisconnected: true));
            transport.AdvanceTimeBy(100);
            clientTime = 5001;
            clientA.CheckConnection();
            var authoritative = Snapshot(token);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            clientA.Resume();
            Assert.That(clientA.State.PendingShot, Is.Not.Null);
            Assert.That(clientA.Connection.State, Is.EqualTo(ClientConnectionState.Resuming));
            DrainTransport();
            Assert.That(clientA, Is.SameAs(oldClient));
            Assert.That(endpointA.Identity, Is.EqualTo(oldEndpoint));
            Assert.That(identityA.SessionToken, Is.EqualTo(token));
            Assert.That(clientA.Connection.IsConnected, Is.True);
            Assert.That(clientA.State.PendingShot, Is.Null);
            Assert.That(clientA.State.OpponentShots.Count, Is.EqualTo(1));
            Assert.That(clientA.State.StateVersion, Is.EqualTo(authoritative.StateVersion));
            Assert.That(clientA.State.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(authoritative.TurnDeadlineUnixTimeMilliseconds));
            Assert.That(transport.Log.Count(x => x.Direction == TransportDirection.ClientToServer &&
                x.Status == TransportLogStatus.Sent && x.MessageType == typeof(FireRequest).FullName),
                Is.EqualTo(1));
        }

        [Test]
        public void DisposalRemovesOldDeliveriesCallbacksAndStopsOldClientSending()
        {
            var events = 0;
            clientA.StateChanged += () => events++;
            clientA.RequestSent += () => events++;
            var oldClient = clientA;
            var oldEndpoint = endpointA.Identity;
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(latencyMilliseconds: 100));
            clientA.TryFire(new ClientPosition(0, 0));
            transport.Send(oldEndpoint, before);
            Assert.That(transport.PendingCount, Is.EqualTo(2));
            RecreateA();
            var afterDisposeEvents = events;
            Assert.Throws<ObjectDisposedException>(() => oldClient.SendHeartbeat());
            Assert.Throws<ObjectDisposedException>(() => oldClient.TryFire(new ClientPosition(1, 0)));
            transport.Send(oldEndpoint, before);
            DrainTransport();
            Assert.That(events, Is.EqualTo(afterDisposeEvents));
            Assert.That(clientA.State, Is.Not.SameAs(oldClient.State));
            Assert.That(clientA.Connection, Is.Not.SameAs(oldClient.Connection));
            Assert.That(Snapshot(identityA.SessionToken).StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(clientA.State.OpponentShots, Is.Empty);
            Assert.That(adapter.Status.ConnectedClients, Is.EqualTo(2));
        }

        [Test]
        public void DisconnectedPlayerDoesNotStopDeadlinesAndResumeReadsCurrentState()
        {
            var before = Snapshot(identityA.SessionToken);
            endpointA.Configure(new NetworkSettings(silentlyDisconnected: true));
            serverClock.UnixTimeMilliseconds = before.TurnDeadlineUnixTimeMilliseconds;
            Assert.That(adapter.ProcessDeadlines(), Is.True);
            transport.ProcessPending();
            Assert.That(clientA.State.StateVersion, Is.EqualTo(before.StateVersion));
            Assert.That(clientB.State.StateVersion, Is.EqualTo(before.StateVersion + 1));
            endpointA.Configure(new NetworkSettings());
            clientA.Resume();
            DrainTransport();
            Assert.That(clientA.State.StateVersion, Is.EqualTo(clientB.State.StateVersion));
            Assert.That(clientA.State.TurnDeadlineUnixTimeMilliseconds,
                Is.EqualTo(clientB.State.TurnDeadlineUnixTimeMilliseconds));
            Assert.That(clientA.State.TurnId, Is.EqualTo(clientB.State.TurnId));
        }

        [Test]
        public void AdapterRejectsCapturedOldGenerationCallbacksAfterInvalidationAndReplacement()
        {
            using (var routing = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All)))
            {
                var captured = new CapturingReceiver();
                routing.RegisterServer(captured);
                var old = routing.RegisterClient(ClientEndpointId.ClientA, new NetworkSettings(), captured);
                var before = Snapshot(identityA.SessionToken);
                old.Send(new FireRequest
                {
                    RequestId = "captured-old-fire",
                    SessionToken = identityA.SessionToken,
                    TurnId = before.TurnId,
                    Target = new BoardPosition { X = 0, Y = 0 }
                });
                routing.ProcessPending();
                var delivery = captured.Delivery;
                adapter.DeactivateEndpoint(old.Identity);
                adapter.Receive(delivery);
                Assert.That(Snapshot(identityA.SessionToken).StateVersion, Is.EqualTo(before.StateVersion));
                old.Dispose();
                var replacement = routing.RegisterClient(ClientEndpointId.ClientA,
                    new NetworkSettings(), captured);
                adapter.ActivateEndpoint(replacement.Identity);
                adapter.Receive(delivery);
                Assert.That(Snapshot(identityA.SessionToken).StateVersion, Is.EqualTo(before.StateVersion));
                Assert.Throws<InvalidOperationException>(() => adapter.ActivateEndpoint(delivery.Endpoint));
            }
        }

        private sealed class CapturingReceiver : ITransportMessageReceiver
        {
            public TransportDelivery Delivery { get; private set; }
            public void Receive(TransportDelivery delivery) => Delivery = delivery;
        }

        private BattleClient CreateClient(ClientEndpointId endpointId,
            ClientSessionIdentity identity, NetworkSettings settings,
            out ClientTransportEndpoint endpoint)
        {
            var client = new BattleClient(identity, 5000, () => clientTime,
                () => $"request-{++requestNumber}");
            endpoint = transport.RegisterClient(endpointId, settings, client);
            adapter.ActivateEndpoint(endpoint.Identity);
            client.AttachEndpoint(endpoint);
            return client;
        }

        private void RecreateA(NetworkSettings settings = null)
        {
            var previous = settings ?? EnabledCopy(endpointA.Settings);
            clientA.Dispose();
            clientA = CreateClient(ClientEndpointId.ClientA, identityA, previous, out endpointA);
            clientA.Resume();
            DrainTransport();
        }

        private void RecreateB(NetworkSettings settings = null)
        {
            var previous = settings ?? EnabledCopy(endpointB.Settings);
            clientB.Dispose();
            clientB = CreateClient(ClientEndpointId.ClientB, identityB, previous, out endpointB);
            clientB.Resume();
            DrainTransport();
        }

        private void DrainTransport()
        {
            for (var i = 0; i < 10 && transport.PendingCount > 0; i++)
                transport.AdvanceTimeBy(1000);
        }

        private MatchSnapshot Snapshot(string token) => server.Handle(new ResumeRequest
        {
            RequestId = "server-read-" + ++requestNumber,
            SessionToken = token
        }).Response;

        private static NetworkSettings EnabledCopy(NetworkSettings settings) =>
            new NetworkSettings(settings.LatencyMilliseconds, settings.JitterMilliseconds,
                settings.LossRate, settings.DuplicateRate, false, settings.RandomSeed);

        private sealed class ManualClock : IServerClock
        {
            public long UnixTimeMilliseconds { get; set; }
        }
    }
}
