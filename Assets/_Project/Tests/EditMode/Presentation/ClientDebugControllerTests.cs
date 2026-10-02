using System;
using System.Collections.Generic;
using System.Globalization;
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
            transport.Send(endpointA.Identity, new JoinResponse
            {
                RequestId = "join-a",
                SessionToken = "session-a",
                PlayerSlot = PlayerSlot.PlayerOne
            });
            transport.ProcessPending();
            controllerA.Disconnect();
            Assert.That(endpointA.Settings.SilentlyDisconnected, Is.True);
            Assert.That(controllerA.ConnectedText, Is.EqualTo("Connected"));
            Assert.That(endpointB.Settings.SilentlyDisconnected, Is.False);

            endpointA.Send(new JoinRequest { RequestId = "lost" });
            endpointB.Send(new JoinRequest { RequestId = "other-client" });
            transport.ProcessPending();
            Assert.That(server.Deliveries.Count, Is.EqualTo(1));
            Assert.That(((JoinRequest)server.Deliveries[0].Message).RequestId, Is.EqualTo("other-client"));

            controllerA.Connect();
            transport.ProcessPending();
            Assert.That(server.Deliveries.Count, Is.EqualTo(2));
            Assert.That(server.Deliveries[1].Message, Is.TypeOf<ResumeRequest>());
            Assert.That(((ResumeRequest)server.Deliveries[1].Message).SessionToken,
                Is.EqualTo("session-a"));
            Assert.That(controllerA.ConnectedText, Is.EqualTo("Resuming"));
            Assert.That(server.Deliveries.Exists(x =>
                x.Message is JoinRequest request && request.RequestId == "lost"), Is.False);
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
            Assert.That(controllerA.RecentEventsText, Does.Contain("Client recreation requested"));
            Assert.That(controllerB.RecentEventsText, Is.Empty);
            Assert.That(requested, Is.EqualTo(new[] { ClientEndpointId.ClientA }));
        }

        [Test]
        public void RecentEventsRetainScrollableChronologicalHistoryAndStableLocalTimestamps()
        {
            var before = DateTimeOffset.Now;
            for (var i = 0; i < 30; i++) controllerA.ReportRuntimeEvent($"Event {i}");
            var after = DateTimeOffset.Now;
            var rendered = controllerA.RecentEventsText;
            var lines = rendered.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(30));
            for (var i = 0; i < lines.Length; i++)
            {
                Assert.That(lines[i], Does.Match(@"^\[\d{2}:\d{2}:\d{2}\] Event " + i + "$"));
                var time = DateTime.ParseExact(lines[i].Substring(1, 8), "HH:mm:ss",
                    CultureInfo.InvariantCulture).TimeOfDay;
                // Compare at displayed second precision, allowing a midnight boundary.
                var firstSecond = new TimeSpan(before.Hour, before.Minute, before.Second);
                var lastSecond = new TimeSpan(after.Hour, after.Minute, after.Second);
                Assert.That(firstSecond <= lastSecond
                    ? time >= firstSecond && time <= lastSecond
                    : time >= firstSecond || time <= lastSecond, Is.True);
            }
            controllerA.SetLatency("10");
            Assert.That(controllerA.RecentEventsText, Is.EqualTo(rendered));
            Assert.That(controllerB.RecentEventsText, Is.Empty);

            for (var i = 30; i < 205; i++) controllerA.ReportRuntimeEvent($"Event {i}");
            lines = controllerA.RecentEventsText.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(200));
            Assert.That(lines[0], Does.EndWith("Event 5"));
            Assert.That(lines[199], Does.EndWith("Event 204"));
        }

        private sealed class RecordingReceiver : ITransportMessageReceiver
        {
            public List<TransportDelivery> Deliveries { get; } = new List<TransportDelivery>();
            public void Receive(TransportDelivery delivery) => Deliveries.Add(delivery);
        }
    }
}
