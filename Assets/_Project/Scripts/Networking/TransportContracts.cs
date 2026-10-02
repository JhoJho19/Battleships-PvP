using System;
using System.Collections.Generic;

namespace Battleships.Networking
{
    public enum ClientEndpointId
    {
        ClientA,
        ClientB
    }

    public enum TransportDirection
    {
        ClientToServer,
        ServerToClient
    }

    public enum TransportLogStatus
    {
        Sent,
        Received,
        Dropped,
        Duplicated
    }

    public enum TransportDropReason
    {
        None,
        Loss,
        Disconnected,
        StaleEndpoint
    }

    public readonly struct EndpointIdentity : IEquatable<EndpointIdentity>
    {
        public ClientEndpointId EndpointId { get; }
        public long Generation { get; }

        public EndpointIdentity(ClientEndpointId endpointId, long generation)
        {
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
            EndpointId = endpointId;
            Generation = generation;
        }

        public bool Equals(EndpointIdentity other) =>
            EndpointId == other.EndpointId && Generation == other.Generation;

        public override bool Equals(object obj) => obj is EndpointIdentity other && Equals(other);
        public override int GetHashCode() => ((int)EndpointId * 397) ^ Generation.GetHashCode();
        public override string ToString() => $"{EndpointId}#{Generation}";
        public static bool operator ==(EndpointIdentity left, EndpointIdentity right) => left.Equals(right);
        public static bool operator !=(EndpointIdentity left, EndpointIdentity right) => !left.Equals(right);
    }

    public sealed class NetworkSettings
    {
        public double LatencyMilliseconds { get; }
        public double JitterMilliseconds { get; }
        public double LossRate { get; }
        public double DuplicateRate { get; }
        public bool SilentlyDisconnected { get; }
        public int RandomSeed { get; }

        public NetworkSettings(double latencyMilliseconds = 0, double jitterMilliseconds = 0,
            double lossRate = 0, double duplicateRate = 0, bool silentlyDisconnected = false,
            int randomSeed = 0)
        {
            if (!IsFinite(latencyMilliseconds) || latencyMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(latencyMilliseconds));
            if (!IsFinite(jitterMilliseconds) || jitterMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(jitterMilliseconds));
            if (!IsFinite(lossRate) || lossRate < 0 || lossRate > 1)
                throw new ArgumentOutOfRangeException(nameof(lossRate));
            if (!IsFinite(duplicateRate) || duplicateRate < 0 || duplicateRate > 1)
                throw new ArgumentOutOfRangeException(nameof(duplicateRate));

            LatencyMilliseconds = latencyMilliseconds;
            JitterMilliseconds = jitterMilliseconds;
            LossRate = lossRate;
            DuplicateRate = duplicateRate;
            SilentlyDisconnected = silentlyDisconnected;
            RandomSeed = randomSeed;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class TransportDelivery
    {
        public EndpointIdentity Endpoint { get; }
        public TransportDirection Direction { get; }
        public string MessageType { get; }
        public object Message { get; }

        internal TransportDelivery(EndpointIdentity endpoint, TransportDirection direction,
            string messageType, object message)
        {
            Endpoint = endpoint;
            Direction = direction;
            MessageType = messageType;
            Message = message;
        }
    }

    public sealed class TransportLogEntry
    {
        public EndpointIdentity Endpoint { get; }
        public TransportDirection Direction { get; }
        public string MessageType { get; }
        public TransportLogStatus Status { get; }
        public TransportDropReason DropReason { get; }
        public double TimestampMilliseconds { get; }
        // Diagnostic wall clock only; scheduling and deterministic tests use TimestampMilliseconds.
        public DateTimeOffset LocalTimestamp { get; }

        internal TransportLogEntry(EndpointIdentity endpoint, TransportDirection direction,
            string messageType, TransportLogStatus status, TransportDropReason dropReason,
            double timestampMilliseconds)
        {
            Endpoint = endpoint;
            Direction = direction;
            MessageType = messageType;
            Status = status;
            DropReason = dropReason;
            TimestampMilliseconds = timestampMilliseconds;
            LocalTimestamp = DateTimeOffset.Now;
        }
    }

    public interface ITransportMessageReceiver
    {
        void Receive(TransportDelivery delivery);
    }

    public interface IServerTransportSender
    {
        void Send(EndpointIdentity destination, object message);
    }

    public interface IMessageSerializer
    {
        SerializedMessage Serialize(object message);
        object Deserialize(string messageType, byte[] payload);
    }

    public readonly struct SerializedMessage
    {
        public string MessageType { get; }
        public byte[] Payload { get; }

        public SerializedMessage(string messageType, byte[] payload)
        {
            MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    public static class ProtocolMessageTypes
    {
        private static readonly Type[] Types =
        {
            typeof(Protocol.JoinRequest),
            typeof(Protocol.ResumeRequest),
            typeof(Protocol.FireRequest),
            typeof(Protocol.HeartbeatRequest),
            typeof(Protocol.JoinResponse),
            typeof(Protocol.FireResponse),
            typeof(Protocol.MatchSnapshot),
            typeof(Protocol.HeartbeatResponse),
            typeof(Protocol.ErrorResponse)
        };

        public static IReadOnlyList<Type> All => Types;
    }
}
