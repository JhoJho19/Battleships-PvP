using System;
using Battleships.Networking;
using Battleships.Protocol;

namespace Battleships.Client
{
    public sealed class BattleClient : ITransportMessageReceiver, IDisposable
    {
        private readonly Func<string> requestIdFactory;
        private ClientTransportEndpoint endpoint;
        private bool disposed;

        public ClientState State { get; } = new ClientState();
        public ClientConnectionMonitor Connection { get; } = new ClientConnectionMonitor();
        public string LastRequestId { get; private set; }

        public event Action StateChanged;
        public event Action<string> RuntimeEvent;
        public event Action RequestSent;

        public BattleClient(Func<string> requestIdFactory = null)
        {
            this.requestIdFactory = requestIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        }

        public void AttachEndpoint(ClientTransportEndpoint transportEndpoint)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleClient));
            if (endpoint != null) throw new InvalidOperationException("A transport endpoint is already attached.");
            endpoint = transportEndpoint ?? throw new ArgumentNullException(nameof(transportEndpoint));
            Connection.Register(endpoint.Identity);
            StateChanged?.Invoke();
        }

        public void Join()
        {
            EnsureReady();
            Send(new JoinRequest { RequestId = NextRequestId() });
        }

        public bool TryFire(ClientPosition target)
        {
            EnsureReady();
            if (!State.CanFire(target)) return false;
            var requestId = NextRequestId();
            if (!State.TryBeginShot(requestId, target))
                throw new InvalidOperationException("The client state changed while creating a shot request.");

            LastRequestId = requestId;
            StateChanged?.Invoke();
            RuntimeEvent?.Invoke($"Fire {target} sent");
            Send(new FireRequest
            {
                RequestId = requestId,
                SessionToken = State.SessionToken,
                TurnId = State.TurnId,
                Target = new BoardPosition { X = target.X, Y = target.Y }
            });
            return true;
        }

        public void Receive(TransportDelivery delivery)
        {
            if (disposed) return;
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            if (delivery.Direction != TransportDirection.ServerToClient ||
                endpoint == null || delivery.Endpoint != endpoint.Identity) return;

            switch (delivery.Message)
            {
                case JoinResponse response:
                    if (State.ApplyJoin(response))
                    {
                        RuntimeEvent?.Invoke("Joined match");
                        StateChanged?.Invoke();
                    }
                    break;
                case MatchSnapshot snapshot:
                    if (State.ApplySnapshot(snapshot))
                    {
                        RuntimeEvent?.Invoke("Snapshot received");
                        StateChanged?.Invoke();
                    }
                    break;
                case FireResponse response:
                    var pending = State.PendingShot;
                    if (!State.ApplyFireResponse(response)) break;
                    RuntimeEvent?.Invoke(response.Accepted
                        ? $"Fire {pending.Target} confirmed: {response.Result}"
                        : $"Fire {pending.Target} rejected: {response.ErrorCode}");
                    StateChanged?.Invoke();
                    break;
                case ErrorResponse response:
                    RuntimeEvent?.Invoke($"Request rejected: {response.ErrorCode}");
                    break;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            endpoint?.Dispose();
            endpoint = null;
            Connection.Unregister();
            StateChanged = null;
            RuntimeEvent = null;
            RequestSent = null;
        }

        private string NextRequestId()
        {
            var requestId = requestIdFactory();
            if (string.IsNullOrWhiteSpace(requestId))
                throw new InvalidOperationException("The request id factory returned an empty value.");
            return requestId;
        }

        private void Send(object message)
        {
            endpoint.Send(message);
            RequestSent?.Invoke();
        }

        private void EnsureReady()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleClient));
            if (endpoint == null) throw new InvalidOperationException("No transport endpoint is attached.");
        }
    }
}
