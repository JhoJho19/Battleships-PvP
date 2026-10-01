using System.Collections.Generic;
using Battleships.Client;
using Battleships.Networking;
using Battleships.Presentation;
using Battleships.Protocol;
using NUnit.Framework;

namespace Battleships.Tests.Presentation
{
    public sealed class ClientDebugControllerTests
    {
        private InProcessTransport transport;
        private RecordingReceiver server;
        private BattleClient clientA;
        private BattleClient clientB;
        private ClientTransportEndpoint endpointA;
        private ClientTransportEndpoint endpointB;
        private ClientDebugController controllerA;
        private ClientDebugController controllerB;

        [SetUp]
        public void SetUp()
        {
            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            server = new RecordingReceiver();
            transport.RegisterServer(server);
            clientA = new BattleClient(() => "request-a");
            clientB = new BattleClient(() => "request-b");
            endpointA = transport.RegisterClient(ClientEndpointId.ClientA, new NetworkSettings(), clientA);
            endpointB = transport.RegisterClient(ClientEndpointId.ClientB, new NetworkSettings(), clientB);
            clientA.AttachEndpoint(endpointA);
            clientB.AttachEndpoint(endpointB);
            controllerA = new ClientDebugController(clientA, endpointA);
            controllerB = new ClientDebugController(clientB, endpointB);
        }

        [TearDown]
        public void TearDown()
        {
            controllerA?.Dispose();
            controllerB?.Dispose();
            clientA?.Dispose();
            clientB?.Dispose();
            transport?.Dispose();
        }

        [Test]
        public void SettingsAreValidatedConvertedAndAppliedOnlyToSelectedEndpoint()
        {
            Assert.That(controllerA.SetLatency("-15"), Is.EqualTo("0"));
            Assert.That(controllerA.SetJitter("bad value"), Is.EqualTo("0"));
            Assert.That(controllerA.SetLossPercent("25"), Is.EqualTo("25"));
            Assert.That(controllerA.SetDuplicatePercent("150"), Is.EqualTo("100"));

            Assert.That(endpointA.Settings.LatencyMilliseconds, Is.Zero);
            Assert.That(endpointA.Settings.JitterMilliseconds, Is.Zero);
            Assert.That(endpointA.Settings.LossRate, Is.EqualTo(0.25d));
            Assert.That(endpointA.Settings.DuplicateRate, Is.EqualTo(1d));
            Assert.That(endpointB.Settings.LossRate, Is.Zero);
            Assert.That(endpointB.Settings.DuplicateRate, Is.Zero);

            controllerB.SetLatency("120.5");
            controllerB.SetJitter("30");
            Assert.That(endpointB.Settings.LatencyMilliseconds, Is.EqualTo(120.5d));
            Assert.That(endpointB.Settings.JitterMilliseconds, Is.EqualTo(30d));
            Assert.That(endpointA.Settings.LatencyMilliseconds, Is.Zero);
        }

        [Test]
        public void DisconnectAndConnectChangeOnlyManualDeliveryState()
        {
            controllerA.Disconnect();
            Assert.That(endpointA.Settings.SilentlyDisconnected, Is.True);
            Assert.That(controllerA.ConnectedText, Is.EqualTo("No"));
            Assert.That(endpointB.Settings.SilentlyDisconnected, Is.False);

            endpointA.Send(new JoinRequest { RequestId = "lost" });
            endpointB.Send(new JoinRequest { RequestId = "other-client" });
            transport.ProcessPending();
            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
            Assert.That(((JoinRequest)server.Deliveries[0].Message).RequestId, Is.EqualTo("other-client"));

            controllerA.Connect();
            endpointA.Send(new JoinRequest { RequestId = "new-message" });
            transport.ProcessPending();
            Assert.That(server.Deliveries.Count, Is.EqualTo(2));
            Assert.That(((JoinRequest)server.Deliveries[1].Message).RequestId, Is.EqualTo("new-message"));
            Assert.That(server.Deliveries.Exists(x =>
                ((JoinRequest)x.Message).RequestId == "lost"), Is.False);
        }

        [Test]
        public void NetworkLogToggleStopsAndResumesRecordingWithoutStoppingDelivery()
        {
            var entriesBefore = transport.Log.Count;
            controllerA.SetNetworkLogEnabled(false);
            endpointA.Send(new JoinRequest { RequestId = "unlogged" });
            transport.ProcessPending();

            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
            Assert.That(transport.Log.Count, Is.EqualTo(entriesBefore));

            controllerA.SetNetworkLogEnabled(true);
            endpointA.Send(new JoinRequest { RequestId = "logged" });
            transport.ProcessPending();
            Assert.That(server.Deliveries.Count, Is.EqualTo(2));
            Assert.That(transport.Log.Count, Is.EqualTo(entriesBefore + 2));
        }

        [Test]
        public void RuntimeFieldsAndRecreateEntryPointRemainClientSpecific()
        {
            var requested = new List<ClientEndpointId>();
            controllerA.RecreateRequested += requested.Add;

            transport.Send(endpointA.Identity, new JoinResponse
            {
                RequestId = "join-a",
                SessionToken = "session-a",
                PlayerSlot = PlayerSlot.PlayerOne
            });
            transport.ProcessPending();
            controllerA.RequestRecreate();

            Assert.That(controllerA.EndpointText, Is.EqualTo("Client A / gen 1"));
            Assert.That(controllerA.RecentEventsText, Does.Contain("Joined match"));
            Assert.That(controllerA.RecentEventsText, Does.Contain("reserved for stage 4.9"));
            Assert.That(controllerB.RecentEventsText, Is.Empty);
            Assert.That(requested, Is.EqualTo(new[] { ClientEndpointId.ClientA }));
        }

        private sealed class RecordingReceiver : ITransportMessageReceiver
        {
            public List<TransportDelivery> Deliveries { get; } = new List<TransportDelivery>();
            public void Receive(TransportDelivery delivery) => Deliveries.Add(delivery);
        }
    }
}
