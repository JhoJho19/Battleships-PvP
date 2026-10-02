using System;
using System.Globalization;
using Battleships.Networking;
using Battleships.Presentation;
using Battleships.Protocol;
using NUnit.Framework;

namespace Battleships.Tests.Presentation
{
    public sealed class DebugLogFormattingTests
    {
        [TestCase(14, 7, 3, "14:07:03")]
        [TestCase(0, 0, 0, "00:00:00")]
        [TestCase(23, 59, 59, "23:59:59")]
        public void TimestampUsesStoredLocalClockAndInvariant24HourFormat(int hour, int minute,
            int second, string expected)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
                var timestamp = new DateTimeOffset(2026, 10, 2, hour, minute, second, 987,
                    TimeSpan.FromHours(3));
                Assert.That(DebugLogFormatting.FormatTimestamp(timestamp), Is.EqualTo(expected));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void TransportCapturesCreationClockWithoutChangingInternalTimeOrDiagnosticFields()
        {
            using (var transport = new InProcessTransport(
                       new XmlMessageSerializer(ProtocolMessageTypes.All)))
            {
                var receiver = new RecordingReceiver();
                transport.RegisterServer(receiver);
                var endpoint = transport.RegisterClient(ClientEndpointId.ClientA,
                    new NetworkSettings(duplicateRate: 1), receiver);
                transport.AdvanceTimeBy(42000);
                var beforeCreation = DateTimeOffset.Now;
                endpoint.Send(new HeartbeatRequest());
                transport.ProcessPending();
                transport.Send(endpoint.Identity, new HeartbeatResponse());
                transport.ProcessPending();
                endpoint.Configure(new NetworkSettings(lossRate: 1));
                endpoint.Send(new HeartbeatRequest());
                var afterCreation = DateTimeOffset.Now;

                var statuses = new System.Collections.Generic.HashSet<TransportLogStatus>();
                foreach (var entry in transport.Log)
                {
                    Assert.That(entry.LocalTimestamp, Is.InRange(beforeCreation, afterCreation));
                    Assert.That(entry.TimestampMilliseconds, Is.EqualTo(42000));
                    var formatted = DebugLogFormatting.FormatTransportEntry(entry);
                    var expected = $"[{DebugLogFormatting.FormatTimestamp(entry.LocalTimestamp)}] " +
                                   $"{entry.Status.ToString().ToUpperInvariant()} {entry.Direction} " +
                                   $"{entry.Endpoint} {entry.MessageType}" +
                                   (entry.DropReason == TransportDropReason.None
                                       ? string.Empty : $" ({entry.DropReason})");
                    Assert.That(formatted, Is.EqualTo(expected));
                    Assert.That(formatted, Does.Match(@"^\[\d{2}:\d{2}:\d{2}\] "));
                    statuses.Add(entry.Status);
                }
                Assert.That(statuses, Is.EquivalentTo(new[] { TransportLogStatus.Sent,
                    TransportLogStatus.Received, TransportLogStatus.Dropped,
                    TransportLogStatus.Duplicated }));

                var original = transport.Log[0];
                var originalText = DebugLogFormatting.FormatTransportEntry(original);
                transport.AdvanceTimeBy(10000);
                Assert.That(DebugLogFormatting.FormatTransportEntry(original), Is.EqualTo(originalText));
                Assert.That(original.TimestampMilliseconds, Is.EqualTo(42000));
            }
        }

        private sealed class RecordingReceiver : ITransportMessageReceiver
        {
            public void Receive(TransportDelivery delivery) { }
        }
    }
}
