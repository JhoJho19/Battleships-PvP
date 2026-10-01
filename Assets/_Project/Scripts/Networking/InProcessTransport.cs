using System;
using System.Collections.Generic;

namespace Battleships.Networking
{
    public sealed class InProcessTransport : IServerTransportSender, IDisposable
    {
        private readonly IMessageSerializer serializer;
        private readonly Dictionary<ClientEndpointId, EndpointRegistration> endpoints =
            new Dictionary<ClientEndpointId, EndpointRegistration>();
        private readonly Dictionary<ClientEndpointId, long> generations =
            new Dictionary<ClientEndpointId, long>();
        private readonly List<PendingDelivery> pending = new List<PendingDelivery>();
        private readonly List<TransportLogEntry> log = new List<TransportLogEntry>();
        private readonly IReadOnlyList<TransportLogEntry> readOnlyLog;
        private ITransportMessageReceiver serverReceiver;
        private long nextSequence;
        private bool disposed;

        public double CurrentTimeMilliseconds { get; private set; }
        public int PendingCount => pending.Count;
        public IReadOnlyList<TransportLogEntry> Log => readOnlyLog;

        public InProcessTransport(IMessageSerializer serializer)
        {
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            readOnlyLog = log.AsReadOnly();
        }

        public IDisposable RegisterServer(ITransportMessageReceiver receiver)
        {
            ThrowIfDisposed();
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (serverReceiver != null) throw new InvalidOperationException("A server receiver is already registered.");
            serverReceiver = receiver;
            return new ServerRegistration(this, receiver);
        }

        public ClientTransportEndpoint RegisterClient(ClientEndpointId endpointId,
            NetworkSettings settings, ITransportMessageReceiver receiver)
        {
            ThrowIfDisposed();
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (endpoints.ContainsKey(endpointId))
                throw new InvalidOperationException($"{endpointId} is already registered.");

            generations.TryGetValue(endpointId, out var previousGeneration);
            var identity = new EndpointIdentity(endpointId, previousGeneration + 1);
            generations[endpointId] = identity.Generation;
            endpoints.Add(endpointId, new EndpointRegistration(identity, settings, receiver));
            return new ClientTransportEndpoint(this, identity);
        }

        public void Configure(EndpointIdentity identity, NetworkSettings settings)
        {
            ThrowIfDisposed();
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            GetCurrent(identity).Configure(settings);
        }

        public void Send(EndpointIdentity destination, object message)
        {
            ThrowIfDisposed();
            Schedule(TransportDirection.ServerToClient, destination, message);
        }

        public void ProcessPending()
        {
            ThrowIfDisposed();

            while (true)
            {
                var nextIndex = FindNextDueIndex();
                if (nextIndex < 0) return;
                var delivery = pending[nextIndex];
                pending.RemoveAt(nextIndex);
                Deliver(delivery);
            }
        }

        public void AdvanceTimeBy(double milliseconds)
        {
            ThrowIfDisposed();
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            CurrentTimeMilliseconds += milliseconds;
            ProcessPending();
        }

        public void Reset()
        {
            ThrowIfDisposed();
            pending.Clear();
            endpoints.Clear();
            serverReceiver = null;
            log.Clear();
            CurrentTimeMilliseconds = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            pending.Clear();
            endpoints.Clear();
            serverReceiver = null;
            log.Clear();
            disposed = true;
        }

        internal void SendFromClient(EndpointIdentity source, object message)
        {
            ThrowIfDisposed();
            Schedule(TransportDirection.ClientToServer, source, message);
        }

        internal void Unregister(EndpointIdentity identity)
        {
            if (disposed) return;
            if (!endpoints.TryGetValue(identity.EndpointId, out var registration) ||
                registration.Identity != identity) return;

            endpoints.Remove(identity.EndpointId);
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Endpoint != identity) continue;
                var removed = pending[i];
                pending.RemoveAt(i);
                AddLog(removed.Endpoint, removed.Direction, removed.MessageType,
                    TransportLogStatus.Dropped, TransportDropReason.StaleEndpoint);
            }
        }

        private void Schedule(TransportDirection direction, EndpointIdentity endpoint, object message)
        {
            var serialized = serializer.Serialize(message);
            AddLog(endpoint, direction, serialized.MessageType, TransportLogStatus.Sent);

            if (!TryGetCurrent(endpoint, out var registration))
            {
                AddLog(endpoint, direction, serialized.MessageType, TransportLogStatus.Dropped,
                    TransportDropReason.StaleEndpoint);
                return;
            }

            if (registration.Settings.SilentlyDisconnected)
            {
                AddLog(endpoint, direction, serialized.MessageType, TransportLogStatus.Dropped,
                    TransportDropReason.Disconnected);
                return;
            }

            if (registration.Settings.LossRate >= 1 ||
                registration.Settings.LossRate > 0 && registration.Random.NextDouble() < registration.Settings.LossRate)
            {
                AddLog(endpoint, direction, serialized.MessageType, TransportLogStatus.Dropped,
                    TransportDropReason.Loss);
                return;
            }

            var copies = 1;
            if (registration.Settings.DuplicateRate >= 1 ||
                registration.Settings.DuplicateRate > 0 &&
                registration.Random.NextDouble() < registration.Settings.DuplicateRate)
            {
                copies++;
                AddLog(endpoint, direction, serialized.MessageType, TransportLogStatus.Duplicated);
            }

            for (var i = 0; i < copies; i++)
            {
                var delay = registration.Settings.LatencyMilliseconds;
                if (registration.Settings.JitterMilliseconds > 0)
                    delay += (registration.Random.NextDouble() * 2 - 1) * registration.Settings.JitterMilliseconds;
                if (delay < 0) delay = 0;
                pending.Add(new PendingDelivery(endpoint, direction, serialized.MessageType,
                    (byte[])serialized.Payload.Clone(), CurrentTimeMilliseconds + delay, nextSequence++));
            }
        }

        private void Deliver(PendingDelivery delivery)
        {
            if (!TryGetCurrent(delivery.Endpoint, out var registration))
            {
                AddLog(delivery.Endpoint, delivery.Direction, delivery.MessageType,
                    TransportLogStatus.Dropped, TransportDropReason.StaleEndpoint);
                return;
            }

            if (registration.Settings.SilentlyDisconnected)
            {
                AddLog(delivery.Endpoint, delivery.Direction, delivery.MessageType,
                    TransportLogStatus.Dropped, TransportDropReason.Disconnected);
                return;
            }

            var receiver = delivery.Direction == TransportDirection.ClientToServer
                ? serverReceiver
                : registration.Receiver;
            if (receiver == null)
            {
                AddLog(delivery.Endpoint, delivery.Direction, delivery.MessageType,
                    TransportLogStatus.Dropped, TransportDropReason.StaleEndpoint);
                return;
            }

            var message = serializer.Deserialize(delivery.MessageType, delivery.Payload);
            AddLog(delivery.Endpoint, delivery.Direction, delivery.MessageType, TransportLogStatus.Received);
            receiver.Receive(new TransportDelivery(delivery.Endpoint, delivery.Direction,
                delivery.MessageType, message));
        }

        private int FindNextDueIndex()
        {
            var result = -1;
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i].DeliveryTimeMilliseconds > CurrentTimeMilliseconds) continue;
                if (result < 0 || pending[i].DeliveryTimeMilliseconds < pending[result].DeliveryTimeMilliseconds ||
                    pending[i].DeliveryTimeMilliseconds == pending[result].DeliveryTimeMilliseconds &&
                    pending[i].Sequence < pending[result].Sequence)
                    result = i;
            }
            return result;
        }

        private EndpointRegistration GetCurrent(EndpointIdentity identity)
        {
            if (TryGetCurrent(identity, out var registration)) return registration;
            throw new InvalidOperationException($"Endpoint is not active: {identity}");
        }

        private bool TryGetCurrent(EndpointIdentity identity, out EndpointRegistration registration) =>
            endpoints.TryGetValue(identity.EndpointId, out registration) && registration.Identity == identity;

        private void AddLog(EndpointIdentity endpoint, TransportDirection direction, string messageType,
            TransportLogStatus status, TransportDropReason dropReason = TransportDropReason.None) =>
            log.Add(new TransportLogEntry(endpoint, direction, messageType, status, dropReason,
                CurrentTimeMilliseconds));

        private void UnregisterServer(ITransportMessageReceiver receiver)
        {
            if (!disposed && ReferenceEquals(serverReceiver, receiver)) serverReceiver = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(InProcessTransport));
        }

        private sealed class EndpointRegistration
        {
            public EndpointIdentity Identity { get; }
            public ITransportMessageReceiver Receiver { get; }
            public NetworkSettings Settings { get; private set; }
            public Random Random { get; private set; }

            public EndpointRegistration(EndpointIdentity identity, NetworkSettings settings,
                ITransportMessageReceiver receiver)
            {
                Identity = identity;
                Receiver = receiver;
                Configure(settings);
            }

            public void Configure(NetworkSettings settings)
            {
                Settings = settings;
                Random = new Random(settings.RandomSeed);
            }
        }

        private readonly struct PendingDelivery
        {
            public EndpointIdentity Endpoint { get; }
            public TransportDirection Direction { get; }
            public string MessageType { get; }
            public byte[] Payload { get; }
            public double DeliveryTimeMilliseconds { get; }
            public long Sequence { get; }

            public PendingDelivery(EndpointIdentity endpoint, TransportDirection direction,
                string messageType, byte[] payload, double deliveryTimeMilliseconds, long sequence)
            {
                Endpoint = endpoint;
                Direction = direction;
                MessageType = messageType;
                Payload = payload;
                DeliveryTimeMilliseconds = deliveryTimeMilliseconds;
                Sequence = sequence;
            }
        }

        private sealed class ServerRegistration : IDisposable
        {
            private InProcessTransport owner;
            private readonly ITransportMessageReceiver receiver;

            public ServerRegistration(InProcessTransport owner, ITransportMessageReceiver receiver)
            {
                this.owner = owner;
                this.receiver = receiver;
            }

            public void Dispose()
            {
                owner?.UnregisterServer(receiver);
                owner = null;
            }
        }
    }

    public sealed class ClientTransportEndpoint : IDisposable
    {
        private InProcessTransport owner;
        public EndpointIdentity Identity { get; }

        internal ClientTransportEndpoint(InProcessTransport owner, EndpointIdentity identity)
        {
            this.owner = owner;
            Identity = identity;
        }

        public void Send(object message)
        {
            if (owner == null) throw new ObjectDisposedException(nameof(ClientTransportEndpoint));
            owner.SendFromClient(Identity, message);
        }

        public void Configure(NetworkSettings settings)
        {
            if (owner == null) throw new ObjectDisposedException(nameof(ClientTransportEndpoint));
            owner.Configure(Identity, settings);
        }

        public void Dispose()
        {
            owner?.Unregister(Identity);
            owner = null;
        }
    }
}
